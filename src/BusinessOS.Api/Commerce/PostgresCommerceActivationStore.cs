using System.Text.Json;
using BusinessOS.Application;
using BusinessOS.Catalog;
using BusinessOS.Commerce;
using BusinessOS.Licensing;
using BusinessOS.Payments;
using Npgsql;

namespace BusinessOS.Api.Commerce;

public sealed partial class PostgresCommerceActivationStore : ICommerceActivationStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly NpgsqlDataSource _dataSource;
    private readonly PaymentSubscriptionActivationService _activationService;
    private readonly LeaseSigner _signer;

    public PostgresCommerceActivationStore(
        string connectionString,
        PaymentSubscriptionActivationService activationService,
        LeaseSigner signer)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Commerce connection string is required.", nameof(connectionString));
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _activationService = activationService;
        _signer = signer;
    }

    public async Task<ActivationResponse?> FindActivationAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        var persisted = await LoadSubscriptionAsync(
            connection,
            transaction,
            tenantId,
            subscriptionId,
            forUpdate: false,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return persisted is null ? null : ToResponse(persisted);
    }

    public async Task<SubscriptionStateSnapshot?> FindSubscriptionStateAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        var persisted = await LoadSubscriptionAsync(
            connection, transaction, tenantId, subscriptionId,
            forUpdate: false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return persisted is null ? null : ToStateSnapshot(persisted);
    }

    public async Task<SubscriptionStateSnapshot?> CancelSubscriptionAtPeriodEndAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        var persisted = await LoadSubscriptionAsync(
            connection, transaction, tenantId, subscriptionId,
            forUpdate: true, cancellationToken);
        if (persisted is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        persisted.Subscription.Cancel();
        await UpdateSubscriptionStatusAsync(
            connection, transaction, persisted.Subscription, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToStateSnapshot(persisted);
    }

    public async Task<ActivationResponse> ActivateInitialPurchaseAsync(
        Guid tenantId,
        InitialActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        var planId = Required(request.PlanId, "Plan id is required for PostgreSQL activation.");
        var planVersionId = Required(request.PlanVersionId, "Plan version id is required for PostgreSQL activation.");
        CommerceActivationFactory.ValidateTenantAndOrganisation(tenantId, request.OrganisationId);
        var createdAtUtc = DateTimeOffset.UtcNow;
        var snapshot = CommerceActivationFactory.Snapshot(
            planId,
            planVersionId,
            request.PlanVersionNumber,
            request.Amount,
            request.CurrencyCode,
            request.TermMonths,
            request.DesktopDeviceLimit,
            request.LocationLimit,
            request.WebAdminSeats,
            request.FieldStaffSeats,
            request.MultiLocationCloud);
        var quoteId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var order = CommerceActivationFactory.AcceptedOrder(
            tenantId,
            request.OrganisationId,
            snapshot,
            quoteId,
            orderId,
            createdAtUtc);
        var payment = CommerceActivationFactory.CapturedPayment(
            order,
            request.PaymentId,
            request.CapturedAtUtc);
        var activation = _activationService.ActivateInitialPurchase(
            payment,
            order,
            subscriptionId,
            request.ProductCode,
            _signer);

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
            await InsertQuoteAsync(connection, transaction, order, createdAtUtc, cancellationToken);
            await InsertOrderAsync(connection, transaction, order, cancellationToken);
            await InsertSubscriptionAsync(
                connection,
                transaction,
                activation.Subscription,
                activation.License.LicenseId,
                request.ProductCode,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex)
        {
            throw ToInvalidOperation(ex);
        }

        return new ActivationResponse(
            tenantId,
            activation.Subscription.OrganisationId,
            activation.Subscription.OrderId,
            activation.Subscription.Id,
            activation.License.LicenseId,
            request.ProductCode.Trim(),
            activation.Subscription.StartsOn,
            activation.Subscription.ValidUntil!.Value,
            activation.Subscription.Entitlements);
    }

    public async Task<RenewalResponse?> ActivateRenewalAsync(
        Guid tenantId,
        Guid subscriptionId,
        RenewalActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        var planVersionId = Required(request.PlanVersionId, "Plan version id is required for PostgreSQL renewal.");
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
            var persisted = await LoadSubscriptionAsync(
                connection,
                transaction,
                tenantId,
                subscriptionId,
                forUpdate: true,
                cancellationToken);
            if (persisted is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var snapshot = CommerceActivationFactory.Snapshot(
                persisted.Subscription.PlanId,
                planVersionId,
                request.PlanVersionNumber,
                request.Amount,
                request.CurrencyCode,
                request.TermMonths,
                request.DesktopDeviceLimit,
                request.LocationLimit,
                request.WebAdminSeats,
                request.FieldStaffSeats,
                request.MultiLocationCloud);
            var createdAtUtc = DateTimeOffset.UtcNow;
            var order = CommerceActivationFactory.AcceptedOrder(
                tenantId,
                persisted.Subscription.OrganisationId,
                snapshot,
                Guid.NewGuid(),
                Guid.NewGuid(),
                createdAtUtc);
            var payment = CommerceActivationFactory.CapturedPayment(
                order,
                request.PaymentId,
                request.CapturedAtUtc);
            var license = LicenseEngine.FromPersistedSubscription(
                persisted.LicenseId,
                persisted.ProductCode,
                persisted.Subscription.StartsOn,
                persisted.Subscription.ValidUntil!.Value,
                persisted.Subscription.Entitlements,
                _signer);
            var result = _activationService.ActivateRenewal(
                payment,
                order,
                Guid.NewGuid(),
                persisted.Subscription,
                license);

            await InsertQuoteAsync(connection, transaction, order, createdAtUtc, cancellationToken);
            await InsertOrderAsync(connection, transaction, order, cancellationToken);
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

    private static async Task SetTenantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant_id, true)",
            connection,
            transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertQuoteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Order order,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO commerce_quotes(
                id,tenant_id,organisation_id,plan_id,plan_version_id,
                plan_version_number,amount,currency_code,billing_cycle,term_months,
                entitlement_snapshot,created_at_utc,valid_until_utc,status)
            VALUES(
                @id,@tenant_id,@organisation_id,@plan_id,@plan_version_id,
                @plan_version_number,@amount,@currency_code,@billing_cycle,@term_months,
                @entitlement_snapshot::jsonb,@created_at_utc,@valid_until_utc,@status)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddQuoteParameters(command, order, createdAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Order order,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO commerce_orders(
                id,tenant_id,organisation_id,quote_id,plan_id,plan_version_id,
                amount,currency_code,status,payment_id,paid_at_utc)
            VALUES(
                @id,@tenant_id,@organisation_id,@quote_id,@plan_id,@plan_version_id,
                @amount,@currency_code,@status,@payment_id,@paid_at_utc)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", order.Id);
        command.Parameters.AddWithValue("tenant_id", order.TenantId);
        command.Parameters.AddWithValue("organisation_id", order.OrganisationId);
        command.Parameters.AddWithValue("quote_id", order.QuoteId);
        command.Parameters.AddWithValue("plan_id", order.Snapshot.PlanId);
        command.Parameters.AddWithValue("plan_version_id", order.Snapshot.PlanVersionId);
        command.Parameters.AddWithValue("amount", order.Snapshot.Billing.Amount);
        command.Parameters.AddWithValue("currency_code", order.Snapshot.Billing.CurrencyCode);
        command.Parameters.AddWithValue("status", (int)order.Status);
        command.Parameters.AddWithValue("payment_id", order.PaymentId!);
        command.Parameters.AddWithValue("paid_at_utc", order.PaidAtUtc!.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSubscriptionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SubscriptionEntitlement subscription,
        Guid licenseId,
        string productCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO commerce_subscriptions(
                id,tenant_id,organisation_id,order_id,plan_id,plan_version_id,
                starts_on,valid_until,entitlement_snapshot,status,license_id,product_code)
            VALUES(
                @id,@tenant_id,@organisation_id,@order_id,@plan_id,@plan_version_id,
                @starts_on,@valid_until,@entitlement_snapshot::jsonb,@status,@license_id,@product_code)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", subscription.Id);
        command.Parameters.AddWithValue("tenant_id", subscription.TenantId);
        command.Parameters.AddWithValue("organisation_id", subscription.OrganisationId);
        command.Parameters.AddWithValue("order_id", subscription.OrderId);
        command.Parameters.AddWithValue("plan_id", subscription.PlanId);
        command.Parameters.AddWithValue("plan_version_id", subscription.PlanVersionId);
        command.Parameters.AddWithValue("starts_on", subscription.StartsOn);
        command.Parameters.AddWithValue("valid_until", subscription.ValidUntil!.Value);
        command.Parameters.AddWithValue("entitlement_snapshot", Serialize(subscription.Entitlements));
        command.Parameters.AddWithValue("status", (int)subscription.Status);
        command.Parameters.AddWithValue("license_id", licenseId);
        command.Parameters.AddWithValue("product_code", productCode.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRenewalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SubscriptionRenewal renewal,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO commerce_subscription_renewals(
                id,tenant_id,subscription_id,order_id,paid_at_utc,
                previous_valid_until,new_valid_until,term_months,plan_version_id)
            VALUES(
                @id,@tenant_id,@subscription_id,@order_id,@paid_at_utc,
                @previous_valid_until,@new_valid_until,@term_months,@plan_version_id)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", renewal.Id);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", renewal.SubscriptionId);
        command.Parameters.AddWithValue("order_id", renewal.OrderId);
        command.Parameters.AddWithValue("paid_at_utc", renewal.PaidAtUtc);
        command.Parameters.AddWithValue("previous_valid_until", renewal.PreviousValidUntil!);
        command.Parameters.AddWithValue("new_valid_until", renewal.NewValidUntil);
        command.Parameters.AddWithValue("term_months", renewal.TermMonths);
        command.Parameters.AddWithValue("plan_version_id", renewal.PlanVersionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateSubscriptionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SubscriptionEntitlement subscription,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE commerce_subscriptions
            SET plan_version_id=@plan_version_id,
                valid_until=@valid_until,
                entitlement_snapshot=@entitlement_snapshot::jsonb
            WHERE tenant_id=@tenant_id AND id=@id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("plan_version_id", subscription.PlanVersionId);
        command.Parameters.AddWithValue("valid_until", subscription.ValidUntil!.Value);
        command.Parameters.AddWithValue("entitlement_snapshot", Serialize(subscription.Entitlements));
        command.Parameters.AddWithValue("tenant_id", subscription.TenantId);
        command.Parameters.AddWithValue("id", subscription.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateSubscriptionStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SubscriptionEntitlement subscription,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE commerce_subscriptions
            SET status=@status
            WHERE tenant_id=@tenant_id AND id=@id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("status", (int)subscription.Status);
        command.Parameters.AddWithValue("tenant_id", subscription.TenantId);
        command.Parameters.AddWithValue("id", subscription.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PersistedActivation?> LoadSubscriptionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid subscriptionId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = """
            SELECT id,tenant_id,organisation_id,order_id,plan_id,plan_version_id,
                   starts_on,valid_until,entitlement_snapshot,status,license_id,product_code
            FROM commerce_subscriptions
            WHERE tenant_id=@tenant_id AND id=@subscription_id
              AND license_id IS NOT NULL AND product_code IS NOT NULL
            """;
        if (forUpdate)
            sql += " FOR UPDATE";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var entitlementSnapshot = Deserialize(reader.GetString(8));
        var subscription = SubscriptionEntitlement.Rehydrate(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetGuid(5),
            reader.GetFieldValue<DateOnly>(6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
            entitlementSnapshot,
            (SubscriptionStatus)reader.GetInt32(9));

        if (subscription.ValidUntil is null)
            throw new InvalidOperationException("A finite subscription term is required.");

        return new PersistedActivation(
            subscription,
            reader.GetGuid(10),
            reader.GetString(11));
    }

    private static void AddQuoteParameters(
        NpgsqlCommand command,
        Order order,
        DateTimeOffset createdAtUtc)
    {
        command.Parameters.AddWithValue("id", order.QuoteId);
        command.Parameters.AddWithValue("tenant_id", order.TenantId);
        command.Parameters.AddWithValue("organisation_id", order.OrganisationId);
        command.Parameters.AddWithValue("plan_id", order.Snapshot.PlanId);
        command.Parameters.AddWithValue("plan_version_id", order.Snapshot.PlanVersionId);
        command.Parameters.AddWithValue("plan_version_number", order.Snapshot.PlanVersionNumber);
        command.Parameters.AddWithValue("amount", order.Snapshot.Billing.Amount);
        command.Parameters.AddWithValue("currency_code", order.Snapshot.Billing.CurrencyCode);
        command.Parameters.AddWithValue("billing_cycle", (int)order.Snapshot.Billing.Cycle);
        command.Parameters.AddWithValue("term_months", order.Snapshot.Billing.TermMonths!);
        command.Parameters.AddWithValue("entitlement_snapshot", Serialize(order.Snapshot.ToLicensingSnapshot()));
        command.Parameters.AddWithValue("created_at_utc", createdAtUtc.ToUniversalTime());
        command.Parameters.AddWithValue("valid_until_utc", createdAtUtc.ToUniversalTime().AddDays(7));
        command.Parameters.AddWithValue("status", (int)QuoteStatus.Accepted);
    }

    private static string Serialize(EntitlementSnapshot entitlements) =>
        JsonSerializer.Serialize(entitlements, JsonOptions);

    private static EntitlementSnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize<EntitlementSnapshot>(json, JsonOptions)
        ?? throw new InvalidOperationException("Invalid entitlement snapshot.");

    private static Guid Required(Guid? value, string message) =>
        value is null || value == Guid.Empty ? throw new ArgumentException(message) : value.Value;

    private static InvalidOperationException ToInvalidOperation(PostgresException ex) =>
        new($"Commerce persistence rejected the operation: {ex.MessageText}", ex);

    private static ActivationResponse ToResponse(PersistedActivation persisted)
    {
        var subscription = persisted.Subscription;
        return new ActivationResponse(
            subscription.TenantId,
            subscription.OrganisationId,
            subscription.OrderId,
            subscription.Id,
            persisted.LicenseId,
            persisted.ProductCode,
            subscription.StartsOn,
            subscription.ValidUntil!.Value,
            subscription.Entitlements);
    }

    private static SubscriptionStateSnapshot ToStateSnapshot(PersistedActivation persisted)
    {
        var subscription = persisted.Subscription;
        return new SubscriptionStateSnapshot(
            subscription.TenantId,
            subscription.OrganisationId,
            subscription.Id,
            persisted.LicenseId,
            persisted.ProductCode,
            subscription.PlanId,
            subscription.PlanVersionId,
            subscription.StartsOn,
            subscription.ValidUntil!.Value,
            subscription.Entitlements,
            subscription.Status);
    }

    private sealed record PersistedActivation(
        SubscriptionEntitlement Subscription,
        Guid LicenseId,
        string ProductCode);

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
