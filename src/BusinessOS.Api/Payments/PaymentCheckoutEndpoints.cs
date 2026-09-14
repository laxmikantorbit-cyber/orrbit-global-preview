using BusinessOS.Api.Commerce;
using BusinessOS.Payments;

namespace BusinessOS.Api.Payments;

public static class PaymentCheckoutEndpoints
{
    private const string RazorpayProvider = "razorpay";

    public static IEndpointRouteBuilder MapPaymentCheckoutEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/payments/checkout");

        group.MapPost("/razorpay/verify", async (
            RazorpayCheckoutVerificationRequest verification,
            IConfiguration configuration,
            ICommerceActivationStore store,
            CancellationToken cancellationToken) =>
        {
            var preflight = await VerifyAndLoadStatusAsync(
                verification,
                configuration,
                store,
                cancellationToken);
            if (preflight.Result is not null)
                return preflight.Result;

            var status = preflight.Status!;
            return Results.Ok(new RazorpayCheckoutVerificationResponse(
                "signature_verified",
                status.Outcome,
                status.Route.TenantId,
                status.Route.CommerceOrderId,
                status.Route.ProductCode,
                status.Route.SubscriptionId,
                status.InitialActivation,
                status.RenewalActivation));
        });

        group.MapPost("/razorpay/reconcile", async (
            RazorpayCheckoutVerificationRequest verification,
            IConfiguration configuration,
            ICommerceActivationStore store,
            IRazorpayPaymentClient paymentClient,
            IPaymentEventStore paymentEvents,
            CancellationToken cancellationToken) =>
        {
            var preflight = await VerifyAndLoadStatusAsync(
                verification,
                configuration,
                store,
                cancellationToken);
            if (preflight.Result is not null)
                return preflight.Result;

            var providerPayment = await paymentClient.FetchPaymentAsync(
                verification.RazorpayPaymentId,
                cancellationToken);
            var providerError = ValidateProviderPayment(verification, providerPayment);
            if (providerError is not null)
                return Results.BadRequest(new ErrorResponse(providerError));

            var message = RazorpayHttpPaymentClient.ToWebhookMessage(providerPayment);
            var paymentResult = await paymentEvents.ProcessAsync(
                RazorpayProvider,
                message,
                cancellationToken);
            var route = preflight.Status!.Route;

            if (paymentResult.Payment.Status != PaymentStatus.Captured)
                return Results.Ok(ToReconciliationResponse(
                    PaymentOutcome(paymentResult.Payment.Status),
                    paymentResult.Duplicate,
                    preflight.Status));
            if (route.SubscriptionId is Guid subscriptionId)
            {
                var renewal = await store.ActivateCapturedRenewalOrderAsync(
                    route.TenantId,
                    subscriptionId,
                    paymentResult.Payment,
                    cancellationToken);
                return renewal is null
                    ? Results.NotFound(new ErrorResponse("Renewal order or subscription was not found for this tenant."))
                    : Results.Ok(ToReconciliationResponse(
                        "provider_payment_captured",
                        paymentResult.Duplicate,
                        new ProviderOrderStatus(route, "activated", null, renewal)));
            }

            var activation = await store.ActivateCapturedInitialOrderAsync(
                route.TenantId,
                paymentResult.Payment,
                route.ProductCode,
                cancellationToken);
            return activation is null
                ? Results.NotFound(new ErrorResponse("Initial commerce order was not found for this tenant."))
                : Results.Ok(ToReconciliationResponse(
                    "provider_payment_captured",
                    paymentResult.Duplicate,
                    new ProviderOrderStatus(route, "activated", activation, null)));
        });

        return app;
    }

    private static async Task<PreflightResult> VerifyAndLoadStatusAsync(
        RazorpayCheckoutVerificationRequest verification,
        IConfiguration configuration,
        ICommerceActivationStore store,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(verification);
        if (validationError is not null)
            return new PreflightResult(null, Results.BadRequest(new ErrorResponse(validationError)));

        var secret = configuration["Payments:RazorpayKeySecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return new PreflightResult(null, Results.Problem(
                "Razorpay key secret is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable));

        if (!RazorpayCheckoutSignatureVerifier.Verify(
            verification.RazorpayOrderId,
            verification.RazorpayPaymentId,
            verification.RazorpaySignature,
            secret))
            return new PreflightResult(null, Results.Unauthorized());

        var status = await store.FindProviderOrderStatusAsync(
            RazorpayProvider,
            verification.RazorpayOrderId,
            cancellationToken);
        return status is null
            ? new PreflightResult(null, Results.NotFound(new ErrorResponse(
                "Razorpay provider order route was not found.")))
            : new PreflightResult(status, null);
    }

    private static string? ValidateRequest(
        RazorpayCheckoutVerificationRequest verification)
    {
        if (verification is null)
            return "Checkout verification payload is required.";
        if (string.IsNullOrWhiteSpace(verification.RazorpayOrderId))
            return "razorpay_order_id is required.";
        if (string.IsNullOrWhiteSpace(verification.RazorpayPaymentId))
            return "razorpay_payment_id is required.";
        if (string.IsNullOrWhiteSpace(verification.RazorpaySignature))
            return "razorpay_signature is required.";
        return null;
    }

    private static string? ValidateProviderPayment(
        RazorpayCheckoutVerificationRequest verification,
        RazorpayPaymentResult providerPayment)
    {
        if (!string.Equals(
            providerPayment.Id,
            verification.RazorpayPaymentId.Trim(),
            StringComparison.Ordinal))
            return "Fetched Razorpay payment id does not match checkout response.";
        if (!string.Equals(
            providerPayment.OrderId,
            verification.RazorpayOrderId.Trim(),
            StringComparison.Ordinal))
            return "Fetched Razorpay payment order id does not match checkout response.";
        return null;
    }

    private static string PaymentOutcome(PaymentStatus status) =>
        status switch
        {
            PaymentStatus.Captured => "provider_payment_captured",
            PaymentStatus.Failed => "provider_payment_failed",
            _ => "provider_payment_pending"
        };

    private static RazorpayCheckoutReconciliationResponse ToReconciliationResponse(
        string paymentOutcome,
        bool duplicatePaymentEvent,
        ProviderOrderStatus status) =>
        new(
            "signature_verified",
            paymentOutcome,
            status.Outcome,
            duplicatePaymentEvent,
            status.Route.TenantId,
            status.Route.CommerceOrderId,
            status.Route.ProductCode,
            status.Route.SubscriptionId,
            status.InitialActivation,
            status.RenewalActivation);

    private sealed record PreflightResult(
        ProviderOrderStatus? Status,
        IResult? Result);
}
public sealed record RazorpayCheckoutVerificationRequest(
    string RazorpayOrderId,
    string RazorpayPaymentId,
    string RazorpaySignature);

public sealed record RazorpayCheckoutVerificationResponse(
    string VerificationOutcome,
    string ActivationOutcome,
    Guid TenantId,
    Guid CommerceOrderId,
    string ProductCode,
    Guid? SubscriptionId,
    ActivationResponse? InitialActivation,
    RenewalResponse? RenewalActivation);

public sealed record RazorpayCheckoutReconciliationResponse(
    string VerificationOutcome,
    string PaymentOutcome,
    string ActivationOutcome,
    bool DuplicatePaymentEvent,
    Guid TenantId,
    Guid CommerceOrderId,
    string ProductCode,
    Guid? SubscriptionId,
    ActivationResponse? InitialActivation,
    RenewalResponse? RenewalActivation);
