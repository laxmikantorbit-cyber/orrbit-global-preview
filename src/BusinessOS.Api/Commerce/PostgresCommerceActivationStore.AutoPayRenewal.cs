using Npgsql;

namespace BusinessOS.Api.Commerce;

public sealed partial class PostgresCommerceActivationStore
{
    public async Task<AutoPayRenewalTemplate?> FindAutoPayRenewalTemplateAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);

        const string sql = """
            SELECT s.product_code,q.plan_version_id,q.plan_version_number,
                   o.amount,o.currency_code,q.term_months,q.entitlement_snapshot
            FROM commerce_subscriptions s
            JOIN commerce_orders o ON o.tenant_id=s.tenant_id AND (
                o.id=s.order_id OR EXISTS (
                    SELECT 1 FROM commerce_subscription_renewals r
                    WHERE r.tenant_id=s.tenant_id
                      AND r.subscription_id=s.id AND r.order_id=o.id))
            JOIN commerce_quotes q ON q.tenant_id=o.tenant_id
                AND q.id=o.quote_id AND q.organisation_id=o.organisation_id
            WHERE s.tenant_id=@tenant_id AND s.id=@subscription_id
              AND s.product_code IS NOT NULL
            ORDER BY o.paid_at_utc DESC NULLS LAST,q.created_at_utc DESC
            LIMIT 1
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (reader.IsDBNull(5))
            throw new InvalidOperationException("AutoPay renewal requires a finite billing term.");
        var entitlements = Deserialize(reader.GetString(6));
        var template = new AutoPayRenewalTemplate(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetInt32(2),
            reader.GetDecimal(3),
            reader.GetString(4),
            reader.GetInt32(5),
            entitlements.DesktopSystems,
            entitlements.Locations,
            entitlements.WebAdminSeats,
            entitlements.FieldStaffSeats,
            entitlements.MultiLocationCloud);
        await transaction.CommitAsync(cancellationToken);
        return template;
    }
}
