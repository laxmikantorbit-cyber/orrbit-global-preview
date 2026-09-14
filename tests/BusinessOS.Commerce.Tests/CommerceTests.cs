using BusinessOS.Catalog;
using BusinessOS.Commerce;

namespace BusinessOS.Commerce.Tests;

public sealed class CommerceTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Org = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Expired_Quote_Cannot_Be_Accepted()
    {
        var now = DateTimeOffset.UtcNow;
        var quote = NewQuote(now.AddDays(-2), now.AddDays(-1));
        Assert.Throws<InvalidOperationException>(() => quote.Accept(now));
    }

    [Fact]
    public void Order_Requires_Accepted_Quote()
    {
        var quote = NewQuote();
        Assert.Throws<InvalidOperationException>(() => quote.CreateOrder(Guid.NewGuid()));
        quote.Accept(DateTimeOffset.UtcNow);
        Assert.Equal(OrderStatus.PendingPayment, quote.CreateOrder(Guid.NewGuid()).Status);
    }

    [Fact]
    public void Payment_Is_Idempotent_For_Same_Payment_Id()
    {
        var order = PaidOrder("pay_1");
        order.CapturePayment("pay_1", DateTimeOffset.UtcNow.AddMinutes(2));
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal("pay_1", order.PaymentId);
    }

    [Fact]
    public void Different_Second_Payment_Is_Rejected()
    {
        var order = PaidOrder("pay_1");
        Assert.Throws<InvalidOperationException>(() =>
            order.CapturePayment("pay_2", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Activation_Requires_Captured_Payment()
    {
        var order = AcceptedQuote().CreateOrder(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => order.Activate(Guid.NewGuid()));
    }

    [Fact]
    public void Subscription_Starts_On_Payment_Date()
    {
        var paidAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        var subscription = ActivateSubscription(paidAt);
        Assert.Equal(new DateOnly(2026, 9, 1), subscription.StartsOn);
        Assert.Equal(new DateOnly(2027, 8, 31), subscription.ValidUntil);
    }

    [Fact]
    public void Early_Renewal_Extends_Existing_Expiry()
    {
        var subscription = ActivateSubscription(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        var renewalOrder = RenewalOrder(subscription, new DateTimeOffset(2027, 8, 1, 9, 0, 0, TimeSpan.Zero));

        var renewal = renewalOrder.ActivateRenewal(Guid.NewGuid(), subscription);

        Assert.Equal(new DateOnly(2028, 8, 31), subscription.ValidUntil);
        Assert.Equal(new DateOnly(2027, 8, 31), renewal.PreviousValidUntil);
        Assert.Equal(subscription.Id, renewal.SubscriptionId);
        Assert.Equal(OrderStatus.Activated, renewalOrder.Status);
    }

    [Fact]
    public void Expired_Renewal_Starts_On_Payment_Date()
    {
        var subscription = ActivateSubscription(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        var renewalOrder = RenewalOrder(subscription, new DateTimeOffset(2027, 9, 15, 9, 0, 0, TimeSpan.Zero));

        renewalOrder.ActivateRenewal(Guid.NewGuid(), subscription);

        Assert.Equal(new DateOnly(2028, 9, 14), subscription.ValidUntil);
        Assert.Equal(new DateOnly(2026, 9, 1), subscription.StartsOn);
    }

    [Fact]
    public void Renewal_Refreshes_PlanVersion_And_Entitlements()
    {
        var subscription = ActivateSubscription(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        var newVersion = Guid.NewGuid();
        var renewalOrder = RenewalOrder(subscription, new DateTimeOffset(2027, 8, 1, 9, 0, 0, TimeSpan.Zero), newVersion, 25);

        renewalOrder.ActivateRenewal(Guid.NewGuid(), subscription);

        Assert.Equal(newVersion, subscription.PlanVersionId);
        Assert.Equal(25, subscription.Entitlements.WebAdminSeats);
        Assert.Single(subscription.Renewals);
    }

    [Fact]
    public void Renewal_Requires_Captured_Payment()
    {
        var subscription = ActivateSubscription(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        var snapshot = NewSnapshot(subscription.PlanId, Guid.NewGuid());
        var order = AcceptedQuote(snapshot: snapshot).CreateOrder(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() =>
            order.ActivateRenewal(Guid.NewGuid(), subscription));
    }

    [Fact]
    public void Cross_Organisation_Renewal_Is_Rejected()
    {
        var subscription = ActivateSubscription(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        var otherOrg = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var renewalOrder = RenewalOrder(subscription, DateTimeOffset.UtcNow, organisationId: otherOrg);

        Assert.Throws<InvalidOperationException>(() =>
            renewalOrder.ActivateRenewal(Guid.NewGuid(), subscription));
    }

    [Fact]
    public void Different_Plan_Renewal_Is_Rejected()
    {
        var subscription = ActivateSubscription(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        var renewalOrder = RenewalOrder(subscription, DateTimeOffset.UtcNow, planId: Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() =>
            renewalOrder.ActivateRenewal(Guid.NewGuid(), subscription));
    }

    [Fact]
    public void OneTime_Order_Cannot_Renew_Subscription()
    {
        var subscription = ActivateSubscription(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));
        var billing = new BillingRule(100m, "USD", BillingCycle.OneTime, null);
        var snapshot = NewSnapshot(subscription.PlanId, Guid.NewGuid(), billing: billing);
        var order = AcceptedQuote(snapshot: snapshot).CreateOrder(Guid.NewGuid());
        order.CapturePayment("pay_once", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            order.ActivateRenewal(Guid.NewGuid(), subscription));
    }

    private static Quote AcceptedQuote(Guid? organisationId = null, CommercialSnapshot? snapshot = null)
    {
        var quote = NewQuote(snapshot: snapshot, organisationId: organisationId);
        quote.Accept(DateTimeOffset.UtcNow);
        return quote;
    }

    private static Order PaidOrder(string paymentId)
    {
        var order = AcceptedQuote().CreateOrder(Guid.NewGuid());
        order.CapturePayment(paymentId, DateTimeOffset.UtcNow);
        return order;
    }

    private static SubscriptionEntitlement ActivateSubscription(DateTimeOffset paidAt)
    {
        var order = AcceptedQuote().CreateOrder(Guid.NewGuid());
        order.CapturePayment("initial_" + Guid.NewGuid().ToString("N"), paidAt);
        return order.Activate(Guid.NewGuid());
    }
    private static Order RenewalOrder(
        SubscriptionEntitlement subscription,
        DateTimeOffset paidAt,
        Guid? planVersionId = null,
        int webAdminSeats = 10,
        Guid? organisationId = null,
        Guid? planId = null)
    {
        var snapshot = NewSnapshot(
            planId ?? subscription.PlanId,
            planVersionId ?? Guid.NewGuid(),
            webAdminSeats: webAdminSeats);
        var quote = AcceptedQuote(organisationId ?? subscription.OrganisationId, snapshot);
        var order = quote.CreateOrder(Guid.NewGuid());
        order.CapturePayment("renew_" + Guid.NewGuid().ToString("N"), paidAt);
        return order;
    }

    private static Quote NewQuote(
        DateTimeOffset? created = null,
        DateTimeOffset? validUntil = null,
        CommercialSnapshot? snapshot = null,
        Guid? organisationId = null)
    {
        var now = created ?? DateTimeOffset.UtcNow;
        return new Quote(Guid.NewGuid(), Tenant, organisationId ?? Org,
            snapshot ?? NewSnapshot(), now, validUntil ?? now.AddDays(7));
    }
    private static CommercialSnapshot NewSnapshot(
        Guid? planId = null,
        Guid? planVersionId = null,
        BillingRule? billing = null,
        int webAdminSeats = 10)
    {
        billing ??= new BillingRule(100m, "USD", BillingCycle.Annual, 12);
        var entitlements = new EntitlementProfile(
            2, 3, webAdminSeats, 5, true,
            new HashSet<string> { "WEB" });
        return new CommercialSnapshot(
            planId ?? Guid.NewGuid(),
            planVersionId ?? Guid.NewGuid(),
            1,
            billing,
            entitlements);
    }
}
