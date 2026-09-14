using BusinessOS.Commerce;
using Npgsql;

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
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || commerceOrderId == Guid.Empty)
            throw new ArgumentException("Tenant and commerce order ids are required.");
        if (string.IsNullOrWhiteSpace(razorpayOrderId))
            throw new ArgumentException("Razorpay order id is required.", nameof(razorpayOrderId));

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
            await UpdateRazorpayOrderIdAsync(
                connection, transaction, tenantId, commerceOrderId,
                razorpayOrderId.Trim(), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex)
        {
            throw ToInvalidOperation(ex);
        }
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
            expiresAtUtc,
            notes);
    }
}
