using BusinessOS.Api.Commerce;
using BusinessOS.Api.Payments;
using BusinessOS.Application;
using BusinessOS.Commerce;
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
    public async Task Subscription_State_Is_Tenant_Scoped_And_Includes_License_Mapping()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(), signer);
        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA, InitialRequest("pay_state"));

        var state = await store.FindSubscriptionStateAsync(
            TenantA, activation.SubscriptionId);

        Assert.NotNull(state);
        Assert.Equal(activation.LicenseId, state!.LicenseId);
        Assert.Equal(activation.ProductCode, state.ProductCode);
        Assert.Equal(SubscriptionStatus.Active, state.SubscriptionStatus);
        Assert.Null(await store.FindSubscriptionStateAsync(
            TenantB, activation.SubscriptionId));
    }

    [Fact]
    public async Task Cancel_At_Period_End_Is_Idempotent_And_Blocks_Renewal_Checkout()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(), signer);
        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA, InitialRequest("pay_cancel"));

        var first = await store.CancelSubscriptionAtPeriodEndAsync(
            TenantA, activation.SubscriptionId);
        var second = await store.CancelSubscriptionAtPeriodEndAsync(
            TenantA, activation.SubscriptionId);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(SubscriptionStatus.Cancelled, first!.SubscriptionStatus);
        Assert.Equal(first, second);
        Assert.NotNull(await store.FindActivationAsync(
            TenantA, activation.SubscriptionId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CreateRenewalCheckoutOrderAsync(
                TenantA, activation.SubscriptionId, RenewalCheckoutRequest()));
    }

    [Fact]
    public async Task Provider_Subscription_Binding_Is_Tenant_Scoped_And_State_Updatable()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(), signer);
        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA, InitialRequest("pay_autopay_binding"));
        var binding = new ProviderSubscriptionBinding(
            TenantA, activation.SubscriptionId, "razorpay", "sub_test_1",
            "plan_test_1", 1800000000, 12, "created", true, false,
            "https://example.test/authorize", DateTimeOffset.UtcNow);

        var recorded = await store.RecordProviderSubscriptionAsync(binding);
        var sameTenant = await store.FindProviderSubscriptionAsync(
            TenantA, activation.SubscriptionId, "razorpay");
        var otherTenant = await store.FindProviderSubscriptionAsync(
            TenantB, activation.SubscriptionId, "razorpay");
        var routed = await store.FindProviderSubscriptionRouteAsync(
            "razorpay", "sub_test_1");
        var updated = await store.UpdateProviderSubscriptionStateAsync(
            "razorpay", "sub_test_1", "halted", false, false);

        Assert.Equal(recorded, sameTenant);
        Assert.Null(otherTenant);
        Assert.Equal(activation.SubscriptionId, routed!.SubscriptionId);
        Assert.Equal("halted", updated!.Status);
        Assert.False(updated.AutoRenewEnabled);
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

    [Fact]
    public async Task Fetched_Razorpay_Payment_Can_Reconcile_And_Activate_Order()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);
        var processor = new PaymentProcessor();

        var checkout = await store.CreateInitialCheckoutOrderAsync(
            TenantA,
            InitialCheckoutRequest());
        await store.RecordRazorpayOrderAsync(
            TenantA,
            checkout.CommerceOrderId,
            "order_fetch_reconcile_1",
            checkout.ProductCode,
            checkout.SubscriptionId);
        var fetched = new BusinessOS.Api.Payments.RazorpayPaymentResult(
            "pay_fetch_reconcile_1",
            "order_fetch_reconcile_1",
            checked(decimal.ToInt64(checkout.Amount * 100m)),
            checkout.CurrencyCode,
            "captured",
            true,
            new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));
        var processed = processor.Process(
            BusinessOS.Api.Payments.RazorpayHttpPaymentClient.ToWebhookMessage(fetched));

        var activation = await store.ActivateCapturedInitialOrderAsync(
            TenantA,
            processed.Payment,
            checkout.ProductCode);

        Assert.NotNull(activation);
        Assert.Equal(checkout.CommerceOrderId, activation!.OrderId);
    }

    [Fact]
    public async Task Admin_Snapshot_Shows_Pending_Order_And_Ledger_Payment()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);
        var paymentEvents = new InMemoryPaymentEventStore(new PaymentProcessor());

        var checkout = await store.CreateInitialCheckoutOrderAsync(
            TenantA,
            InitialCheckoutRequest());
        await store.RecordRazorpayOrderAsync(
            TenantA,
            checkout.CommerceOrderId,
            "order_admin_pending_1",
            checkout.ProductCode,
            checkout.SubscriptionId);
        await paymentEvents.ProcessAsync(
            "razorpay",
            new PaymentWebhookMessage(
                "event_admin_pending_1", "pay_admin_pending_1",
                "order_admin_pending_1", PaymentStatus.Pending,
                checked(decimal.ToInt64(checkout.Amount * 100m)), checkout.CurrencyCode));

        var snapshot = await store.GetAdminSnapshotAsync(TenantA, 50);
        var payments = await paymentEvents.ListPaymentsForProviderOrdersAsync(
            "razorpay",
            ["order_admin_pending_1"],
            50);

        Assert.Single(snapshot.Orders);
        Assert.Equal(checkout.CommerceOrderId, snapshot.Orders[0].CommerceOrderId);
        Assert.Single(payments);
        Assert.Equal("Pending", payments[0].Status);
        Assert.Empty(snapshot.Activations);
    }

    [Fact]
    public async Task Admin_Snapshot_Shows_Activation_After_Captured_Payment()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);

        var checkout = await store.CreateInitialCheckoutOrderAsync(
            TenantA,
            InitialCheckoutRequest());
        var activation = await store.ActivateCapturedInitialOrderAsync(
            TenantA,
            new PaymentRecord(
                "pay_admin_captured_1",
                checkout.CommerceOrderId.ToString(),
                PaymentStatus.Captured,
                checked(decimal.ToInt64(checkout.Amount * 100m)),
                checkout.CurrencyCode,
                new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero)),
            checkout.ProductCode);

        var snapshot = await store.GetAdminSnapshotAsync(TenantA, 50);

        Assert.NotNull(activation);
        Assert.Empty(snapshot.Orders);
        Assert.Single(snapshot.Activations);
        Assert.Equal(activation!.SubscriptionId, snapshot.Activations[0].SubscriptionId);
    }

    [Fact]
    public async Task Admin_Manual_Reconcile_Can_Use_Captured_Order_Payment()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(), signer);
        var paymentEvents = new InMemoryPaymentEventStore(new PaymentProcessor());
        var checkout = await store.CreateInitialCheckoutOrderAsync(
            TenantA, InitialCheckoutRequest());
        await store.RecordRazorpayOrderAsync(TenantA, checkout.CommerceOrderId,
            "order_admin_manual_1", checkout.ProductCode, checkout.SubscriptionId);
        var payments = new[]
        {
            new RazorpayPaymentResult("pay_admin_manual_pending", "order_admin_manual_1",
                10000, checkout.CurrencyCode, "authorized", false,
                new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero)),
            new RazorpayPaymentResult("pay_admin_manual_captured", "order_admin_manual_1",
                10000, checkout.CurrencyCode, "captured", true,
                new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero))
        };
        var selected = payments.OrderByDescending(x => x.Captured).ThenByDescending(x => x.CreatedAtUtc).First();
        var processed = await paymentEvents.ProcessAsync("razorpay",
            RazorpayHttpPaymentClient.ToWebhookMessage(selected));
        var activation = await store.ActivateCapturedInitialOrderAsync(
            TenantA, processed.Payment, checkout.ProductCode);

        Assert.Equal("pay_admin_manual_captured", processed.Payment.PaymentId);
        Assert.NotNull(activation);
        Assert.Equal(checkout.CommerceOrderId, activation!.OrderId);
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
