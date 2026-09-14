using BusinessOS.Api.Payments;
using BusinessOS.Payments;

namespace BusinessOS.Api.Tests;

public sealed class RazorpayCheckoutSignatureVerifierTests
{
    [Fact]
    public void Valid_Checkout_Signature_Is_Accepted()
    {
        const string orderId = "order_checkout_1";
        const string paymentId = "pay_checkout_1";
        const string secret = "unit_test_secret";
        var signature = WebhookSignatureVerifier.Compute(
            RazorpayCheckoutSignatureVerifier.Payload(orderId, paymentId),
            secret);

        Assert.True(RazorpayCheckoutSignatureVerifier.Verify(
            orderId,
            paymentId,
            signature,
            secret));
    }

    [Fact]
    public void Tampered_Checkout_Signature_Is_Rejected()
    {
        const string orderId = "order_checkout_1";
        const string paymentId = "pay_checkout_1";
        const string secret = "unit_test_secret";
        var signature = WebhookSignatureVerifier.Compute(
            RazorpayCheckoutSignatureVerifier.Payload(orderId, paymentId),
            secret);

        Assert.False(RazorpayCheckoutSignatureVerifier.Verify(
            orderId,
            "pay_tampered",
            signature,
            secret));
    }

    [Fact]
    public void Payload_Uses_Order_Then_Payment_Id()
    {
        Assert.Equal(
            "order_checkout_1|pay_checkout_1",
            RazorpayCheckoutSignatureVerifier.Payload(
                " order_checkout_1 ",
                " pay_checkout_1 "));
    }
}
