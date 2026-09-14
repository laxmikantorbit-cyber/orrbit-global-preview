using BusinessOS.Catalog;
using BusinessOS.Commerce;
using BusinessOS.Licensing;
using BusinessOS.Payments;
using Npgsql;

namespace BusinessOS.Api.Commerce;

public sealed partial class PostgresCommerceActivationStore
{
    public async Task<ActivationResponse?> ActivateCapturedInitialOrderAsync(
        Guid tenantId,
        PaymentRecord payment,
        string productCode,
        CancellationToken cancellationToken = default)
    {
        var orderId = ValidateCapturedPaymentRecord(payment);
        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("Product code is required.", nameof(productCode));

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await SetTenantAsync(connection, transaction, tenantId, cancellationToken);

            var existing = await LoadActivationByOrderAsync(
                connection, transaction, tenantId, orderId, cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return ToResponse(existing);
            }

            var persistedOrder = await LoadOrderForActivationAsync(
                connection, transaction, tenantId, orderId, cancellationToken);
            if (persistedOrder is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            EnsurePendingOrder(persistedOrder.Status);
            var activation = _activationService.ActivateInitialPurchase(
                payment,
                persistedOrder.Order,
                Guid.NewGuid(),
                productCode,
                _signer);

            await UpdateExistingOrderActivatedAsync(
                connection, transaction, tenantId, activation.Subscription.OrderId,
                payment, cancellationToken);
            await InsertSubscriptionAsync(
                connection,
                transaction,
                activation.Subscription,
                activation.License.LicenseId,
                productCode,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToResponse(new PersistedActivation(
                activation.Subscription,
                activation.License.LicenseId,
                productCode.Trim()));
        }
        catch (PostgresException ex)
        {
            throw ToInvalidOperation(ex);
        }
    }

    public async Task<RenewalResponse?> ActivateCapturedRenewalOrderAsync(
        Guid tenantId,
        Guid subscriptionId,
        PaymentRecord payment,
        CancellationToken cancellationToken = default)
    {
        var orderId = ValidateCapturedPaymentRecord(payment);
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await SetTenantAsync(connection, transaction, tenantId, cancellationToken);

            var existingRenewal = await LoadRenewalByOrderAsync(
                connection, transaction, tenantId, subscriptionId, orderId, cancellationToken);
            if (existingRenewal is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existingRenewal;
            }

            var persisted = await LoadSubscriptionAsync(
                connection, transaction, tenantId, subscriptionId,
                forUpdate: true, cancellationToken);
            if (persisted is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var persistedOrder = await LoadOrderForActivationAsync(
                connection, transaction, tenantId, orderId, cancellationToken);
            if (persistedOrder is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            EnsurePendingOrder(persistedOrder.Status);
            var license = LicenseEngine.FromPersistedSubscription(
                persisted.LicenseId,
                persisted.ProductCode,
                persisted.Subscription.StartsOn,
                persisted.Subscription.ValidUntil!.Value,
                persisted.Subscription.Entitlements,
                _signer);
            var result = _activationService.ActivateRenewal(
                payment,
                persistedOrder.Order,
                Guid.NewGuid(),
                persisted.Subscription,
                license);

            await UpdateExistingOrderActivatedAsync(
                connection, transaction, tenantId, result.Renewal.OrderId,
                payment, cancellationToken);
            await InsertRenewalAsync(connection, transaction, result.Renewal, tenantId, cancellationToken);
            await UpdateSubscriptionAsync(connection, transaction, result.Subscription, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new RenewalResponse(
                tenantId,
                result.Subscription.Id,
                result.Renewal.Id,
                result.Renewal.OrderId,
                result.Renewal.PlanVersionId,
                result.Renewal.PreviousValidUntil ?? result.Subscription.StartsOn,
                result.Renewal.NewValidUntil,
                result.Subscription.Entitlements);
        }
        catch (PostgresException ex)
        {
            throw ToInvalidOperation(ex);
        }
    }

    private static async Task<PersistedActivation?> LoadActivationByOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id,tenant_id,organisation_id,order_id,plan_id,plan_version_id,
                   starts_on,valid_until,entitlement_snapshot,status,license_id,product_code
            FROM commerce_subscriptions
            WHERE tenant_id=@tenant_id AND order_id=@order_id
              AND license_id IS NOT NULL AND product_code IS NOT NULL
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("order_id", orderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

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

    private static async Task<RenewalResponse?> LoadRenewalByOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid subscriptionId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.subscription_id,r.id,r.order_id,r.plan_version_id,
                   COALESCE(r.previous_valid_until, s.starts_on),
                   r.new_valid_until,s.entitlement_snapshot
            FROM commerce_subscription_renewals r
            JOIN commerce_subscriptions s ON s.tenant_id=r.tenant_id AND s.id=r.subscription_id
            WHERE r.tenant_id=@tenant_id AND r.subscription_id=@subscription_id AND r.order_id=@order_id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("order_id", orderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new RenewalResponse(
            tenantId,
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetFieldValue<DateOnly>(4),
            reader.GetFieldValue<DateOnly>(5),
            Deserialize(reader.GetString(6)));
    }

    private static async Task<PersistedCommerceOrder?> LoadOrderForActivationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT o.id,o.tenant_id,o.organisation_id,o.quote_id,o.opportunity_id,
                   o.plan_id,o.plan_version_id,q.plan_version_number,o.amount,o.currency_code,
                   q.billing_cycle,q.term_months,q.entitlement_snapshot,o.status
            FROM commerce_orders o
            JOIN commerce_quotes q ON q.tenant_id=o.tenant_id
                AND q.id=o.quote_id AND q.organisation_id=o.organisation_id
            WHERE o.tenant_id=@tenant_id AND o.id=@order_id
            FOR UPDATE
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("order_id", orderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var entitlements = Deserialize(reader.GetString(12));
        var snapshot = new CommercialSnapshot(
            reader.GetGuid(5),
            reader.GetGuid(6),
            reader.GetInt32(7),
            new BillingRule(
                reader.GetDecimal(8),
                reader.GetString(9),
                (BillingCycle)reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11)),
            new EntitlementProfile(
                entitlements.DesktopSystems,
                entitlements.Locations,
                entitlements.WebAdminSeats,
                entitlements.FieldStaffSeats,
                entitlements.MultiLocationCloud,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        var order = new Order(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
            snapshot);
        return new PersistedCommerceOrder(order, (OrderStatus)reader.GetInt32(13));
    }

    private static async Task UpdateExistingOrderActivatedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid orderId,
        PaymentRecord payment,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE commerce_orders
            SET status=@status, payment_id=@payment_id, paid_at_utc=@paid_at_utc
            WHERE tenant_id=@tenant_id AND id=@id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("status", (int)OrderStatus.Activated);
        command.Parameters.AddWithValue("payment_id", payment.PaymentId);
        command.Parameters.AddWithValue("paid_at_utc", payment.CapturedAtUtc!.Value);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", orderId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Guid ValidateCapturedPaymentRecord(PaymentRecord payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (payment.Status != PaymentStatus.Captured || payment.CapturedAtUtc is null)
            throw new InvalidOperationException("Only captured payment can activate commerce order.");
        if (!Guid.TryParse(payment.OrderId, out var orderId) || orderId == Guid.Empty)
            throw new InvalidOperationException("Payment order id must be the internal commerce order id.");
        return orderId;
    }

    private static void EnsurePendingOrder(OrderStatus status)
    {
        if (status != OrderStatus.PendingPayment)
            throw new InvalidOperationException("Commerce order is not pending payment.");
    }

    private sealed record PersistedCommerceOrder(Order Order, OrderStatus Status);
}
