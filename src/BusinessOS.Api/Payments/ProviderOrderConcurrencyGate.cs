using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace BusinessOS.Api.Payments;

public interface IProviderOrderConcurrencyGate
{
    ValueTask<IAsyncDisposable> AcquireAsync(
        string provider,
        string providerOrderId,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryProviderOrderConcurrencyGate : IProviderOrderConcurrencyGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string provider,
        string providerOrderId,
        CancellationToken cancellationToken = default)
    {
        var key = ProviderOrderConcurrencyKey.Text(provider, providerOrderId);
        var gate = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new InMemoryLease(gate);
    }

    private sealed class InMemoryLease : IAsyncDisposable
    {
        private SemaphoreSlim? _gate;

        public InMemoryLease(SemaphoreSlim gate) => _gate = gate;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class PostgresProviderOrderConcurrencyGate : IProviderOrderConcurrencyGate, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string? _runtimeRole;

    public PostgresProviderOrderConcurrencyGate(
        string connectionString,
        string? runtimeRole = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Commerce connection string is required.", nameof(connectionString));
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _runtimeRole = runtimeRole;
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string provider,
        string providerOrderId,
        CancellationToken cancellationToken = default)
    {
        var key = ProviderOrderConcurrencyKey.Hash(provider, providerOrderId);
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(
                connection, _runtimeRole, cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteScalarAsync(cancellationToken);
            return new PostgresLease(connection, key);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private sealed class PostgresLease : IAsyncDisposable
    {
        private NpgsqlConnection? _connection;
        private readonly long _key;

        public PostgresLease(NpgsqlConnection connection, long key)
        {
            _connection = connection;
            _key = key;
        }

        public async ValueTask DisposeAsync()
        {
            var connection = Interlocked.Exchange(ref _connection, null);
            if (connection is null)
                return;
            try
            {
                await using var command = new NpgsqlCommand(
                    "SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", _key);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}

internal static class ProviderOrderConcurrencyKey
{
    public static string Text(string provider, string providerOrderId)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Payment provider is required.", nameof(provider));
        if (string.IsNullOrWhiteSpace(providerOrderId))
            throw new ArgumentException("Provider order id is required.", nameof(providerOrderId));
        return $"{provider.Trim().ToLowerInvariant()}|{providerOrderId.Trim()}";
    }

    public static long Hash(string provider, string providerOrderId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Text(provider, providerOrderId)));
        return BinaryPrimitives.ReadInt64BigEndian(bytes);
    }
}
