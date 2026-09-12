using BusinessOS.Payments;

namespace BusinessOS.Payments.Tests;

public sealed class PaymentReliabilityTests
{
    private const string Secret = "test-webhook-secret";

    [Fact]
    public void Valid_Signed_Captured_Webhook_Creates_Payment_And_Outbox()
    {
        var processor = new PaymentProcessor();
        var receiver = new PaymentWebhookReceiver(processor, Secret);
        var message = Captured("evt-1", "pay-1");
        var raw = "{\"event\":\"payment.captured\"}";
        var signature = WebhookSignatureVerifier.Compute(raw, Secret);

        var result = receiver.Receive(raw, signature, message);

        Assert.True(result.Accepted);
        Assert.False(result.Duplicate);
        Assert.Equal(PaymentStatus.Captured, result.Payment.Status);
        Assert.Single(processor.Outbox);
    }

    [Fact]
    public void Invalid_Signature_Is_Rejected_Without_State_Change()
    {
        var processor = new PaymentProcessor();
        var receiver = new PaymentWebhookReceiver(processor, Secret);
        var message = Captured("evt-2", "pay-2");

        Assert.Throws<UnauthorizedAccessException>(() =>
            receiver.Receive("{}", "bad-signature", message));

        Assert.Empty(processor.Payments);
        Assert.Empty(processor.Outbox);
    }

    [Fact]
    public void Duplicate_Event_Is_Harmless()
    {
        var processor = new PaymentProcessor();
        var message = Captured("evt-3", "pay-3");

        processor.Process(message);
        var duplicate = processor.Process(message);

        Assert.True(duplicate.Duplicate);
        Assert.Single(processor.Payments);
        Assert.Single(processor.Outbox);
    }

    [Fact]
    public void Same_Payment_With_New_Event_Does_Not_Duplicate_Entitlement_Work()
    {
        var processor = new PaymentProcessor();
        processor.Process(Captured("evt-4a", "pay-4"));

        var result = processor.Process(Captured("evt-4b", "pay-4"));

        Assert.True(result.Duplicate);
        Assert.Single(processor.Payments);
        Assert.Single(processor.Outbox);
    }

    [Fact]
    public void Pending_Then_Captured_Queues_Entitlement_Exactly_Once()
    {
        var processor = new PaymentProcessor();
        processor.Process(Message("evt-5a", "pay-5", PaymentStatus.Pending));

        var result = processor.Process(Captured("evt-5b", "pay-5"));

        Assert.Equal(PaymentStatus.Captured, result.Payment.Status);
        Assert.Single(processor.Outbox);
    }

    [Fact]
    public void Late_Failed_Event_Cannot_Downgrade_Captured_Payment()
    {
        var processor = new PaymentProcessor();
        processor.Process(Captured("evt-6a", "pay-6"));

        var result = processor.Process(
            Message("evt-6b", "pay-6", PaymentStatus.Failed));

        Assert.Equal(PaymentStatus.Captured, result.Payment.Status);
        Assert.True(result.Duplicate);
        Assert.Single(processor.Outbox);
    }

    [Fact]
    public void Signature_Is_Bound_To_Exact_Request_Body()
    {
        var sig = WebhookSignatureVerifier.Compute("request-one", Secret);
        Assert.True(WebhookSignatureVerifier.Verify("request-one", sig, Secret));
        Assert.False(WebhookSignatureVerifier.Verify("request-two", sig, Secret));
    }
    private static PaymentWebhookMessage Captured(
        string eventId,
        string paymentId)
        => Message(eventId, paymentId, PaymentStatus.Captured);

    private static PaymentWebhookMessage Message(
        string eventId,
        string paymentId,
        PaymentStatus status)
        => new(
            eventId,
            paymentId,
            "order-1",
            status,
            10000,
            "INR");
}
