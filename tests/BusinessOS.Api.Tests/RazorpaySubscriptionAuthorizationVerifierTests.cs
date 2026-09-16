using BusinessOS.Api.Payments;
using BusinessOS.Payments;

namespace BusinessOS.Api.Tests;

public sealed class RazorpaySubscriptionAuthorizationVerifierTests
{
    [Fact]
    public void Valid_Subscription_Authorization_Signature_Is_Accepted()
    {
        const string paymentId = "pay_auth_1";
        const string subscriptionId = "sub_auth_1";
        const string secret = "unit_test_secret";
        var signature = WebhookSignatureVerifier.Compute(
            RazorpaySubscriptionAuthorizationVerifier.Payload(paymentId, subscriptionId),
            secret);

        Assert.True(RazorpaySubscriptionAuthorizationVerifier.Verify(
            paymentId, subscriptionId, signature, secret));
    }

    [Fact]
    public void Tampered_Subscription_Authorization_Is_Rejected()
    {
        const string secret = "unit_test_secret";
        var signature = WebhookSignatureVerifier.Compute(
            RazorpaySubscriptionAuthorizationVerifier.Payload("pay_auth_1", "sub_auth_1"),
            secret);

        Assert.False(RazorpaySubscriptionAuthorizationVerifier.Verify(
            "pay_tampered", "sub_auth_1", signature, secret));
    }

    [Fact]
    public void Subscription_Authorization_Payload_Uses_Payment_Then_Subscription()
    {
        Assert.Equal(
            "pay_auth_1|sub_auth_1",
            RazorpaySubscriptionAuthorizationVerifier.Payload(
                " pay_auth_1 ", " sub_auth_1 "));
    }
}
