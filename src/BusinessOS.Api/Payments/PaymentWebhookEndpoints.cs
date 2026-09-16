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
            IPaymentEventStore paymentEvents,
            ICommerceActivationStore store,
            IProviderOrderConcurrencyGate providerOrderGate,
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

            var subscriptionWebhook = RazorpaySubscriptionWebhookParser.TryParse(rawBody);
            if (subscriptionWebhook is not null)
            {
                var subscriptionResult = await ProcessSubscriptionWebhookAsync(
                    subscriptionWebhook,
                    rawBody,
                    paymentEvents,
                    store,
                    providerOrderGate,
                    cancellationToken);
                return subscriptionResult;
            }

            var webhook = RazorpayWebhookParser.Parse(rawBody);
            await using var providerOrderLease = await providerOrderGate.AcquireAsync(
                RazorpayProvider,
                webhook.ProviderOrderId,
                cancellationToken);
            var paymentResult = await paymentEvents.ProcessAsync(
                RazorpayProvider,
                webhook.Message with { OrderId = webhook.ProviderOrderId },
                cancellationToken);
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

    private static async Task<IResult> ProcessSubscriptionWebhookAsync(
        RazorpaySubscriptionWebhook webhook,
        string rawBody,
        IPaymentEventStore paymentEvents,
        ICommerceActivationStore store,
        IProviderOrderConcurrencyGate providerOrderGate,
        CancellationToken cancellationToken)
    {
        var binding = await store.FindProviderSubscriptionRouteAsync(
            RazorpayProvider,
            webhook.ProviderSubscriptionId,
            cancellationToken);
        if (binding is null)
            return Results.NotFound(new ErrorResponse(
                "Razorpay subscription route was not found."));

        var autoRenewEnabled = binding.CancelAtPeriodEnd
            ? false
            : webhook.Status.ToLowerInvariant() switch
            {
                "cancelled" or "completed" or "expired" or "halted" => false,
                _ => true
            };
        var updated = await store.UpdateProviderSubscriptionStateAsync(
            RazorpayProvider,
            webhook.ProviderSubscriptionId,
            webhook.Status,
            autoRenewEnabled,
            binding.CancelAtPeriodEnd,
            cancellationToken);

        var outcome = "subscription_state_recorded";
        bool duplicatePayment = false;
        string? paymentId = null;
        RenewalResponse? renewalActivation = null;
        if (string.Equals(
            webhook.EventName,
            "subscription.charged",
            StringComparison.OrdinalIgnoreCase))
        {
            var paymentWebhook = RazorpayWebhookParser.Parse(rawBody);
            await using var providerOrderLease = await providerOrderGate.AcquireAsync(
                RazorpayProvider,
                paymentWebhook.ProviderOrderId,
                cancellationToken);
            var route = await store.FindProviderOrderRouteAsync(
                RazorpayProvider,
                paymentWebhook.ProviderOrderId,
                cancellationToken);
            if (route is not null &&
                (route.TenantId != binding.TenantId ||
                 route.SubscriptionId != binding.SubscriptionId))
                return Results.BadRequest(new ErrorResponse(
                    "Recurring charge provider order route does not match AutoPay subscription."));

            var template = await store.FindAutoPayRenewalTemplateAsync(
                binding.TenantId,
                binding.SubscriptionId,
                cancellationToken);
            if (template is null)
                return Results.NotFound(new ErrorResponse(
                    "AutoPay renewal commercial template was not found."));

            var expectedPaise = checked(decimal.ToInt64(template.Amount * 100m));
            if (paymentWebhook.Message.AmountPaise != expectedPaise ||
                !string.Equals(paymentWebhook.Message.Currency, template.CurrencyCode,
                    StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new ErrorResponse(
                    "Recurring charge amount or currency does not match current commercial terms."));

            var paymentResult = await paymentEvents.ProcessAsync(
                RazorpayProvider,
                paymentWebhook.Message with { OrderId = paymentWebhook.ProviderOrderId },
                cancellationToken);
            duplicatePayment = paymentResult.Duplicate;
            paymentId = paymentResult.Payment.PaymentId;

            if (paymentResult.Payment.Status == PaymentStatus.Captured)
            {
                if (binding.CancelAtPeriodEnd)
                    return Results.Conflict(new ErrorResponse(
                        "Recurring charge arrived after AutoPay cancellation and requires manual reconciliation."));

                if (route is null)
                {
                    var checkout = await store.CreateRenewalCheckoutOrderAsync(
                        binding.TenantId,
                        binding.SubscriptionId,
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

                    await store.RecordRazorpayOrderAsync(
                        binding.TenantId,
                        checkout.CommerceOrderId,
                        paymentWebhook.ProviderOrderId,
                        checkout.ProductCode,
                        binding.SubscriptionId,
                        cancellationToken);
                }

                renewalActivation = await store.ActivateCapturedRenewalOrderAsync(
                    binding.TenantId,
                    binding.SubscriptionId,
                    paymentResult.Payment,
                    cancellationToken);
                if (renewalActivation is null)
                    return Results.NotFound(new ErrorResponse(
                        "AutoPay renewal order was not found after recurring charge."));
                outcome = paymentResult.Duplicate
                    ? "subscription_charge_already_renewed"
                    : "subscription_charge_renewed";
            }
        }

        return Results.Ok(new RazorpaySubscriptionWebhookResponse(
            outcome,
            duplicatePayment,
            binding.TenantId,
            binding.SubscriptionId,
            webhook.ProviderSubscriptionId,
            webhook.Status,
            paymentId,
            updated?.AutoRenewEnabled ?? autoRenewEnabled,
            renewalActivation));
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

public sealed record RazorpaySubscriptionWebhookResponse(
    string Outcome,
    bool DuplicatePaymentEvent,
    Guid TenantId,
    Guid SubscriptionId,
    string ProviderSubscriptionId,
    string ProviderStatus,
    string? PaymentId,
    bool AutoRenewEnabled,
    RenewalResponse? RenewalActivation);
