using BusinessOS.Payments;

namespace BusinessOS.Api.Payments;

public static class RazorpayCheckoutSignatureVerifier
{
    public static bool Verify(
        string razorpayOrderId,
        string razorpayPaymentId,
        string suppliedSignature,
        string secret)
    {
        if (string.IsNullOrWhiteSpace(razorpayOrderId) ||
            string.IsNullOrWhiteSpace(razorpayPaymentId) ||
            string.IsNullOrWhiteSpace(suppliedSignature) ||
            string.IsNullOrWhiteSpace(secret))
            return false;

        return WebhookSignatureVerifier.Verify(
            Payload(razorpayOrderId, razorpayPaymentId),
            suppliedSignature,
            secret);
    }

    public static string Payload(
        string razorpayOrderId,
        string razorpayPaymentId)
    {
        if (string.IsNullOrWhiteSpace(razorpayOrderId))
            throw new ArgumentException("Razorpay order id is required.", nameof(razorpayOrderId));
        if (string.IsNullOrWhiteSpace(razorpayPaymentId))
            throw new ArgumentException("Razorpay payment id is required.", nameof(razorpayPaymentId));

        return $"{razorpayOrderId.Trim()}|{razorpayPaymentId.Trim()}";
    }
}
