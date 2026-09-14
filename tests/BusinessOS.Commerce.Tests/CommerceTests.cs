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
        var quote = AcceptedQuote();
        var order = quote.CreateOrder(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => order.Activate(Guid.NewGuid()));
    }

    [Fact]
    public void Subscription_Starts_On_Payment_Date()
    {
        var paidAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        var order = AcceptedQuote().CreateOrder(Guid.NewGuid());
        order.CapturePayment("pay_1", paidAt);
        var subscription = order.Activate(Guid.NewGuid());
        Assert.Equal(new DateOnly(2026, 9, 1), subscription.StartsOn);
        Assert.Equal(new DateOnly(2027, 8, 31), subscription.ValidUntil);
    }

    private static Quote AcceptedQuote()
    {
        var quote = NewQuote();
        quote.Accept(DateTimeOffset.UtcNow);
        return quote;
    }

    private static Order PaidOrder(string paymentId)
    {
        var order = AcceptedQuote().CreateOrder(Guid.NewGuid());
        order.CapturePayment(paymentId, DateTimeOffset.UtcNow);
        return order;
    }

    private static Quote NewQuote(DateTimeOffset? created = null, DateTimeOffset? validUntil = null)
    {
        var now = created ?? DateTimeOffset.UtcNow;
        var billing = new BillingRule(100m, "USD", BillingCycle.Annual, 12);
        var entitlements = new EntitlementProfile(2, 3, 10, 5, true, new HashSet<string> { "WEB" });
        var snapshot = new CommercialSnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, billing, entitlements);
        return new Quote(Guid.NewGuid(), Tenant, Org, snapshot, now, validUntil ?? now.AddDays(7));
    }
}
