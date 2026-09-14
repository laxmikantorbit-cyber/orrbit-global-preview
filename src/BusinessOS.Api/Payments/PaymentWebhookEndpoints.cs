using System.Text;
using BusinessOS.Api.Commerce;
using BusinessOS.Payments;

namespace BusinessOS.Api.Payments;

public static class PaymentWebhookEndpoints
{
    private const string RazorpayProvider = "razorpay";

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

            var route = await store.FindProviderOrderRouteAsync(
                RazorpayProvider,
                webhook.ProviderOrderId,
                cancellationToken);

            var routeError = ValidateRoute(webhook, route);
            if (routeError is not null)
                return Results.BadRequest(new ErrorResponse(routeError));

            var tenantId = webhook.TenantId ?? route?.TenantId;
            if (tenantId is null)
                return Results.NotFound(new ErrorResponse(
                    "Razorpay provider order route was not found."));

            var subscriptionId = webhook.SubscriptionId ?? route?.SubscriptionId;
            var productCode = string.IsNullOrWhiteSpace(webhook.ProductCode)
                ? route?.ProductCode
                : webhook.ProductCode.Trim();

            if (subscriptionId is Guid renewalSubscriptionId)
            {
                var renewal = await store.ActivateCapturedRenewalOrderAsync(
                    tenantId.Value,
                    renewalSubscriptionId,
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

            if (string.IsNullOrWhiteSpace(productCode))
                return Results.BadRequest(new ErrorResponse(
                    "Initial purchase webhook requires productCode note or provider route."));

            var activation = await store.ActivateCapturedInitialOrderAsync(
                tenantId.Value,
                paymentResult.Payment,
                productCode,
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

    private static string? ValidateRoute(
        RazorpayPaymentWebhook webhook,
        ProviderOrderRoute? route)
    {
        if (route is null)
            return null;

        if (webhook.TenantId is Guid noteTenant && noteTenant != route.TenantId)
            return "Webhook tenantId note does not match provider order route.";
        if (webhook.SubscriptionId is Guid noteSubscription &&
            route.SubscriptionId != noteSubscription)
            return "Webhook subscriptionId note does not match provider order route.";
        if (!string.IsNullOrWhiteSpace(webhook.ProductCode) &&
            !string.Equals(
                webhook.ProductCode.Trim(),
                route.ProductCode,
                StringComparison.Ordinal))
            return "Webhook productCode note does not match provider order route.";

        return null;
    }
}

public sealed record PaymentWebhookResponse(
    string Outcome,
    bool DuplicatePaymentEvent,
    ActivationResponse? InitialActivation,
    RenewalResponse? RenewalActivation);
