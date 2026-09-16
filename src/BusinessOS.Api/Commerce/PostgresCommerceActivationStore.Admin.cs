using BusinessOS.Commerce;
using BusinessOS.Licensing;
using Npgsql;

namespace BusinessOS.Api.Commerce;

public sealed partial class PostgresCommerceActivationStore
{
    public async Task<CommerceAdminSnapshot> GetAdminSnapshotAsync(
        Guid tenantId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(take, 1, 200);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        var orders = await LoadAdminOrdersAsync(
            connection, transaction, tenantId, limit, cancellationToken);
        var activations = await LoadAdminActivationsAsync(
            connection, transaction, tenantId, limit, cancellationToken);
        var renewals = await LoadAdminRenewalsAsync(
            connection, transaction, tenantId, limit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CommerceAdminSnapshot(
            tenantId, DateTimeOffset.UtcNow, orders, activations, renewals);
    }

    private static async Task<IReadOnlyList<CommerceAdminOrderSnapshot>> LoadAdminOrdersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        int take,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT o.tenant_id,o.organisation_id,o.quote_id,o.id,o.plan_id,o.plan_version_id,
                   o.amount,o.currency_code,o.status,o.payment_id,o.paid_at_utc,o.razorpay_order_id,
                   r.provider,r.provider_order_id,r.product_code,r.subscription_id,
                   q.created_at_utc,q.valid_until_utc
            FROM commerce_orders o
            JOIN commerce_quotes q ON q.tenant_id=o.tenant_id
                AND q.id=o.quote_id AND q.organisation_id=o.organisation_id
            LEFT JOIN commerce_provider_order_routes r
                ON r.tenant_id=o.tenant_id AND r.commerce_order_id=o.id
            WHERE o.tenant_id=@tenant_id
            ORDER BY COALESCE(o.paid_at_utc,q.created_at_utc) DESC
            LIMIT @take
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("take", take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<CommerceAdminOrderSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CommerceAdminOrderSnapshot(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
                reader.GetGuid(4), reader.GetGuid(5), reader.GetDecimal(6), reader.GetString(7),
                ((OrderStatus)reader.GetInt32(8)).ToString(),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetGuid(15),
                reader.GetFieldValue<DateTimeOffset>(16),
                reader.GetFieldValue<DateTimeOffset>(17)));
        }
        return rows;
    }

    private static async Task<IReadOnlyList<ActivationResponse>> LoadAdminActivationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        int take,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id,tenant_id,organisation_id,order_id,plan_id,plan_version_id,
                   starts_on,valid_until,entitlement_snapshot,status,license_id,product_code
            FROM commerce_subscriptions
            WHERE tenant_id=@tenant_id
              AND license_id IS NOT NULL AND product_code IS NOT NULL
            ORDER BY starts_on DESC,id DESC
            LIMIT @take
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("take", take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ActivationResponse>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(ToResponse(ReadPersistedActivation(reader)));
        return rows;
    }

    private static async Task<IReadOnlyList<RenewalResponse>> LoadAdminRenewalsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        int take,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.subscription_id,r.id,r.order_id,r.plan_version_id,
                   COALESCE(r.previous_valid_until, s.starts_on),
                   r.new_valid_until,s.entitlement_snapshot
            FROM commerce_subscription_renewals r
            JOIN commerce_subscriptions s ON s.tenant_id=r.tenant_id AND s.id=r.subscription_id
            WHERE r.tenant_id=@tenant_id
            ORDER BY r.paid_at_utc DESC,r.id DESC
            LIMIT @take
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("take", take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<RenewalResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RenewalResponse(
                tenantId,
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetFieldValue<DateOnly>(5),
                Deserialize(reader.GetString(6))));
        }
        return rows;
    }

    private static PersistedActivation ReadPersistedActivation(NpgsqlDataReader reader)
    {
        var subscription = SubscriptionEntitlement.Rehydrate(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetGuid(3), reader.GetGuid(4), reader.GetGuid(5),
            reader.GetFieldValue<DateOnly>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
            Deserialize(reader.GetString(8)),
            (SubscriptionStatus)reader.GetInt32(9));
        return new PersistedActivation(
            subscription,
            reader.GetGuid(10),
            reader.GetString(11));
    }
}
