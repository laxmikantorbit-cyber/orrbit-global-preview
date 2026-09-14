using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BusinessOS.Api.Payments;
using BusinessOS.Payments;
using Microsoft.Extensions.Configuration;

namespace BusinessOS.Api.Tests;

public sealed class RazorpayPaymentClientTests
{
    [Fact]
    public async Task Razorpay_Payment_Client_Fetches_Payment_With_Basic_Auth()
    {
        var handler = new CapturingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "id":"pay_test_123",
                      "order_id":"order_test_123",
                      "amount":10000,
                      "currency":"INR",
                      "status":"captured",
                      "captured":true,
                      "captured_at":1789413600,
                      "created_at":1789413500
                    }
                    """, Encoding.UTF8, "application/json")
            });
        var client = new RazorpayHttpPaymentClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.razorpay.com") },
            Config());

        var result = await client.FetchPaymentAsync("pay_test_123");

        Assert.Equal("pay_test_123", result.Id);
        Assert.Equal("order_test_123", result.OrderId);
        Assert.Equal(10000, result.Amount);
        Assert.True(result.Captured);
        Assert.Equal("/v1/payments/pay_test_123", handler.Request!.RequestUri!.PathAndQuery);
        Assert.Equal(HttpMethod.Get, handler.Request.Method);
        AssertAuth(handler.Request.Headers.Authorization);
    }

    [Fact]
    public void Fetched_Captured_Payment_Maps_To_Webhook_Message()
    {
        var result = new RazorpayPaymentResult(
            "pay_map_1",
            "order_map_1",
            10000,
            "INR",
            "captured",
            true,
            new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

        var message = RazorpayHttpPaymentClient.ToWebhookMessage(result);

        Assert.Equal("razorpay.fetch:pay_map_1:captured", message.EventId);
        Assert.Equal("pay_map_1", message.PaymentId);
        Assert.Equal("order_map_1", message.OrderId);
        Assert.Equal(PaymentStatus.Captured, message.Status);
        Assert.Equal(10000, message.AmountPaise);
        Assert.NotNull(message.CapturedAtUtc);
    }

    private static void AssertAuth(AuthenticationHeaderValue? auth)
    {
        Assert.NotNull(auth);
        Assert.Equal("Basic", auth!.Scheme);
        Assert.False(string.IsNullOrWhiteSpace(auth.Parameter));
        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(auth.Parameter!));
        Assert.StartsWith("rzp_test_key:", decoded);
    }

    private static IConfiguration Config()
    {
        var values = new Dictionary<string, string?>
        {
            ["Payments:RazorpayKeyId"] = "rzp_test_key",
            ["Payments:RazorpayKeySecret"] = "unit_test_value"
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? Request { get; private set; }

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(_responder(request));
        }
    }
}
