using BusinessOS.Api.Commerce;
using BusinessOS.Application;
using BusinessOS.Licensing;
using BusinessOS.Payments;

namespace BusinessOS.Api.Tests;

public sealed class PostgresCommerceCheckoutOrderSmokeTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlanA = Guid.Parse("73333333-3333-3333-3333-333333333331");
    private static readonly Guid PlanVersionA = Guid.Parse("74444444-4444-4444-4444-444444444441");

    [Fact]
    public async Task Postgres_Checkout_Order_Flows_Through_Captured_Webhook_Activation()
    {
        var cs = Environment.GetEnvironmentVariable("BUSINESSOS_COMMERCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
            return;

        using var signer = new LeaseSigner();
        await using var store = new PostgresCommerceActivationStore(
            cs,
            new PaymentSubscriptionActivationService(),
            signer);

        var suffix = Guid.NewGuid().ToString("N");
        var checkout = await store.CreateInitialCheckoutOrderAsync(
            TenantA,
            InitialCheckoutRequest());
        Assert.Equal(checkout.CommerceOrderId.ToString(), checkout.RazorpayNotes["commerceOrderId"]);
        Assert.Equal("ORRBIT-REPAIR", checkout.RazorpayNotes["productCode"]);
        var razorpayOrderId = $"order_pg_checkout_{suffix}";
        await store.RecordRazorpayOrderAsync(
            TenantA,
            checkout.CommerceOrderId,
            razorpayOrderId,
            checkout.ProductCode,
            checkout.SubscriptionId);
        var initialRoute = await store.FindProviderOrderRouteAsync(
            "razorpay",
            razorpayOrderId);
        Assert.NotNull(initialRoute);
        Assert.Equal(TenantA, initialRoute!.TenantId);
        Assert.Equal(checkout.CommerceOrderId, initialRoute.CommerceOrderId);
        Assert.Null(initialRoute.SubscriptionId);

        var activation = await store.ActivateCapturedInitialOrderAsync(
            TenantA,
            CapturedPayment($"pay_pg_checkout_{suffix}", checkout, razorpayOrderId),
            "ORRBIT-REPAIR");
        Assert.NotNull(activation);
        Assert.Equal(checkout.CommerceOrderId, activation!.OrderId);
        Assert.Equal(new DateOnly(2027, 9, 13), activation.ValidUntil);

        var renewalCheckout = await store.CreateRenewalCheckoutOrderAsync(
            TenantA,
            activation.SubscriptionId,
            RenewalCheckoutRequest());
        Assert.NotNull(renewalCheckout);
        Assert.Equal(activation.SubscriptionId.ToString(), renewalCheckout!.RazorpayNotes["subscriptionId"]);

        var renewalRazorpayOrderId = $"order_pg_checkout_renewal_{suffix}";
        await store.RecordRazorpayOrderAsync(
            TenantA,
            renewalCheckout.CommerceOrderId,
            renewalRazorpayOrderId,
            renewalCheckout.ProductCode,
            renewalCheckout.SubscriptionId);
        var renewalRoute = await store.FindProviderOrderRouteAsync(
            "razorpay",
            renewalRazorpayOrderId);
        Assert.NotNull(renewalRoute);
        Assert.Equal(activation.SubscriptionId, renewalRoute!.SubscriptionId);

        var renewal = await store.ActivateCapturedRenewalOrderAsync(
            TenantA,
            activation.SubscriptionId,
            CapturedPayment($"pay_pg_checkout_renewal_{suffix}", renewalCheckout, renewalRazorpayOrderId));
        var current = await store.FindActivationAsync(TenantA, activation.SubscriptionId);

        Assert.NotNull(renewal);
        Assert.NotNull(current);
        Assert.Equal(new DateOnly(2028, 9, 13), renewal!.NewValidUntil);
        Assert.Equal(new DateOnly(2028, 9, 13), current!.ValidUntil);
        Assert.Equal(20, current.Entitlements.WebAdminSeats);
    }

    private static CreateInitialCheckoutOrderRequest InitialCheckoutRequest() => new(
        OrgA,
        "ORRBIT-REPAIR",
        PlanA,
        PlanVersionA,
        1,
        100m,
        "INR",
        12,
        1,
        1,
        10,
        5,
        true);

    private static CreateRenewalCheckoutOrderRequest RenewalCheckoutRequest() => new(
        PlanVersionA,
        2,
        100m,
        "INR",
        12,
        1,
        1,
        20,
        5,
        true);

    private static PaymentRecord CapturedPayment(
        string paymentId,
        CheckoutOrderResponse checkout,
        string? orderReference = null)
        => new(
            paymentId,
            orderReference ?? checkout.CommerceOrderId.ToString(),
            PaymentStatus.Captured,
            checked(decimal.ToInt64(checkout.Amount * 100m)),
            checkout.CurrencyCode,
            checkout.CommerceOrderId == Guid.Empty
                ? DateTimeOffset.UtcNow
                : new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));
}
