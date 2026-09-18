using BusinessOS.Api;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.SoftwareDelivery;

public sealed class PostgresSoftwareDeliveryEventStore : ISoftwareDeliveryEventStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string? _runtimeRole;

    public PostgresSoftwareDeliveryEventStore(string connectionString, string? runtimeRole)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _runtimeRole = runtimeRole;
    }

    public async Task<SoftwareDeliveryEventRecord> AddAsync(
        Guid tenantId,
        SoftwareDeliveryEventCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = new SoftwareDeliveryEventRecord(
            Guid.NewGuid(), tenantId, request.SubscriptionId, request.ReleaseId,
            request.ProductCode.Trim().ToUpperInvariant(), request.Channel.Trim(),
            request.Platform.Trim(), request.Architecture.Trim(),
            string.IsNullOrWhiteSpace(request.Action) ? "delivery_viewed" : request.Action.Trim(),
            request.DownloadEntitled,
            string.IsNullOrWhiteSpace(request.UnavailableReason) ? null : request.UnavailableReason.Trim(),
            request.OccurredAtUtc ?? DateTimeOffset.UtcNow);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        const string sql = """
            INSERT INTO software_delivery_events(
                id,tenant_id,subscription_id,release_id,product_code,channel,platform,
                architecture,action,download_entitled,unavailable_reason,occurred_at_utc)
            VALUES(@id,@tenant_id,@subscription_id,@release_id,@product_code,@channel,@platform,
                @architecture,@action,@download_entitled,@unavailable_reason,@occurred_at_utc)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Bind(command, item);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return item;
    }

    public async Task<IReadOnlyList<SoftwareDeliveryEventRecord>> ListAsync(
        Guid tenantId,
        Guid? subscriptionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        const string sql = """
            SELECT id,tenant_id,subscription_id,release_id,product_code,channel,platform,
                   architecture,action,download_entitled,unavailable_reason,occurred_at_utc
            FROM software_delivery_events
            WHERE tenant_id=@tenant_id
              AND (@subscription_id IS NULL OR subscription_id=@subscription_id)
            ORDER BY occurred_at_utc DESC
            LIMIT @take
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableGuid(command, "subscription_id", subscriptionId);
        command.Parameters.AddWithValue("take", take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<SoftwareDeliveryEventRecord>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(Read(reader));
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return items;
    }

    private static SoftwareDeliveryEventRecord Read(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.GetString(8), reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetFieldValue<DateTimeOffset>(11));

    private static void Bind(NpgsqlCommand command, SoftwareDeliveryEventRecord item)
    {
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("tenant_id", item.TenantId);
        command.Parameters.AddWithValue("subscription_id", item.SubscriptionId);
        AddNullableGuid(command, "release_id", item.ReleaseId);
        command.Parameters.AddWithValue("product_code", item.ProductCode);
        command.Parameters.AddWithValue("channel", item.Channel);
        command.Parameters.AddWithValue("platform", item.Platform);
        command.Parameters.AddWithValue("architecture", item.Architecture);
        command.Parameters.AddWithValue("action", item.Action);
        command.Parameters.AddWithValue("download_entitled", item.DownloadEntitled);
        AddNullableText(command, "unavailable_reason", item.UnavailableReason);
        command.Parameters.AddWithValue("occurred_at_utc", item.OccurredAtUtc);
    }

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value is null ? DBNull.Value : value.Value;
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    private static async Task SetTenantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant_id, true)",
            connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
