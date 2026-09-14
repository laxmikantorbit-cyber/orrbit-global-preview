using BusinessOS.Api.Commerce;
using BusinessOS.Application;
using BusinessOS.Licensing;
using BusinessOS.Payments;

namespace BusinessOS.Api.Tests;

public sealed class CommerceActivationStoreTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Initial_Activation_Is_Retrievable_Only_For_Same_Tenant()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);

        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA,
            InitialRequest("pay_initial"));

        Assert.NotNull(await store.FindActivationAsync(TenantA, activation.SubscriptionId));
        Assert.Null(await store.FindActivationAsync(TenantB, activation.SubscriptionId));
        Assert.Equal(new DateOnly(2027, 9, 13), activation.ValidUntil);
        Assert.Equal(10, activation.Entitlements.WebAdminSeats);
    }

    [Fact]
    public async Task Renewal_Extends_Subscription_And_Updates_Entitlements()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);
        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA,
            InitialRequest("pay_initial"));

        var renewal = await store.ActivateRenewalAsync(
            TenantA,
            activation.SubscriptionId,
            RenewalRequest("pay_renewal"));

        Assert.NotNull(renewal);
        Assert.Equal(new DateOnly(2027, 9, 13), renewal!.PreviousValidUntil);
        Assert.Equal(new DateOnly(2028, 9, 13), renewal.NewValidUntil);
        Assert.Equal(20, renewal.Entitlements.WebAdminSeats);

        var current = await store.FindActivationAsync(TenantA, activation.SubscriptionId);
        Assert.NotNull(current);
        Assert.Equal(new DateOnly(2028, 9, 13), current!.ValidUntil);
        Assert.Equal(20, current.Entitlements.WebAdminSeats);
    }

    [Fact]
    public async Task Renewal_For_Other_Tenant_Is_Not_Found()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);
        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA,
            InitialRequest("pay_initial"));

        var renewal = await store.ActivateRenewalAsync(
            TenantB,
            activation.SubscriptionId,
            RenewalRequest("pay_renewal"));

        Assert.Null(renewal);
    }

    [Fact]
    public async Task Checkout_Order_Can_Be_Activated_From_Captured_Payment()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);

        var checkout = await store.CreateInitialCheckoutOrderAsync(
            TenantA,
            InitialCheckoutRequest());
        var payment = CapturedPayment(
            "pay_checkout_initial",
            checkout.CommerceOrderId,
            checkout.Amount,
            checkout.CurrencyCode,
            new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

        var activation = await store.ActivateCapturedInitialOrderAsync(
            TenantA,
            payment,
            "ORRBIT-REPAIR");
        var duplicate = await store.ActivateCapturedInitialOrderAsync(
            TenantA,
            payment,
            "ORRBIT-REPAIR");

        Assert.NotNull(activation);
        Assert.NotNull(duplicate);
        Assert.Equal(activation!.SubscriptionId, duplicate!.SubscriptionId);
        Assert.Equal(checkout.CommerceOrderId, activation.OrderId);
        Assert.Equal("ORRBIT-REPAIR", checkout.RazorpayNotes["productCode"]);
        Assert.Equal(checkout.CommerceOrderId.ToString(), checkout.RazorpayNotes["commerceOrderId"]);
        Assert.Equal(new DateOnly(2027, 9, 13), activation.ValidUntil);
    }

    [Fact]
    public async Task Renewal_Checkout_Order_Can_Be_Activated_From_Captured_Payment()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);
        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA,
            InitialRequest("pay_initial"));

        var checkout = await store.CreateRenewalCheckoutOrderAsync(
            TenantA,
            activation.SubscriptionId,
            RenewalCheckoutRequest());
        Assert.NotNull(checkout);
        var payment = CapturedPayment(
            "pay_checkout_renewal",
            checkout!.CommerceOrderId,
            checkout.Amount,
            checkout.CurrencyCode,
            new DateTimeOffset(2027, 8, 1, 10, 0, 0, TimeSpan.Zero));

        var renewal = await store.ActivateCapturedRenewalOrderAsync(
            TenantA,
            activation.SubscriptionId,
            payment);
        var duplicate = await store.ActivateCapturedRenewalOrderAsync(
            TenantA,
            activation.SubscriptionId,
            payment);

        Assert.NotNull(renewal);
        Assert.NotNull(duplicate);
        Assert.Equal(renewal!.RenewalId, duplicate!.RenewalId);
        Assert.Equal(activation.SubscriptionId.ToString(), checkout.RazorpayNotes["subscriptionId"]);
        Assert.Equal(new DateOnly(2028, 9, 13), renewal.NewValidUntil);
    }

    [Fact]
    public async Task Captured_Webhook_Can_Resolve_Order_From_Razorpay_Order_Id()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);

        var checkout = await store.CreateInitialCheckoutOrderAsync(
            TenantA,
            InitialCheckoutRequest());
        await store.RecordRazorpayOrderAsync(
            TenantA,
            checkout.CommerceOrderId,
            "order_rzp_resolve_1",
            checkout.ProductCode,
            checkout.SubscriptionId);
        var payment = new PaymentRecord(
            "pay_rzp_resolve_1",
            "order_rzp_resolve_1",
            PaymentStatus.Captured,
            checked(decimal.ToInt64(checkout.Amount * 100m)),
            checkout.CurrencyCode,
            new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

        var activation = await store.ActivateCapturedInitialOrderAsync(
            TenantA,
            payment,
            "ORRBIT-REPAIR");

        Assert.NotNull(activation);
        Assert.Equal(checkout.CommerceOrderId, activation!.OrderId);
    }

    [Fact]
    public async Task Provider_Order_Route_Is_Available_Without_Tenant_Context()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);

        var checkout = await store.CreateInitialCheckoutOrderAsync(
            TenantA,
            InitialCheckoutRequest());
        await store.RecordRazorpayOrderAsync(
            TenantA,
            checkout.CommerceOrderId,
            "order_route_initial",
            checkout.ProductCode,
            checkout.SubscriptionId);

        var route = await store.FindProviderOrderRouteAsync(
            "razorpay",
            "order_route_initial");

        Assert.NotNull(route);
        Assert.Equal(TenantA, route!.TenantId);
        Assert.Equal(checkout.CommerceOrderId, route.CommerceOrderId);
        Assert.Equal("ORRBIT-REPAIR", route.ProductCode);
        Assert.Null(route.SubscriptionId);
    }

    [Fact]
    public async Task Provider_Order_Status_Reflects_Pending_Then_Activated()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);

        var checkout = await store.CreateInitialCheckoutOrderAsync(
            TenantA,
            InitialCheckoutRequest());
        await store.RecordRazorpayOrderAsync(
            TenantA,
            checkout.CommerceOrderId,
            "order_status_initial",
            checkout.ProductCode,
            checkout.SubscriptionId);

        var pending = await store.FindProviderOrderStatusAsync(
            "razorpay",
            "order_status_initial");
        Assert.NotNull(pending);
        Assert.Equal("verified_pending_activation", pending!.Outcome);
        Assert.Null(pending.InitialActivation);

        await store.ActivateCapturedInitialOrderAsync(
            TenantA,
            new PaymentRecord(
                "pay_status_initial",
                "order_status_initial",
                PaymentStatus.Captured,
                checked(decimal.ToInt64(checkout.Amount * 100m)),
                checkout.CurrencyCode,
                new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero)),
            checkout.ProductCode);
        var activated = await store.FindProviderOrderStatusAsync(
            "razorpay",
            "order_status_initial");
        Assert.Equal("activated", activated!.Outcome);
        Assert.NotNull(activated.InitialActivation);
        Assert.Equal(checkout.CommerceOrderId, activated.InitialActivation!.OrderId);
    }

    private static InitialActivationRequest InitialRequest(string paymentId) => new(
        OrgA,
        "ORRBIT-REPAIR",
        Guid.NewGuid(),
        Guid.NewGuid(),
        1,
        100m,
        "USD",
        12,
        1,
        1,
        10,
        5,
        true,
        paymentId,
        new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

    private static RenewalActivationRequest RenewalRequest(string paymentId) => new(
        Guid.NewGuid(),
        2,
        100m,
        "USD",
        12,
        1,
        1,
        20,
        5,
        true,
        paymentId,
        new DateTimeOffset(2027, 8, 1, 10, 0, 0, TimeSpan.Zero));

    private static CreateInitialCheckoutOrderRequest InitialCheckoutRequest() => new(
        OrgA,
        "ORRBIT-REPAIR",
        Guid.NewGuid(),
        Guid.NewGuid(),
        1,
        100m,
        "USD",
        12,
        1,
        1,
        10,
        5,
        true);

    private static CreateRenewalCheckoutOrderRequest RenewalCheckoutRequest() => new(
        Guid.NewGuid(),
        2,
        100m,
        "USD",
        12,
        1,
        1,
        20,
        5,
        true);

    private static PaymentRecord CapturedPayment(
        string paymentId,
        Guid orderId,
        decimal amount,
        string currency,
        DateTimeOffset capturedAtUtc)
        => new(
            paymentId,
            orderId.ToString(),
            PaymentStatus.Captured,
            checked(decimal.ToInt64(amount * 100m)),
            currency,
            capturedAtUtc);
}
