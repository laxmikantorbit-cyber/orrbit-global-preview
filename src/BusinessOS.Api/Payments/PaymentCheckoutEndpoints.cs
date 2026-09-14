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
            var validationError = ValidateRequest(verification);
            if (validationError is not null)
                return Results.BadRequest(new ErrorResponse(validationError));

            var secret = configuration["Payments:RazorpayKeySecret"];
            if (string.IsNullOrWhiteSpace(secret))
                return Results.Problem(
                    "Razorpay key secret is not configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            if (!RazorpayCheckoutSignatureVerifier.Verify(
                verification.RazorpayOrderId,
                verification.RazorpayPaymentId,
                verification.RazorpaySignature,
                secret))
                return Results.Unauthorized();

            var status = await store.FindProviderOrderStatusAsync(
                RazorpayProvider,
                verification.RazorpayOrderId,
                cancellationToken);
            if (status is null)
                return Results.NotFound(new ErrorResponse(
                    "Razorpay provider order route was not found."));

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

        return app;
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
