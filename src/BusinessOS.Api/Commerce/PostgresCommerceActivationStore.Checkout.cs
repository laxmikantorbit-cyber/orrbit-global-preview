using BusinessOS.Commerce;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Commerce;

public sealed partial class PostgresCommerceActivationStore
{
    public async Task<CheckoutOrderResponse> CreateInitialCheckoutOrderAsync(
        Guid tenantId,
        CreateInitialCheckoutOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var planId = Required(request.PlanId, "Plan id is required for checkout order creation.");
        var planVersionId = Required(request.PlanVersionId, "Plan version id is required for checkout order creation.");
        if (string.IsNullOrWhiteSpace(request.ProductCode))
            throw new ArgumentException("Product code is required.", nameof(request.ProductCode));
        CommerceActivationFactory.ValidateTenantAndOrganisation(tenantId, request.OrganisationId);

        var createdAtUtc = DateTimeOffset.UtcNow;
        var order = CreatePendingOrder(
            tenantId,
            request.OrganisationId,
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
            request.MultiLocationCloud,
            createdAtUtc);

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
            await InsertQuoteAsync(connection, transaction, order, createdAtUtc, cancellationToken);
            await InsertPendingOrderAsync(connection, transaction, order, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex)
        {
            throw ToInvalidOperation(ex);
        }

        return ToCheckoutResponse(
            tenantId,
            order,
            createdAtUtc.AddDays(7),
            request.ProductCode,
            subscriptionId: null);
    }

    public async Task<CheckoutOrderResponse?> CreateRenewalCheckoutOrderAsync(
        Guid tenantId,
        Guid subscriptionId,
        CreateRenewalCheckoutOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var planVersionId = Required(request.PlanVersionId, "Plan version id is required for renewal checkout order creation.");
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
                forUpdate: false,
                cancellationToken);
            if (persisted is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var createdAtUtc = DateTimeOffset.UtcNow;
            var order = CreatePendingOrder(
                tenantId,
                persisted.Subscription.OrganisationId,
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
                request.MultiLocationCloud,
                createdAtUtc);

            await InsertQuoteAsync(connection, transaction, order, createdAtUtc, cancellationToken);
            await InsertPendingOrderAsync(connection, transaction, order, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToCheckoutResponse(
                tenantId,
                order,
                createdAtUtc.AddDays(7),
                persisted.ProductCode,
                subscriptionId);
        }
        catch (PostgresException ex)
        {
            throw ToInvalidOperation(ex);
        }
    }

    public async Task RecordRazorpayOrderAsync(
        Guid tenantId,
        Guid commerceOrderId,
        string razorpayOrderId,
        string productCode,
        Guid? subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || commerceOrderId == Guid.Empty)
            throw new ArgumentException("Tenant and commerce order ids are required.");
        if (string.IsNullOrWhiteSpace(razorpayOrderId))
            throw new ArgumentException("Razorpay order id is required.", nameof(razorpayOrderId));
        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("Product code is required.", nameof(productCode));

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
            var trimmedRazorpayOrderId = razorpayOrderId.Trim();
            var trimmedProductCode = productCode.Trim();
            await UpdateRazorpayOrderIdAsync(
                connection, transaction, tenantId, commerceOrderId,
                trimmedRazorpayOrderId, cancellationToken);
            await InsertProviderOrderRouteAsync(
                connection, transaction, "razorpay", trimmedRazorpayOrderId,
                tenantId, commerceOrderId, trimmedProductCode,
                subscriptionId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex)
        {
            throw ToInvalidOperation(ex);
        }
    }

    public async Task<ProviderOrderRoute?> FindProviderOrderRouteAsync(
        string provider,
        string providerOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerOrderId))
            return null;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT provider,provider_order_id,tenant_id,commerce_order_id,product_code,subscription_id
            FROM commerce_provider_order_routes
            WHERE provider=@provider AND provider_order_id=@provider_order_id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider", NormalizeProvider(provider));
        command.Parameters.AddWithValue("provider_order_id", providerOrderId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new ProviderOrderRoute(
            reader.GetString(0), reader.GetString(1), reader.GetGuid(2),
            reader.GetGuid(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5));
    }

    private static Order CreatePendingOrder(
        Guid tenantId,
        Guid organisationId,
        Guid planId,
        Guid planVersionId,
        int planVersionNumber,
        decimal amount,
        string currencyCode,
        int termMonths,
        int desktopDeviceLimit,
        int locationLimit,
        int webAdminSeats,
        int fieldStaffSeats,
        bool multiLocationCloud,
        DateTimeOffset createdAtUtc)
    {
        var snapshot = CommerceActivationFactory.Snapshot(
            planId,
            planVersionId,
            planVersionNumber,
            amount,
            currencyCode,
            termMonths,
            desktopDeviceLimit,
            locationLimit,
            webAdminSeats,
            fieldStaffSeats,
            multiLocationCloud);
        return CommerceActivationFactory.AcceptedOrder(
            tenantId,
            organisationId,
            snapshot,
            Guid.NewGuid(),
            Guid.NewGuid(),
            createdAtUtc);
    }

    private static async Task InsertPendingOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Order order,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO commerce_orders(
                id,tenant_id,organisation_id,quote_id,plan_id,plan_version_id,
                amount,currency_code,status)
            VALUES(
                @id,@tenant_id,@organisation_id,@quote_id,@plan_id,@plan_version_id,
                @amount,@currency_code,@status)
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
        command.Parameters.AddWithValue("status", (int)OrderStatus.PendingPayment);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertProviderOrderRouteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string provider,
        string providerOrderId,
        Guid tenantId,
        Guid commerceOrderId,
        string productCode,
        Guid? subscriptionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO commerce_provider_order_routes(
                provider,provider_order_id,tenant_id,commerce_order_id,
                product_code,subscription_id,created_at_utc)
            VALUES(
                @provider,@provider_order_id,@tenant_id,@commerce_order_id,
                @product_code,@subscription_id,@created_at_utc)
            ON CONFLICT(provider, provider_order_id) DO UPDATE
            SET provider_order_id=EXCLUDED.provider_order_id
            WHERE commerce_provider_order_routes.tenant_id=EXCLUDED.tenant_id
              AND commerce_provider_order_routes.commerce_order_id=EXCLUDED.commerce_order_id
              AND commerce_provider_order_routes.product_code=EXCLUDED.product_code
              AND commerce_provider_order_routes.subscription_id IS NOT DISTINCT FROM EXCLUDED.subscription_id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("provider", NormalizeProvider(provider));
        command.Parameters.AddWithValue("provider_order_id", providerOrderId.Trim());
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("commerce_order_id", commerceOrderId);
        command.Parameters.AddWithValue("product_code", productCode.Trim());
        command.Parameters.Add("subscription_id", NpgsqlDbType.Uuid).Value = subscriptionId is null
            ? DBNull.Value
            : subscriptionId.Value;
        command.Parameters.AddWithValue("created_at_utc", DateTimeOffset.UtcNow);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows != 1)
            throw new InvalidOperationException("Provider order route conflicts with another commerce order.");
    }

    private static async Task UpdateRazorpayOrderIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid commerceOrderId,
        string razorpayOrderId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE commerce_orders
            SET razorpay_order_id=@razorpay_order_id
            WHERE tenant_id=@tenant_id AND id=@id
              AND (razorpay_order_id IS NULL OR razorpay_order_id=@razorpay_order_id)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("razorpay_order_id", razorpayOrderId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", commerceOrderId);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows != 1)
            throw new InvalidOperationException("Commerce order could not be linked to Razorpay order id.");
    }

    private static string NormalizeProvider(string provider) =>
        provider.Trim().ToLowerInvariant();

    private static CheckoutOrderResponse ToCheckoutResponse(
        Guid tenantId,
        Order order,
        DateTimeOffset expiresAtUtc,
        string productCode,
        Guid? subscriptionId)
    {
        var notes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenantId"] = tenantId.ToString(),
            ["commerceOrderId"] = order.Id.ToString(),
            ["internalOrderId"] = order.Id.ToString(),
            ["productCode"] = productCode.Trim()
        };
        if (subscriptionId is Guid id)
            notes["subscriptionId"] = id.ToString();

        return new CheckoutOrderResponse(
            tenantId,
            order.OrganisationId,
            order.QuoteId,
            order.Id,
            order.Snapshot.PlanId,
            order.Snapshot.PlanVersionId,
            order.Snapshot.Billing.Amount,
            order.Snapshot.Billing.CurrencyCode,
            productCode.Trim(),
            subscriptionId,
            expiresAtUtc,
            notes);
    }
}
