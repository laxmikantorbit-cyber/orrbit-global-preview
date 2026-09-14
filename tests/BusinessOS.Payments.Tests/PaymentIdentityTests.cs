using BusinessOS.Payments;

namespace BusinessOS.Payments.Tests;

public sealed class PaymentIdentityTests
{
    [Fact]
    public void Existing_Payment_Cannot_Be_Reused_For_Different_Order()
    {
        var processor = new PaymentProcessor();
        var first = new PaymentWebhookMessage(
            "event-a", "payment-a", "order-a",
            PaymentStatus.Captured, 10000, "INR", DateTimeOffset.Parse("2026-09-14T00:00:00Z"));
        var second = new PaymentWebhookMessage(
            "event-b", "payment-a", "order-b",
            PaymentStatus.Captured, 10000, "INR", DateTimeOffset.Parse("2026-09-14T00:00:00Z"));

        processor.Process(first);

        Assert.Throws<InvalidOperationException>(() => processor.Process(second));
        Assert.Single(processor.Payments);
        Assert.Single(processor.Outbox);
    }
}
