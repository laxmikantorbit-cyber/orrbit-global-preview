using Npgsql;

namespace BusinessOS.Api.Commerce;

public sealed partial class PostgresCommerceActivationStore
{
    public async Task<IReadOnlyList<DesktopDeviceLifecycleEvent>> ListDesktopDeviceEventsAsync(
        Guid tenantId,
        Guid subscriptionId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(take, 1, 200);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        const string sql = """
            SELECT id,tenant_id,subscription_id,device_fingerprint,
                   previous_device_fingerprint,action,outcome,device_name,
                   app_version,occurred_at_utc
            FROM commerce_desktop_device_events
            WHERE tenant_id=@tenant_id AND subscription_id=@subscription_id
            ORDER BY occurred_at_utc DESC
            LIMIT @take
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("take", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<DesktopDeviceLifecycleEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadDeviceEvent(reader));
        }
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return items;
    }

    private static async Task InsertDeviceEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid subscriptionId,
        string deviceFingerprint,
        string? previousDeviceFingerprint,
        string action,
        string outcome,
        string? deviceName,
        string? appVersion,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO commerce_desktop_device_events(
                id,tenant_id,subscription_id,device_fingerprint,
                previous_device_fingerprint,action,outcome,device_name,
                app_version,occurred_at_utc)
            VALUES(
                @id,@tenant_id,@subscription_id,@device_fingerprint,
                @previous_device_fingerprint,@action,@outcome,@device_name,
                @app_version,@occurred_at_utc)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("device_fingerprint", deviceFingerprint);
        command.Parameters.AddWithValue("previous_device_fingerprint", (object?)previousDeviceFingerprint ?? DBNull.Value);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("outcome", outcome);
        command.Parameters.AddWithValue("device_name", (object?)deviceName ?? DBNull.Value);
        command.Parameters.AddWithValue("app_version", (object?)appVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("occurred_at_utc", occurredAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DesktopDeviceLifecycleEvent ReadDeviceEvent(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5), reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetFieldValue<DateTimeOffset>(9));
}
