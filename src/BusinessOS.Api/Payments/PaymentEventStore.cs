using BusinessOS.Payments;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Payments;

public interface IPaymentEventStore
{
    Task<PaymentProcessResult> ProcessAsync(
        string provider,
        PaymentWebhookMessage message,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryPaymentEventStore : IPaymentEventStore
{
    private readonly PaymentProcessor _processor;

    public InMemoryPaymentEventStore(PaymentProcessor processor) =>
        _processor = processor;

    public Task<PaymentProcessResult> ProcessAsync(
        string provider,
        PaymentWebhookMessage message,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_processor.Process(message));
}
public sealed class PostgresPaymentEventStore : IPaymentEventStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresPaymentEventStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Payment connection string is required.", nameof(connectionString));
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<PaymentProcessResult> ProcessAsync(
        string provider,
        PaymentWebhookMessage message,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);
        Validate(message);

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var eventPayment = await LoadPaymentByEventAsync(
                connection, transaction, normalizedProvider,
                message.EventId, cancellationToken);
            if (eventPayment is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new PaymentProcessResult(true, true, eventPayment);
            }

            var existing = await LoadPaymentForUpdateAsync(
                connection, transaction, normalizedProvider,
                message.PaymentId, cancellationToken);
            var (payment, duplicate) = existing is null
                ? await CreatePaymentAsync(connection, transaction, normalizedProvider, message, cancellationToken)
                : await UpdateExistingPaymentAsync(connection, transaction, normalizedProvider, existing, message, cancellationToken);

            await InsertEventAsync(connection, transaction, normalizedProvider, message, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PaymentProcessResult(true, duplicate, payment);
        }
        catch (PostgresException ex)
        {
            throw new InvalidOperationException($"Payment persistence rejected the operation: {ex.MessageText}", ex);
        }
    }
    private static async Task<(PaymentRecord Payment, bool Duplicate)> CreatePaymentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string provider,
        PaymentWebhookMessage message,
        CancellationToken cancellationToken)
    {
        var payment = ToRecord(message);
        const string sql = """
            INSERT INTO payment_gateway_records(
                provider,payment_id,provider_order_id,status,amount_subunits,
                currency_code,captured_at_utc,created_at_utc,updated_at_utc)
            VALUES(
                @provider,@payment_id,@provider_order_id,@status,@amount_subunits,
                @currency_code,@captured_at_utc,@created_at_utc,@updated_at_utc)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddPaymentParameters(command, provider, payment, DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return (payment, false);
    }

    private static async Task<(PaymentRecord Payment, bool Duplicate)> UpdateExistingPaymentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string provider,
        PaymentRecord existing,
        PaymentWebhookMessage message,
        CancellationToken cancellationToken)
    {
        EnsureSameCommercialIdentity(existing, message);
        if (existing.Status == PaymentStatus.Captured || existing.Status == message.Status)
            return (existing, true);

        var updated = existing with
        {
            Status = message.Status,
            CapturedAtUtc = message.Status == PaymentStatus.Captured
                ? message.CapturedAtUtc?.ToUniversalTime()
                : existing.CapturedAtUtc
        };
        const string sql = """
            UPDATE payment_gateway_records
            SET status=@status,captured_at_utc=@captured_at_utc,updated_at_utc=@updated_at_utc
            WHERE provider=@provider AND payment_id=@payment_id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("status", (int)updated.Status);
        command.Parameters.Add("captured_at_utc", NpgsqlDbType.TimestampTz).Value =
            updated.CapturedAtUtc is null ? DBNull.Value : updated.CapturedAtUtc.Value;
        command.Parameters.AddWithValue("updated_at_utc", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("payment_id", updated.PaymentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return (updated, false);
    }

    private static async Task<PaymentRecord?> LoadPaymentByEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string provider,
        string eventId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.payment_id,r.provider_order_id,r.status,r.amount_subunits,r.currency_code,r.captured_at_utc
            FROM payment_gateway_events e
            JOIN payment_gateway_records r ON r.provider=e.provider AND r.payment_id=e.payment_id
            WHERE e.provider=@provider AND e.event_id=@event_id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("event_id", eventId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPayment(reader) : null;
    }
    private static async Task<PaymentRecord?> LoadPaymentForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string provider,
        string paymentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT payment_id,provider_order_id,status,amount_subunits,currency_code,captured_at_utc
            FROM payment_gateway_records
            WHERE provider=@provider AND payment_id=@payment_id
            FOR UPDATE
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("payment_id", paymentId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPayment(reader) : null;
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string provider,
        PaymentWebhookMessage message,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO payment_gateway_events(
                provider,event_id,payment_id,provider_order_id,status,
                amount_subunits,currency_code,received_at_utc)
            VALUES(
                @provider,@event_id,@payment_id,@provider_order_id,@status,
                @amount_subunits,@currency_code,@received_at_utc)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("event_id", message.EventId.Trim());
        command.Parameters.AddWithValue("payment_id", message.PaymentId.Trim());
        command.Parameters.AddWithValue("provider_order_id", message.OrderId.Trim());
        command.Parameters.AddWithValue("status", (int)message.Status);
        command.Parameters.AddWithValue("amount_subunits", message.AmountPaise);
        command.Parameters.AddWithValue("currency_code", message.Currency.Trim());
        command.Parameters.AddWithValue("received_at_utc", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static void AddPaymentParameters(
        NpgsqlCommand command,
        string provider,
        PaymentRecord payment,
        DateTimeOffset now)
    {
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("payment_id", payment.PaymentId);
        command.Parameters.AddWithValue("provider_order_id", payment.OrderId);
        command.Parameters.AddWithValue("status", (int)payment.Status);
        command.Parameters.AddWithValue("amount_subunits", payment.AmountPaise);
        command.Parameters.AddWithValue("currency_code", payment.Currency);
        command.Parameters.Add("captured_at_utc", NpgsqlDbType.TimestampTz).Value =
            payment.CapturedAtUtc is null ? DBNull.Value : payment.CapturedAtUtc.Value;
        command.Parameters.AddWithValue("created_at_utc", now);
        command.Parameters.AddWithValue("updated_at_utc", now);
    }

    private static PaymentRecord ReadPayment(NpgsqlDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            (PaymentStatus)reader.GetInt32(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));

    private static PaymentRecord ToRecord(PaymentWebhookMessage message) =>
        new(
            message.PaymentId.Trim(),
            message.OrderId.Trim(),
            message.Status,
            message.AmountPaise,
            message.Currency.Trim(),
            message.CapturedAtUtc?.ToUniversalTime());

    private static void EnsureSameCommercialIdentity(
        PaymentRecord existing,
        PaymentWebhookMessage incoming)
    {
        if (existing.OrderId != incoming.OrderId.Trim() ||
            existing.AmountPaise != incoming.AmountPaise ||
            !string.Equals(existing.Currency, incoming.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Payment identity mismatch.");
    }
    private static void Validate(PaymentWebhookMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(message.EventId))
            throw new ArgumentException("Event id is required.");
        if (string.IsNullOrWhiteSpace(message.PaymentId))
            throw new ArgumentException("Payment id is required.");
        if (string.IsNullOrWhiteSpace(message.OrderId))
            throw new ArgumentException("Order id is required.");
        if (message.AmountPaise <= 0)
            throw new ArgumentOutOfRangeException(nameof(message.AmountPaise));
        if (string.IsNullOrWhiteSpace(message.Currency))
            throw new ArgumentException("Currency is required.");
        if (message.Status == PaymentStatus.Captured && message.CapturedAtUtc is null)
            throw new ArgumentException("Captured payment timestamp is required.");
    }

    private static string NormalizeProvider(string provider) =>
        string.IsNullOrWhiteSpace(provider)
            ? throw new ArgumentException("Payment provider is required.", nameof(provider))
            : provider.Trim().ToLowerInvariant();

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
