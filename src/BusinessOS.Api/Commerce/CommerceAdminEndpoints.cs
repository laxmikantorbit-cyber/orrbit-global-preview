using BusinessOS.Api.Billing;
using BusinessOS.Api.Payments;
using BusinessOS.Api.Tenancy;
using BusinessOS.Payments;

namespace BusinessOS.Api.Commerce;

public static class CommerceAdminEndpoints
{
    private const string RazorpayProvider = "razorpay";

    public static IEndpointRouteBuilder MapCommerceAdminEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/commerce/admin");

        group.MapGet("/status", async (
            int? take,
            TenantContext tenant,
            ICommerceActivationStore commerceStore,
            IPaymentEventStore paymentStore,
            CancellationToken cancellationToken) =>
        {
            if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden)
                return forbidden;

            var limit = Math.Clamp(take ?? 50, 1, 200);
            var snapshot = await commerceStore.GetAdminSnapshotAsync(
                tenant.TenantId, limit, cancellationToken);
            var payments = await LoadPaymentsAsync(paymentStore, snapshot, limit, cancellationToken);
            var latestByOrder = payments
                .GroupBy(x => x.ProviderOrderId, StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => x.OrderByDescending(p => p.UpdatedAtUtc).First(),
                    StringComparer.Ordinal);

            var orders = snapshot.Orders
                .Select(order => ToStatusItem(order, latestByOrder, snapshot))
                .ToList();
            var counts = new CommerceAdminCounts(
                orders.Count(x => x.Order.OrderStatus == "PendingPayment"),
                payments.Count(x => x.Status == "Captured"),
                payments.Count(x => x.Status == "Failed"),
                snapshot.Activations.Count,
                snapshot.Renewals.Count,
                orders.Count(x => x.ReconciliationStatus is
                    "captured_pending_activation" or "payment_pending" or
                    "checkout_created_without_provider_order"));

            return Results.Ok(new CommerceAdminStatusResponse(
                snapshot.TenantId, snapshot.GeneratedAtUtc, counts,
                orders, payments, snapshot.Activations, snapshot.Renewals));
        });

        group.MapPost("/razorpay/orders/{razorpayOrderId}/reconcile", async (
            string razorpayOrderId,
            TenantContext tenant,
            ICommerceActivationStore commerceStore,
            IPaymentEventStore paymentStore,
            IRazorpayPaymentClient paymentClient,
            IProviderOrderConcurrencyGate providerOrderGate,
            BillingAutomationService billingAutomation,
            CancellationToken cancellationToken) =>
        {
            if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden)
                return forbidden;

            if (string.IsNullOrWhiteSpace(razorpayOrderId))
                return Results.BadRequest(new ErrorResponse("razorpay_order_id is required."));

            var normalizedOrderId = razorpayOrderId.Trim();
            await using var providerOrderLease = await providerOrderGate.AcquireAsync(
                RazorpayProvider, normalizedOrderId, cancellationToken);
            var status = await commerceStore.FindProviderOrderStatusAsync(
                RazorpayProvider, normalizedOrderId, cancellationToken);
            if (status is null)
                return Results.NotFound(new ErrorResponse(
                    "Razorpay provider order route was not found."));
            if (status.Route.TenantId != tenant.TenantId)
                return Results.NotFound(new ErrorResponse(
                    "Razorpay order was not found for this tenant."));

            return await ExecuteManualReconcileAsync(
                normalizedOrderId, status, commerceStore,
                paymentStore, paymentClient, billingAutomation, cancellationToken);
        });

        group.MapGet("/subscriptions/{subscriptionId:guid}/devices", async (
            Guid subscriptionId, TenantContext tenant, ICommerceActivationStore commerceStore,
            CancellationToken cancellationToken) =>
        {
            if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden) return forbidden;
            var subscription = await commerceStore.FindSubscriptionStateAsync(tenant.TenantId, subscriptionId, cancellationToken);
            if (subscription is null) return Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."));
            var devices = await commerceStore.ListDesktopDevicesAsync(tenant.TenantId, subscriptionId, cancellationToken);
            return Results.Ok(new DesktopDeviceInventoryResponse(
                tenant.TenantId, subscriptionId, devices.Count(x => x.Active),
                subscription.Entitlements.DesktopSystems, devices));
        });

        group.MapPost("/subscriptions/{subscriptionId:guid}/devices/revoke", async (
            Guid subscriptionId, DesktopDeviceRevokeRequest request, TenantContext tenant,
            ICommerceActivationStore commerceStore, CancellationToken cancellationToken) =>
        {
            if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden) return forbidden;
            try
            {
                var revoked = await commerceStore.RevokeDesktopDeviceAsync(
                    tenant.TenantId, subscriptionId, request.DeviceFingerprint, cancellationToken);
                return revoked ? Results.Ok(new { subscriptionId, revoked = true })
                    : Results.NotFound(new ErrorResponse("Active device was not found for this subscription."));
            }
            catch (ArgumentException ex) { return Results.BadRequest(new ErrorResponse(ex.Message)); }
        });

        group.MapPost("/subscriptions/{subscriptionId:guid}/devices/replace", async (
            Guid subscriptionId, DesktopDeviceReplaceRequest request, TenantContext tenant,
            ICommerceActivationStore commerceStore, CancellationToken cancellationToken) =>
        {
            if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden) return forbidden;
            try
            {
                var result = await commerceStore.ReplaceDesktopDeviceAsync(
                    tenant.TenantId, subscriptionId, request, cancellationToken);
                return result is null
                    ? Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."))
                    : Results.Ok(result);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new ErrorResponse(ex.Message)); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new ErrorResponse(ex.Message)); }
        });
        return app;
    }

    private static async Task<IResult> ExecuteManualReconcileAsync(
        string razorpayOrderId,
        ProviderOrderStatus currentStatus,
        ICommerceActivationStore commerceStore,
        IPaymentEventStore paymentStore,
        IRazorpayPaymentClient paymentClient,
        BillingAutomationService billingAutomation,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RazorpayPaymentResult> providerPayments;
        try
        {
            providerPayments = await paymentClient.FetchOrderPaymentsAsync(
                razorpayOrderId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }

        if (providerPayments.Count == 0)
            return Results.Ok(ToManualResponse(
                "no_provider_payments_found", false, currentStatus, null,
                0, currentStatus.InitialActivation, currentStatus.RenewalActivation));

        var selected = SelectBestPayment(providerPayments);
        if (!string.Equals(selected.OrderId, razorpayOrderId, StringComparison.Ordinal))
            return Results.BadRequest(new ErrorResponse(
                "Fetched Razorpay payment order id does not match requested order."));

        var processed = await paymentStore.ProcessAsync(
            RazorpayProvider,
            RazorpayHttpPaymentClient.ToWebhookMessage(selected),
            cancellationToken);
        if (processed.Payment.Status != PaymentStatus.Captured)
            return Results.Ok(ToManualResponse(
                PaymentOutcome(processed.Payment.Status),
                processed.Duplicate,
                currentStatus,
                processed.Payment,
                providerPayments.Count,
                currentStatus.InitialActivation,
                currentStatus.RenewalActivation));

        return await ActivateFromManualPaymentAsync(
            currentStatus.Route,
            processed,
            providerPayments.Count,
            commerceStore,
            billingAutomation,
            cancellationToken);
    }

    private static async Task<IResult> ActivateFromManualPaymentAsync(
        ProviderOrderRoute route,
        PaymentProcessResult processed,
        int providerPaymentCount,
        ICommerceActivationStore commerceStore,
        BillingAutomationService billingAutomation,
        CancellationToken cancellationToken)
    {
        if (route.SubscriptionId is Guid subscriptionId)
        {
            var renewal = await commerceStore.ActivateCapturedRenewalOrderAsync(
                route.TenantId, subscriptionId, processed.Payment, cancellationToken);
            if (renewal is null)
                return Results.NotFound(new ErrorResponse(
                    "Renewal order or subscription was not found for this tenant."));
            await billingAutomation.EnsureForOrderAsync(
                route.TenantId, route.CommerceOrderId, cancellationToken);
            return Results.Ok(ToManualResponse(
                "provider_payment_captured", processed.Duplicate,
                new ProviderOrderStatus(route, "activated", null, renewal),
                processed.Payment, providerPaymentCount, null, renewal));
        }

        var activation = await commerceStore.ActivateCapturedInitialOrderAsync(
            route.TenantId, processed.Payment, route.ProductCode, cancellationToken);
        if (activation is null)
            return Results.NotFound(new ErrorResponse("Initial commerce order was not found for this tenant."));
        await billingAutomation.EnsureForOrderAsync(
            route.TenantId, route.CommerceOrderId, cancellationToken);
        return Results.Ok(ToManualResponse(
            "provider_payment_captured", processed.Duplicate,
            new ProviderOrderStatus(route, "activated", activation, null),
            processed.Payment, providerPaymentCount, activation, null));
    }

    private static async Task<IReadOnlyList<CommerceAdminPaymentItem>> LoadPaymentsAsync(
        IPaymentEventStore paymentStore,
        CommerceAdminSnapshot snapshot,
        int limit,
        CancellationToken cancellationToken)
    {
        var providerOrderIds = snapshot.Orders
            .Select(x => x.ProviderOrderId ?? x.RazorpayOrderId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return await paymentStore.ListPaymentsForProviderOrdersAsync(
            RazorpayProvider, providerOrderIds, limit, cancellationToken);
    }

    private static RazorpayPaymentResult SelectBestPayment(
        IReadOnlyList<RazorpayPaymentResult> payments) =>
        payments
            .OrderByDescending(x => x.Captured || x.Status.Equals("captured", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => x.CreatedAtUtc)
            .First();

    private static CommerceAdminOrderStatusItem ToStatusItem(
        CommerceAdminOrderSnapshot order,
        IReadOnlyDictionary<string, CommerceAdminPaymentItem> paymentsByOrder,
        CommerceAdminSnapshot snapshot)
    {
        var providerOrderId = order.ProviderOrderId ?? order.RazorpayOrderId;
        paymentsByOrder.TryGetValue(providerOrderId ?? string.Empty, out var payment);
        var activated = snapshot.Activations.Any(x => x.OrderId == order.CommerceOrderId);
        var renewed = snapshot.Renewals.Any(x => x.OrderId == order.CommerceOrderId);
        var reconciliation = ReconciliationStatus(order, payment, activated, renewed);
        return new CommerceAdminOrderStatusItem(
            order, payment?.Status, reconciliation);
    }

    private static string ReconciliationStatus(
        CommerceAdminOrderSnapshot order,
        CommerceAdminPaymentItem? payment,
        bool activated,
        bool renewed)
    {
        if (activated)
            return "activation_completed";
        if (renewed)
            return "renewal_completed";
        if (payment?.Status == "Captured")
            return "captured_pending_activation";
        if (payment?.Status == "Failed")
            return "payment_failed";
        if (payment?.Status == "Pending")
            return "payment_pending";
        if (string.IsNullOrWhiteSpace(order.ProviderOrderId) &&
            string.IsNullOrWhiteSpace(order.RazorpayOrderId))
            return "checkout_created_without_provider_order";
        return "awaiting_payment";
    }

    private static string PaymentOutcome(PaymentStatus status) =>
        status switch
        {
            PaymentStatus.Captured => "provider_payment_captured",
            PaymentStatus.Failed => "provider_payment_failed",
            _ => "provider_payment_pending"
        };

    private static CommerceAdminManualReconciliationResponse ToManualResponse(
        string paymentOutcome,
        bool duplicatePaymentEvent,
        ProviderOrderStatus status,
        PaymentRecord? selectedPayment,
        int providerPaymentCount,
        ActivationResponse? activation,
        RenewalResponse? renewal) =>
        new(
            paymentOutcome,
            status.Outcome,
            duplicatePaymentEvent,
            providerPaymentCount,
            selectedPayment?.PaymentId,
            selectedPayment?.Status.ToString(),
            status.Route.TenantId,
            status.Route.CommerceOrderId,
            status.Route.ProviderOrderId,
            status.Route.ProductCode,
            status.Route.SubscriptionId,
            activation,
            renewal);
}

public sealed record CommerceAdminManualReconciliationResponse(
    string PaymentOutcome,
    string ActivationOutcome,
    bool DuplicatePaymentEvent,
    int ProviderPaymentCount,
    string? SelectedPaymentId,
    string? SelectedPaymentStatus,
    Guid TenantId,
    Guid CommerceOrderId,
    string ProviderOrderId,
    string ProductCode,
    Guid? SubscriptionId,
    ActivationResponse? InitialActivation,
    RenewalResponse? RenewalActivation);
