using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BusinessOS.Api.Commerce;
using BusinessOS.Api.Payments;
using BusinessOS.Application;
using BusinessOS.Licensing;
using Microsoft.Extensions.Configuration;

namespace BusinessOS.Api.Tests;

public sealed class RazorpayOrderClientTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Razorpay_Client_Posts_Order_With_Basic_Auth_And_Notes()
    {
        var handler = new CapturingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "id":"order_test_123",
                      "amount":10000,
                      "currency":"INR",
                      "receipt":"bos_receipt_1",
                      "status":"created",
                      "notes":{"tenantId":"tenant-a","commerceOrderId":"order-a"}
                    }
                    """, Encoding.UTF8, "application/json")
            });
        var client = new RazorpayHttpOrderClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.razorpay.com") },
            Config());

        var result = await client.CreateOrderAsync(new RazorpayOrderRequest(
            10000,
            "INR",
            "bos_receipt_1",
            new Dictionary<string, string>
            {
                ["tenantId"] = "tenant-a",
                ["commerceOrderId"] = "order-a"
            }));

        Assert.Equal("order_test_123", result.Id);
        Assert.Equal("/v1/orders", handler.Request!.RequestUri!.PathAndQuery);
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        AssertAuth(handler.Request.Headers.Authorization);
        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal(10000, body.GetProperty("amount").GetInt64());
        Assert.Equal("INR", body.GetProperty("currency").GetString());
        Assert.Equal("bos_receipt_1", body.GetProperty("receipt").GetString());
        Assert.Equal("order-a", body.GetProperty("notes").GetProperty("commerceOrderId").GetString());
    }

    [Fact]
    public async Task Checkout_Service_Returns_Razorpay_Order_Id_And_Public_Key()
    {
        using var signer = new LeaseSigner();
        var store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);
        var service = new RazorpayCheckoutService(
            store,
            new StubRazorpayOrderClient(),
            Config());

        var checkout = await service.CreateInitialAsync(
            TenantA,
            InitialCheckoutRequest());

        Assert.Equal("rzp_test_key", checkout.RazorpayKeyId);
        Assert.Equal("order_stub_1", checkout.RazorpayOrderId);
        Assert.Equal(10000, checkout.RazorpayAmount);
        Assert.Equal("created", checkout.RazorpayStatus);
        Assert.Equal(checkout.CommerceOrderId.ToString(), checkout.RazorpayNotes["commerceOrderId"]);
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

    private static CreateInitialCheckoutOrderRequest InitialCheckoutRequest() => new(
        OrgA,
        "ORRBIT-REPAIR",
        Guid.NewGuid(),
        Guid.NewGuid(),
        1,
        100m,
        "INR",
        12,
        1,
        1,
        10,
        5,
        true);

    private sealed class StubRazorpayOrderClient : IRazorpayOrderClient
    {
        public Task<RazorpayOrderResult> CreateOrderAsync(
            RazorpayOrderRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new RazorpayOrderResult(
                "order_stub_1",
                request.Amount,
                request.Currency,
                request.Receipt,
                "created",
                request.Notes));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }
}
