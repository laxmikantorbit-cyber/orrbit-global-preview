using BusinessOS.Api.Billing;
using BusinessOS.Api.Commerce;
using BusinessOS.Api.Tenancy;
using BusinessOS.Payments;

namespace BusinessOS.Api.Payments;

public static class FreeTestingPublicCheckoutEndpoints
{
    private const string RazorpayProvider = "razorpay";
    private const string DemoProductCode = "AI_REPAIR";
    private static readonly Guid DemoOrganisationId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DemoPlanId =
        Guid.Parse("99999999-1111-1111-1111-111111111111");
    private static readonly Guid DemoPlanVersionId =
        Guid.Parse("99999999-2222-2222-2222-222222222222");

    public static IEndpointRouteBuilder MapFreeTestingPublicCheckoutEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public");

        group.MapPost("/ai-repair/purchase", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            RazorpayCheckoutService checkoutService,
            ICommerceActivationStore commerceStore,
            IPaymentEventStore paymentStore,
            BillingAutomationService billingAutomation,
            CancellationToken cancellationToken) =>
        {
            if (!IsFreeTestingMode(configuration, environment))
                return Results.NotFound(new ErrorResponse(
                    "Public free-testing purchase is not enabled."));

            try
            {
                var checkout = await checkoutService.CreateInitialAsync(
                    PocIdentitySeed.TenantAId,
                    DemoAiRepairCheckoutRequest(),
                    cancellationToken);
                var payment = await paymentStore.ProcessAsync(
                    RazorpayProvider,
                    CapturedPayment(checkout),
                    cancellationToken);
                var activation = await commerceStore.ActivateCapturedInitialOrderAsync(
                    checkout.TenantId,
                    payment.Payment,
                    DemoProductCode,
                    cancellationToken);
                if (activation is null)
                    return Results.NotFound(new ErrorResponse(
                        "Free-testing activation could not be completed."));
                await billingAutomation.EnsureForOrderAsync(
                    checkout.TenantId, checkout.CommerceOrderId, cancellationToken);

                var state = await commerceStore.FindSubscriptionStateAsync(
                    activation.TenantId,
                    activation.SubscriptionId,
                    cancellationToken);
                var entitlement = state is null
                    ? null
                    : EntitlementStatusEvaluator.Evaluate(
                        state,
                        DateOnly.FromDateTime(DateTime.UtcNow));

                var activationCode = await commerceStore.GetOrCreateDesktopActivationCodeAsync(
                    activation.TenantId,
                    activation.SubscriptionId,
                    cancellationToken);
                if (activationCode is null)
                    return Results.NotFound(new ErrorResponse(
                        "License activation code could not be generated."));

                return Results.Ok(new FreeTestingPublicPurchaseResponse(
                    checkout,
                    payment.Duplicate,
                    activation,
                    entitlement,
                    activationCode));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        return app;
    }

    private static CreateInitialCheckoutOrderRequest DemoAiRepairCheckoutRequest() =>
        new(
            DemoOrganisationId,
            "AI_REPAIR",
            DemoPlanId,
            DemoPlanVersionId,
            1,
            29999m,
            "INR",
            12,
            1,
            1,
            10,
            10,
            true);

    private static PaymentWebhookMessage CapturedPayment(
        RazorpayCheckoutOrderResponse checkout)
    {
        var capturedAtUtc = DateTimeOffset.UtcNow;
        return new PaymentWebhookMessage(
            $"evt_public_free_test_{Guid.NewGuid():N}",
            $"pay_public_free_test_{Guid.NewGuid():N}",
            checkout.RazorpayOrderId,
            PaymentStatus.Captured,
            checkout.RazorpayAmount,
            checkout.CurrencyCode,
            capturedAtUtc);
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
}

public sealed record FreeTestingPublicPurchaseResponse(
    RazorpayCheckoutOrderResponse Checkout,
    bool DuplicatePaymentEvent,
    ActivationResponse Activation,
    EntitlementStatusResponse? Entitlement,
    LicenseActivationCodeResponse ActivationCode);
