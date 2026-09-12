namespace BusinessOS.Payments;

public sealed class PaymentWebhookReceiver
{
    private readonly PaymentProcessor _processor;
    private readonly string _secret;

    public PaymentWebhookReceiver(PaymentProcessor processor, string secret)
    {
        _processor = processor;
        _secret = string.IsNullOrWhiteSpace(secret)
            ? throw new ArgumentException("Webhook secret is required.")
            : secret;
    }

    public PaymentProcessResult Receive(
        string rawBody,
        string signature,
        PaymentWebhookMessage message)
    {
        if (!WebhookSignatureVerifier.Verify(rawBody, signature, _secret))
            throw new UnauthorizedAccessException("Invalid webhook signature.");

        return _processor.Process(message);
    }
}
