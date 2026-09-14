using System.Text;
using BusinessOS.Api.Commerce;
using BusinessOS.Payments;

namespace BusinessOS.Api.Payments;

public static class PaymentWebhookEndpoints
{
    public static IEndpointRouteBuilder MapPaymentWebhookEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/payments/webhooks");

        group.MapPost("/razorpay", async (
            HttpRequest request,
            IConfiguration configuration,
            PaymentProcessor processor,
            ICommerceActivationStore store,
            CancellationToken cancellationToken) =>
        {
            var secret = configuration["Payments:RazorpayWebhookSecret"];
            if (string.IsNullOrWhiteSpace(secret))
                return Results.Problem(
                    "Razorpay webhook secret is not configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            if (!request.Headers.TryGetValue("X-Razorpay-Signature", out var signature))
                return Results.Unauthorized();

            using var reader = new StreamReader(
                request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            var rawBody = await reader.ReadToEndAsync(cancellationToken);

            if (!WebhookSignatureVerifier.Verify(rawBody, signature.ToString(), secret))
                return Results.Unauthorized();

            var webhook = RazorpayWebhookParser.Parse(rawBody);
            var paymentResult = processor.Process(webhook.Message);
            if (paymentResult.Payment.Status != PaymentStatus.Captured)
                return Results.Ok(new PaymentWebhookResponse(
                    "payment_recorded",
                    paymentResult.Duplicate,
                    null,
                    null));

            if (webhook.SubscriptionId is Guid subscriptionId)
            {
                var renewal = await store.ActivateCapturedRenewalOrderAsync(
                    webhook.TenantId,
                    subscriptionId,
                    paymentResult.Payment,
                    cancellationToken);
                return renewal is null
                    ? Results.NotFound(new ErrorResponse("Renewal order or subscription was not found for this tenant."))
                    : Results.Ok(new PaymentWebhookResponse(
                        "renewal_activated",
                        paymentResult.Duplicate,
                        null,
                        renewal));
            }

            if (string.IsNullOrWhiteSpace(webhook.ProductCode))
                return Results.BadRequest(new ErrorResponse("Initial purchase webhook requires productCode note."));

            var activation = await store.ActivateCapturedInitialOrderAsync(
                webhook.TenantId,
                paymentResult.Payment,
                webhook.ProductCode,
                cancellationToken);
            return activation is null
                ? Results.NotFound(new ErrorResponse("Initial commerce order was not found for this tenant."))
                : Results.Ok(new PaymentWebhookResponse(
                    "initial_purchase_activated",
                    paymentResult.Duplicate,
                    activation,
                    null));
        });

        return app;
    }
}

public sealed record PaymentWebhookResponse(
    string Outcome,
    bool DuplicatePaymentEvent,
    ActivationResponse? InitialActivation,
    RenewalResponse? RenewalActivation);
