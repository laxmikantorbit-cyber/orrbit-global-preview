using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessOS.Api.Commerce;
using BusinessOS.Api.Payments;
using BusinessOS.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BusinessOS.Tenancy.Tests;

public sealed class PaymentCrossPathConcurrencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string WebhookSecret = "cross-path-webhook-secret";
    private const string KeySecret = "cross-path-key-secret";
    private readonly WebApplicationFactory<Program> _factory;

    public PaymentCrossPathConcurrencyTests(WebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task Webhook_And_Checkout_Reconcile_Converge_On_One_Activation()
    {
        var fakePaymentClient = new MutablePaymentClient();
        var client = CreateFactory(fakePaymentClient).CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "tenant-a-cross-token");

        var checkoutResponse = await client.PostAsJsonAsync(
            "/api/commerce/checkout/initial", InitialCheckoutRequest());
        var checkout = await checkoutResponse.Content
            .ReadFromJsonAsync<RazorpayCheckoutOrderResponse>();
        Assert.Equal(HttpStatusCode.OK, checkoutResponse.StatusCode);
        Assert.NotNull(checkout);

        const string paymentId = "pay_cross_path_concurrent";
        fakePaymentClient.Result = new RazorpayPaymentResult(
            paymentId,
            checkout!.RazorpayOrderId,
            checkout.RazorpayAmount,
            checkout.CurrencyCode,
            "captured",
            true,
            DateTimeOffset.FromUnixTimeSeconds(1789413600));

        var webhookBody = CapturedWebhookBody(checkout, paymentId);
        var checkoutSignature = WebhookSignatureVerifier.Compute(
            RazorpayCheckoutSignatureVerifier.Payload(
                checkout.RazorpayOrderId, paymentId),
            KeySecret);
        var reconcileRequest = new RazorpayCheckoutVerificationRequest(
            checkout.RazorpayOrderId,
            paymentId,
            checkoutSignature);

        var webhookTask = SendSignedWebhookAsync(client, webhookBody);
        var reconcileTask = client.PostAsJsonAsync(
            "/api/payments/checkout/razorpay/reconcile",
            reconcileRequest);
        await Task.WhenAll(webhookTask, reconcileTask);

        var webhookResponse = await webhookTask;
        var reconcileResponse = await reconcileTask;
        Assert.Equal(HttpStatusCode.OK, webhookResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reconcileResponse.StatusCode);

        var webhookResult = await webhookResponse.Content
            .ReadFromJsonAsync<PaymentWebhookResponse>();
        var reconcileResult = await reconcileResponse.Content
            .ReadFromJsonAsync<RazorpayCheckoutReconciliationResponse>();
        Assert.NotNull(webhookResult?.InitialActivation);
        Assert.NotNull(reconcileResult?.InitialActivation);
        Assert.Equal(
            webhookResult!.InitialActivation!.SubscriptionId,
            reconcileResult!.InitialActivation!.SubscriptionId);
        Assert.Equal(1, new[] {
            webhookResult.DuplicatePaymentEvent,
            reconcileResult.DuplicatePaymentEvent }.Count(x => x));
    }

    [Fact]
    public async Task Webhook_And_Admin_Reconcile_Converge_On_One_Activation()
    {
        var fakePaymentClient = new MutablePaymentClient();
        var client = CreateFactory(fakePaymentClient).CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "tenant-a-cross-token");

        var checkoutResponse = await client.PostAsJsonAsync(
            "/api/commerce/checkout/initial", InitialCheckoutRequest());
        var checkout = await checkoutResponse.Content
            .ReadFromJsonAsync<RazorpayCheckoutOrderResponse>();
        Assert.Equal(HttpStatusCode.OK, checkoutResponse.StatusCode);
        Assert.NotNull(checkout);

        const string paymentId = "pay_cross_path_admin_concurrent";
        fakePaymentClient.Result = new RazorpayPaymentResult(
            paymentId,
            checkout!.RazorpayOrderId,
            checkout.RazorpayAmount,
            checkout.CurrencyCode,
            "captured",
            true,
            DateTimeOffset.FromUnixTimeSeconds(1789413600));

        var webhookTask = SendSignedWebhookAsync(
            client, CapturedWebhookBody(checkout, paymentId));
        var adminTask = client.PostAsync(
            $"/api/commerce/admin/razorpay/orders/{checkout.RazorpayOrderId}/reconcile",
            content: null);
        await Task.WhenAll(webhookTask, adminTask);

        var webhookResponse = await webhookTask;
        var adminResponse = await adminTask;
        Assert.Equal(HttpStatusCode.OK, webhookResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);

        var webhookResult = await webhookResponse.Content
            .ReadFromJsonAsync<PaymentWebhookResponse>();
        var adminResult = await adminResponse.Content
            .ReadFromJsonAsync<CommerceAdminManualReconciliationResponse>();
        Assert.NotNull(webhookResult?.InitialActivation);
        Assert.NotNull(adminResult?.InitialActivation);
        Assert.Equal(
            webhookResult!.InitialActivation!.SubscriptionId,
            adminResult!.InitialActivation!.SubscriptionId);
        Assert.Equal(1, new[] {
            webhookResult.DuplicatePaymentEvent,
            adminResult.DuplicatePaymentEvent }.Count(x => x));
    }

    private WebApplicationFactory<Program> CreateFactory(
        MutablePaymentClient fakePaymentClient) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BusinessOS:DeploymentMode"] = "FreeTesting",
                    ["BusinessOS:StorageMode"] = "InMemory",
                    ["BusinessOS:Payments:Mode"] = "RazorpayTestPending",
                    ["Payments:RazorpayWebhookSecret"] = WebhookSecret,
                    ["Payments:RazorpayKeySecret"] = KeySecret,
                    ["BusinessOS:Auth:BearerTokens:0:Token"] = "tenant-a-cross-token",
                    ["BusinessOS:Auth:BearerTokens:0:Subject"] = "poc-user-a",
                    ["BusinessOS:Auth:BearerTokens:0:TenantCode"] = "TENANT-A"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRazorpayPaymentClient>();
                services.AddSingleton<IRazorpayPaymentClient>(fakePaymentClient);
            });
        });

    private static async Task<HttpResponseMessage> SendSignedWebhookAsync(
        HttpClient client,
        string rawBody)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/payments/webhooks/razorpay");
        request.Content = new StringContent(
            rawBody, System.Text.Encoding.UTF8, "application/json");
        request.Headers.Add(
            "X-Razorpay-Signature",
            WebhookSignatureVerifier.Compute(rawBody, WebhookSecret));
        return await client.SendAsync(request);
    }

    private static string CapturedWebhookBody(
        RazorpayCheckoutOrderResponse checkout,
        string paymentId) =>
        JsonSerializer.Serialize(new
        {
            id = "evt_cross_path_concurrent",
            @event = "payment.captured",
            payload = new
            {
                payment = new
                {
                    entity = new
                    {
                        id = paymentId,
                        order_id = checkout.RazorpayOrderId,
                        amount = checkout.RazorpayAmount,
                        currency = checkout.CurrencyCode,
                        status = "captured",
                        captured = true,
                        created_at = 1789413600
                    }
                }
            }
        });

    private static CreateInitialCheckoutOrderRequest InitialCheckoutRequest() =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "AI_REPAIR",
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            29999m,
            "INR",
            12,
            1,
            1,
            10,
            10,
            true);

    private sealed class MutablePaymentClient : IRazorpayPaymentClient
    {
        public RazorpayPaymentResult? Result { get; set; }

        public Task<RazorpayPaymentResult> FetchPaymentAsync(
            string paymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result ?? throw new InvalidOperationException(
                "Fake payment result is not configured."));

        public Task<IReadOnlyList<RazorpayPaymentResult>> FetchOrderPaymentsAsync(
            string orderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RazorpayPaymentResult>>(
                Result is null ? [] : [Result]);
    }
}
