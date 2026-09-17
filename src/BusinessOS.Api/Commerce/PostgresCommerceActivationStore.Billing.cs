using Npgsql;

namespace BusinessOS.Api.Commerce;

public sealed partial class PostgresCommerceActivationStore
{
    public async Task<CommerceBillingSource?> FindOrderBillingSourceAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(
            connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);

        const string sql = """
            SELECT o.tenant_id,o.organisation_id,o.id,
                   COALESCE(si.id, r.subscription_id) AS subscription_id,
                   COALESCE(si.product_code, sr.product_code, pr.product_code) AS product_code,
                   o.amount,o.currency_code,o.payment_id,o.paid_at_utc,
                   (r.id IS NOT NULL) AS is_renewal
            FROM commerce_orders o
            LEFT JOIN commerce_subscriptions si
              ON si.tenant_id=o.tenant_id AND si.order_id=o.id
            LEFT JOIN commerce_subscription_renewals r
              ON r.tenant_id=o.tenant_id AND r.order_id=o.id
            LEFT JOIN commerce_subscriptions sr
              ON sr.tenant_id=r.tenant_id AND sr.id=r.subscription_id
            LEFT JOIN commerce_provider_order_routes pr
              ON pr.tenant_id=o.tenant_id AND pr.commerce_order_id=o.id
            WHERE o.tenant_id=@tenant_id AND o.id=@order_id
              AND o.payment_id IS NOT NULL AND o.paid_at_utc IS NOT NULL
            LIMIT 1
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("order_id", orderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var source = new CommerceBillingSource(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? "BUSINESSOS" : reader.GetString(4),
            reader.GetDecimal(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetBoolean(9));
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return source;
    }
}
