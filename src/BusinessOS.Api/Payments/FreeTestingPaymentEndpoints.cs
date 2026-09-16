using BusinessOS.Api.Commerce;
using BusinessOS.Api.Tenancy;
using BusinessOS.Payments;

namespace BusinessOS.Api.Payments;

public static class FreeTestingPaymentEndpoints
{
    private const string RazorpayProvider = "razorpay";

    public static IEndpointRouteBuilder MapFreeTestingPaymentEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/payments");

        group.MapPost("/razorpay/orders/{razorpayOrderId}/capture", async (
            string razorpayOrderId,
            FreeTestingCaptureRequest? request,
            TenantContext tenant,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICommerceActivationStore commerceStore,
            IPaymentEventStore paymentStore,
            CancellationToken cancellationToken) =>
        {
            if (!IsFreeTestingMode(configuration, environment))
                return Results.NotFound(new ErrorResponse(
                    "Free-testing payment capture is not enabled."));
            if (string.IsNullOrWhiteSpace(razorpayOrderId))
                return Results.BadRequest(new ErrorResponse(
                    "razorpay_order_id is required."));

            var orderId = razorpayOrderId.Trim();
            var status = await commerceStore.FindProviderOrderStatusAsync(
                RazorpayProvider,
                orderId,
                cancellationToken);
            if (status is null || status.Route.TenantId != tenant.TenantId)
                return Results.NotFound(new ErrorResponse(
                    "Razorpay order was not found for this tenant."));

            if (status.Outcome == "activated")
                return Results.Ok(ToResponse(
                    "already_activated",
                    true,
                    status,
                    null,
                    status.InitialActivation,
                    status.RenewalActivation));

            var order = await FindPendingOrderAsync(
                commerceStore,
                tenant.TenantId,
                orderId,
                cancellationToken);
            if (order is null)
                return Results.NotFound(new ErrorResponse(
                    "Pending free-testing order was not found."));

            var message = CreateCapturedPayment(orderId, order, request);
            var processed = await paymentStore.ProcessAsync(
                RazorpayProvider,
                message,
                cancellationToken);

            return await ActivateAsync(
                status.Route,
                processed,
                commerceStore,
                cancellationToken);
        });

        group.MapPost("/razorpay/subscriptions/{subscriptionId:guid}/charge", async (
            Guid subscriptionId,
            FreeTestingAutoPayChargeRequest? request,
            TenantContext tenant,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICommerceActivationStore commerceStore,
            IPaymentEventStore paymentStore,
            CancellationToken cancellationToken) =>
        {
            if (!IsFreeTestingMode(configuration, environment))
                return Results.NotFound(new ErrorResponse(
                    "Free-testing AutoPay charge is not enabled."));

            var binding = await commerceStore.FindProviderSubscriptionAsync(
                tenant.TenantId, subscriptionId, RazorpayProvider, cancellationToken);
            if (binding is null)
                return Results.NotFound(new ErrorResponse(
                    "AutoPay subscription was not found for this tenant."));
            if (binding.CancelAtPeriodEnd)
                return Results.Conflict(new ErrorResponse(
                    "Cancelled AutoPay subscription cannot receive a simulated renewal charge."));

            var providerOrderId = string.IsNullOrWhiteSpace(request?.ProviderOrderId)
                ? $"order_free_test_autopay_{Guid.NewGuid():N}"
                : request!.ProviderOrderId.Trim();
            var status = await commerceStore.FindProviderOrderStatusAsync(
                RazorpayProvider, providerOrderId, cancellationToken);
            if (status is not null)
            {
                if (status.Route.TenantId != tenant.TenantId ||
                    status.Route.SubscriptionId != subscriptionId)
                    return Results.NotFound(new ErrorResponse(
                        "Simulated AutoPay provider order was not found for this tenant."));
                if (status.Outcome == "activated")
                    return Results.Ok(ToResponse(
                        "already_activated", true, status, null,
                        status.InitialActivation, status.RenewalActivation));
            }
            else
            {
                var template = await commerceStore.FindAutoPayRenewalTemplateAsync(
                    tenant.TenantId, subscriptionId, cancellationToken);
                if (template is null)
                    return Results.NotFound(new ErrorResponse(
                        "AutoPay renewal commercial template was not found."));
                var checkout = await commerceStore.CreateRenewalCheckoutOrderAsync(
                    tenant.TenantId,
                    subscriptionId,
                    new CreateRenewalCheckoutOrderRequest(
                        template.PlanVersionId,
                        template.PlanVersionNumber,
                        template.Amount,
                        template.CurrencyCode,
                        template.TermMonths,
                        template.DesktopDeviceLimit,
                        template.LocationLimit,
                        template.WebAdminSeats,
                        template.FieldStaffSeats,
                        template.MultiLocationCloud),
                    cancellationToken);
                if (checkout is null)
                    return Results.NotFound(new ErrorResponse(
                        "AutoPay renewal subscription was not found."));
                await commerceStore.RecordRazorpayOrderAsync(
                    tenant.TenantId,
                    checkout.CommerceOrderId,
                    providerOrderId,
                    checkout.ProductCode,
                    subscriptionId,
                    cancellationToken);
            }

            var order = await FindPendingOrderAsync(
                commerceStore, tenant.TenantId, providerOrderId, cancellationToken);
            if (order is null)
                return Results.NotFound(new ErrorResponse(
                    "Pending simulated AutoPay renewal order was not found."));
            var captureRequest = new FreeTestingCaptureRequest(
                request?.PaymentId,
                request?.CapturedAtUtc);
            var message = CreateCapturedPayment(providerOrderId, order, captureRequest);
            var processed = await paymentStore.ProcessAsync(
                RazorpayProvider, message, cancellationToken);
            await commerceStore.UpdateProviderSubscriptionStateAsync(
                RazorpayProvider,
                binding.ProviderSubscriptionId,
                "active",
                autoRenewEnabled: true,
                cancelAtPeriodEnd: false,
                cancellationToken);
            var route = await commerceStore.FindProviderOrderRouteAsync(
                RazorpayProvider, providerOrderId, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Simulated AutoPay provider order route disappeared.");
            return await ActivateAsync(
                route, processed, commerceStore, cancellationToken);
        });
        return app;
    }

    private static async Task<CommerceAdminOrderSnapshot?> FindPendingOrderAsync(
        ICommerceActivationStore commerceStore,
        Guid tenantId,
        string providerOrderId,
        CancellationToken cancellationToken)
    {
        var snapshot = await commerceStore.GetAdminSnapshotAsync(
            tenantId,
            200,
            cancellationToken);
        return snapshot.Orders.FirstOrDefault(x =>
            string.Equals(
                x.ProviderOrderId ?? x.RazorpayOrderId,
                providerOrderId,
                StringComparison.Ordinal));
    }

    private static PaymentWebhookMessage CreateCapturedPayment(
        string providerOrderId,
        CommerceAdminOrderSnapshot order,
        FreeTestingCaptureRequest? request)
    {
        var paymentId = string.IsNullOrWhiteSpace(request?.PaymentId)
            ? $"pay_free_test_{Guid.NewGuid():N}"
            : request!.PaymentId.Trim();
        var capturedAtUtc = request?.CapturedAtUtc?.ToUniversalTime()
            ?? DateTimeOffset.UtcNow;

        return new PaymentWebhookMessage(
            $"evt_free_test_{Guid.NewGuid():N}",
            paymentId,
            providerOrderId,
            PaymentStatus.Captured,
            checked(decimal.ToInt64(order.Amount * 100m)),
            order.CurrencyCode,
            capturedAtUtc);
    }
    private static async Task<IResult> ActivateAsync(
        ProviderOrderRoute route,
        PaymentProcessResult processed,
        ICommerceActivationStore commerceStore,
        CancellationToken cancellationToken)
    {
        if (route.SubscriptionId is Guid subscriptionId)
        {
            var renewal = await commerceStore.ActivateCapturedRenewalOrderAsync(
                route.TenantId,
                subscriptionId,
                processed.Payment,
                cancellationToken);

            return renewal is null
                ? Results.NotFound(new ErrorResponse(
                    "Renewal order or subscription was not found for this tenant."))
                : Results.Ok(ToResponse(
                    "provider_payment_captured",
                    processed.Duplicate,
                    new ProviderOrderStatus(route, "activated", null, renewal),
                    processed.Payment,
                    null,
                    renewal));
        }

        var activation = await commerceStore.ActivateCapturedInitialOrderAsync(
            route.TenantId,
            processed.Payment,
            route.ProductCode,
            cancellationToken);

        return activation is null
            ? Results.NotFound(new ErrorResponse(
                "Initial commerce order was not found for this tenant."))
            : Results.Ok(ToResponse(
                "provider_payment_captured",
                processed.Duplicate,
                new ProviderOrderStatus(route, "activated", activation, null),
                processed.Payment,
                activation,
                null));
    }

    private static bool IsFreeTestingMode(
        IConfiguration configuration,
        IHostEnvironment environment) =>
        !environment.IsProduction() &&
        string.Equals(
            configuration["BusinessOS:DeploymentMode"],
            "FreeTesting",
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            configuration["BusinessOS:Payments:Mode"],
            "RazorpayTestPending",
            StringComparison.OrdinalIgnoreCase);
    private static FreeTestingCaptureResponse ToResponse(
        string paymentOutcome,
        bool duplicatePaymentEvent,
        ProviderOrderStatus status,
        PaymentRecord? payment,
        ActivationResponse? activation,
        RenewalResponse? renewal) =>
        new(
            paymentOutcome,
            status.Outcome,
            duplicatePaymentEvent,
            payment?.PaymentId,
            status.Route.TenantId,
            status.Route.CommerceOrderId,
            status.Route.ProviderOrderId,
            status.Route.ProductCode,
            status.Route.SubscriptionId,
            activation,
            renewal);
}

public sealed record FreeTestingCaptureRequest(
    string? PaymentId,
    DateTimeOffset? CapturedAtUtc);

public sealed record FreeTestingAutoPayChargeRequest(
    string? ProviderOrderId,
    string? PaymentId,
    DateTimeOffset? CapturedAtUtc);
public sealed record FreeTestingCaptureResponse(
    string PaymentOutcome,
    string ActivationOutcome,
    bool DuplicatePaymentEvent,
    string? PaymentId,
    Guid TenantId,
    Guid CommerceOrderId,
    string ProviderOrderId,
    string ProductCode,
    Guid? SubscriptionId,
    ActivationResponse? InitialActivation,
    RenewalResponse? RenewalActivation);
