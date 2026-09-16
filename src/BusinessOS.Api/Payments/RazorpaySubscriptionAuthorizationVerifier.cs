using BusinessOS.Payments;

namespace BusinessOS.Api.Payments;

public static class RazorpaySubscriptionAuthorizationVerifier
{
    public static bool Verify(
        string razorpayPaymentId,
        string razorpaySubscriptionId,
        string suppliedSignature,
        string secret)
    {
        if (string.IsNullOrWhiteSpace(razorpayPaymentId) ||
            string.IsNullOrWhiteSpace(razorpaySubscriptionId) ||
            string.IsNullOrWhiteSpace(suppliedSignature) ||
            string.IsNullOrWhiteSpace(secret))
            return false;

        return WebhookSignatureVerifier.Verify(
            Payload(razorpayPaymentId, razorpaySubscriptionId),
            suppliedSignature,
            secret);
    }

    public static string Payload(string razorpayPaymentId, string razorpaySubscriptionId) =>
        $"{Required(razorpayPaymentId, "payment")}|{Required(razorpaySubscriptionId, "subscription")}";

    private static string Required(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Razorpay {field} id is required.")
            : value.Trim();
}
