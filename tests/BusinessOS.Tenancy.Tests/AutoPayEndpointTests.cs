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

namespace BusinessOS.Tenancy.Tests;

public sealed class AutoPayEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AutoPayEndpointTests(WebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task FreeTesting_AutoPay_Setup_Is_Idempotent()
    {
        var client = CreateTenantAClient();
        var subscriptionId = await ActivateSubscriptionAsync(client);

        var firstResponse = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<AutoPaySetupResponse>();
        Assert.NotNull(first);
        Assert.StartsWith("sub_free_test_", first!.ProviderSubscriptionId);
        Assert.Equal("plan_free_test_annual", first.ProviderPlanId);
        Assert.Equal("rzp_test_free_testing", first.ProviderPublicKeyId);
        Assert.Equal(12, first.TotalCount);
        Assert.False(first.ExistingBinding);

        var secondResponse = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = await secondResponse.Content.ReadFromJsonAsync<AutoPaySetupResponse>();
        Assert.NotNull(second);
        Assert.True(second!.ExistingBinding);
        Assert.Equal(first.ProviderSubscriptionId, second.ProviderSubscriptionId);
        Assert.Equal(first.StartAtUnix, second.StartAtUnix);

        var statusResponse = await client.GetAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await statusResponse.Content
            .ReadFromJsonAsync<ProviderSubscriptionBinding>();
        Assert.NotNull(status);
        Assert.Equal(first.ProviderSubscriptionId, status!.ProviderSubscriptionId);
        Assert.True(status.AutoRenewEnabled);
    }

    [Fact]
    public async Task AutoPay_Setup_Is_Tenant_Scoped()
    {
        var tenantA = CreateTenantAClient();
        var subscriptionId = await ActivateSubscriptionAsync(tenantA);
        var tenantB = CreateTenantBClient();
        var response = await tenantB.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var statusResponse = await tenantB.GetAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay");
        Assert.Equal(HttpStatusCode.NotFound, statusResponse.StatusCode);
    }

    [Fact]
    public async Task Signed_Subscription_Webhook_Updates_Provider_State()
    {
        const string webhookSecret = "autopay-test-webhook-secret";
        var client = CreateTenantAClient();
        var subscriptionId = await ActivateSubscriptionAsync(client);
        var setupResponse = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        var setup = await setupResponse.Content.ReadFromJsonAsync<AutoPaySetupResponse>();
        Assert.NotNull(setup);

        var rawBody = JsonSerializer.Serialize(new
        {
            @event = "subscription.halted",
            payload = new
            {
                subscription = new
                {
                    entity = new
                    {
                        id = setup!.ProviderSubscriptionId,
                        status = "halted"
                    }
                }
            }
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/payments/webhooks/razorpay");
        request.Content = new StringContent(rawBody, System.Text.Encoding.UTF8, "application/json");
        request.Headers.Add("X-Razorpay-Signature",
            WebhookSignatureVerifier.Compute(rawBody, webhookSecret));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content
            .ReadFromJsonAsync<RazorpaySubscriptionWebhookResponse>();
        Assert.NotNull(result);
        Assert.Equal("halted", result!.ProviderStatus);
        Assert.False(result.AutoRenewEnabled);
        Assert.Equal(subscriptionId, result.SubscriptionId);

        var statusResponse = await client.GetAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var status = await statusResponse.Content
            .ReadFromJsonAsync<ProviderSubscriptionBinding>();
        Assert.NotNull(status);
        Assert.Equal("halted", status!.Status);
        Assert.False(status.AutoRenewEnabled);
    }

    [Fact]
    public async Task Subscription_Charged_Renews_Exactly_Once()
    {
        var client = CreateTenantAClient();
        var subscriptionId = await ActivateSubscriptionAsync(client);
        var setupResponse = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        var setup = await setupResponse.Content.ReadFromJsonAsync<AutoPaySetupResponse>();
        Assert.NotNull(setup);

        var before = await client.GetFromJsonAsync<EntitlementStatusResponse>(
            $"/api/commerce/subscriptions/{subscriptionId}/entitlement");
        Assert.NotNull(before);
        var body = ChargedWebhookBody(
            setup!.ProviderSubscriptionId,
            "evt_autopay_renew_once",
            "pay_autopay_renew_once",
            "order_autopay_renew_once",
            2999900,
            "INR");

        var firstResponse = await SendSignedWebhookAsync(client, body);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await firstResponse.Content
            .ReadFromJsonAsync<RazorpaySubscriptionWebhookResponse>();
        Assert.NotNull(first?.RenewalActivation);
        Assert.Equal("subscription_charge_renewed", first!.Outcome);
        Assert.False(first.DuplicatePaymentEvent);
        Assert.True(first.RenewalActivation!.NewValidUntil > before!.ValidUntil);

        var secondResponse = await SendSignedWebhookAsync(client, body);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = await secondResponse.Content
            .ReadFromJsonAsync<RazorpaySubscriptionWebhookResponse>();
        Assert.NotNull(second?.RenewalActivation);
        Assert.Equal("subscription_charge_already_renewed", second!.Outcome);
        Assert.True(second.DuplicatePaymentEvent);
        Assert.Equal(first.RenewalActivation.NewValidUntil,
            second.RenewalActivation!.NewValidUntil);

        var after = await client.GetFromJsonAsync<EntitlementStatusResponse>(
            $"/api/commerce/subscriptions/{subscriptionId}/entitlement");
        Assert.NotNull(after);
        Assert.Equal(first.RenewalActivation.NewValidUntil, after!.ValidUntil);
    }

    [Fact]
    public async Task Recurring_Charge_Amount_Mismatch_Does_Not_Poison_Retry()
    {
        var client = CreateTenantAClient();
        var subscriptionId = await ActivateSubscriptionAsync(client);
        var setupResponse = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        var setup = await setupResponse.Content.ReadFromJsonAsync<AutoPaySetupResponse>();
        Assert.NotNull(setup);
        var before = await client.GetFromJsonAsync<EntitlementStatusResponse>(
            $"/api/commerce/subscriptions/{subscriptionId}/entitlement");
        Assert.NotNull(before);

        const string eventId = "evt_autopay_amount_guard";
        const string paymentId = "pay_autopay_amount_guard";
        const string orderId = "order_autopay_amount_guard";
        var wrong = ChargedWebhookBody(
            setup!.ProviderSubscriptionId, eventId, paymentId, orderId, 12345, "INR");
        var rejected = await SendSignedWebhookAsync(client, wrong);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var unchanged = await client.GetFromJsonAsync<EntitlementStatusResponse>(
            $"/api/commerce/subscriptions/{subscriptionId}/entitlement");
        Assert.Equal(before!.ValidUntil, unchanged!.ValidUntil);

        var corrected = ChargedWebhookBody(
            setup.ProviderSubscriptionId, eventId, paymentId, orderId, 2999900, "INR");
        var accepted = await SendSignedWebhookAsync(client, corrected);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var result = await accepted.Content
            .ReadFromJsonAsync<RazorpaySubscriptionWebhookResponse>();
        Assert.NotNull(result?.RenewalActivation);
        Assert.False(result!.DuplicatePaymentEvent);
        Assert.Equal("subscription_charge_renewed", result.Outcome);
    }
    [Fact]
    public async Task FreeTesting_AutoPay_Charge_Simulator_Renews_Entitlement_Idempotently()
    {
        var client = CreateTenantAClient();
        var subscriptionId = await ActivateSubscriptionAsync(client);
        var setup = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        var before = await client.GetFromJsonAsync<EntitlementStatusResponse>(
            $"/api/commerce/subscriptions/{subscriptionId}/entitlement");
        Assert.NotNull(before);

        const string providerOrderId = "order_free_test_autopay_simulator";
        var chargeRequest = new FreeTestingAutoPayChargeRequest(
            providerOrderId,
            "pay_free_test_autopay_simulator",
            null);
        var firstResponse = await client.PostAsJsonAsync(
            $"/api/testing/payments/razorpay/subscriptions/{subscriptionId}/charge",
            chargeRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await firstResponse.Content
            .ReadFromJsonAsync<FreeTestingCaptureResponse>();
        Assert.NotNull(first?.RenewalActivation);
        Assert.Equal("provider_payment_captured", first!.PaymentOutcome);
        Assert.Equal("activated", first.ActivationOutcome);
        Assert.False(first.DuplicatePaymentEvent);
        Assert.True(first.RenewalActivation!.NewValidUntil > before!.ValidUntil);

        var secondResponse = await client.PostAsJsonAsync(
            $"/api/testing/payments/razorpay/subscriptions/{subscriptionId}/charge",
            chargeRequest);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = await secondResponse.Content
            .ReadFromJsonAsync<FreeTestingCaptureResponse>();
        Assert.NotNull(second?.RenewalActivation);
        Assert.Equal("already_activated", second!.PaymentOutcome);
        Assert.Equal("activated", second.ActivationOutcome);
        Assert.True(second.DuplicatePaymentEvent);
        Assert.Equal(first.RenewalActivation.NewValidUntil,
            second.RenewalActivation!.NewValidUntil);

        var after = await client.GetFromJsonAsync<EntitlementStatusResponse>(
            $"/api/commerce/subscriptions/{subscriptionId}/entitlement");
        Assert.NotNull(after);
        Assert.Equal(first.RenewalActivation.NewValidUntil, after!.ValidUntil);
    }
    [Fact]
    public async Task AutoPay_Authorization_Verifies_Signature_And_Marks_Authenticated()
    {
        var client = CreateTenantAClient();
        var subscriptionId = await ActivateSubscriptionAsync(client);
        var setupResponse = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        var setup = await setupResponse.Content.ReadFromJsonAsync<AutoPaySetupResponse>();
        Assert.NotNull(setup);

        const string paymentId = "pay_autopay_authorize";
        var signature = WebhookSignatureVerifier.Compute(
            RazorpaySubscriptionAuthorizationVerifier.Payload(paymentId, setup!.ProviderSubscriptionId),
            "autopay-test-key-secret");
        var response = await client.PostAsJsonAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/authorize",
            new AutoPayAuthorizationRequest(paymentId, setup.ProviderSubscriptionId, signature));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AutoPayAuthorizationResponse>();
        Assert.NotNull(result);
        Assert.Equal("authenticated", result!.Status);
        Assert.True(result.AutoRenewEnabled);
    }

    [Fact]
    public async Task AutoPay_Authorization_Rejects_Invalid_Signature_Without_State_Change()
    {
        var client = CreateTenantAClient();
        var subscriptionId = await ActivateSubscriptionAsync(client);
        var setupResponse = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        var setup = await setupResponse.Content.ReadFromJsonAsync<AutoPaySetupResponse>();
        Assert.NotNull(setup);

        var response = await client.PostAsJsonAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/authorize",
            new AutoPayAuthorizationRequest(
                "pay_autopay_invalid", setup!.ProviderSubscriptionId, "bad-signature"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var status = await client.GetFromJsonAsync<ProviderSubscriptionBinding>(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay");
        Assert.NotNull(status);
        Assert.Equal("created", status!.Status);
    }

    [Fact]
    public async Task AutoPay_Authorization_Rejects_Provider_Subscription_Mismatch()
    {
        var client = CreateTenantAClient();
        var subscriptionId = await ActivateSubscriptionAsync(client);
        var setupResponse = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        var setup = await setupResponse.Content.ReadFromJsonAsync<AutoPaySetupResponse>();
        Assert.NotNull(setup);

        var response = await client.PostAsJsonAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/authorize",
            new AutoPayAuthorizationRequest(
                "pay_autopay_mismatch", "sub_wrong_provider_id", "ignored"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AutoPay_Authorization_Fails_Closed_When_Key_Secret_Is_Missing()
    {
        var client = FreeTestingFactoryWithoutKeySecret().CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "tenant-a-staging-token");
        var subscriptionId = await ActivateSubscriptionAsync(client);
        var setupResponse = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        var setup = await setupResponse.Content.ReadFromJsonAsync<AutoPaySetupResponse>();
        Assert.NotNull(setup);

        var response = await client.PostAsJsonAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/authorize",
            new AutoPayAuthorizationRequest("pay_missing_secret", setup!.ProviderSubscriptionId, "signature"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task AutoPay_Authorization_Does_Not_Downgrade_Active_Provider_State()
    {
        var client = CreateTenantAClient();
        var subscriptionId = await ActivateSubscriptionAsync(client);
        var setupResponse = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        var setup = await setupResponse.Content.ReadFromJsonAsync<AutoPaySetupResponse>();
        Assert.NotNull(setup);

        var activeBody = JsonSerializer.Serialize(new {
            @event = "subscription.activated",
            payload = new { subscription = new { entity = new { id = setup!.ProviderSubscriptionId, status = "active" } } }
        });
        var webhook = await SendSignedWebhookAsync(client, activeBody);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);

        const string paymentId = "pay_late_authorization";
        var signature = WebhookSignatureVerifier.Compute(
            RazorpaySubscriptionAuthorizationVerifier.Payload(paymentId, setup.ProviderSubscriptionId),
            "autopay-test-key-secret");
        var response = await client.PostAsJsonAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/authorize",
            new AutoPayAuthorizationRequest(paymentId, setup.ProviderSubscriptionId, signature));
        var result = await response.Content.ReadFromJsonAsync<AutoPayAuthorizationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("active", result!.Status);
    }

    [Fact]
    public async Task FreeTesting_AutoPay_Charge_Is_Blocked_After_Cancellation()
    {
        var client = CreateTenantAClient();
        var subscriptionId = await ActivateSubscriptionAsync(client);
        var setup = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        var cancel = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/cancel-at-period-end", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var response = await client.PostAsJsonAsync(
            $"/api/testing/payments/razorpay/subscriptions/{subscriptionId}/charge",
            new FreeTestingAutoPayChargeRequest(null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AutoPay_Setup_Is_Rejected_After_Period_End_Cancellation()
    {
        var client = CreateTenantAClient();
        var subscriptionId = await ActivateSubscriptionAsync(client);
        var setup = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        var cancel = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/cancel-at-period-end", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var entitlement = await cancel.Content.ReadFromJsonAsync<EntitlementStatusResponse>();
        Assert.NotNull(entitlement);
        Assert.Equal("Active", entitlement!.Status);
        Assert.False(entitlement.AutoRenewEnabled);
        Assert.True(entitlement.CancelAtPeriodEnd);

        var response = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/autopay/setup", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendSignedWebhookAsync(
        HttpClient client,
        string rawBody)
    {
        const string webhookSecret = "autopay-test-webhook-secret";
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/payments/webhooks/razorpay");
        request.Content = new StringContent(
            rawBody, System.Text.Encoding.UTF8, "application/json");
        request.Headers.Add("X-Razorpay-Signature",
            WebhookSignatureVerifier.Compute(rawBody, webhookSecret));
        return await client.SendAsync(request);
    }

    private static string ChargedWebhookBody(
        string providerSubscriptionId,
        string eventId,
        string paymentId,
        string providerOrderId,
        long amountPaise,
        string currency) =>
        JsonSerializer.Serialize(new
        {
            id = eventId,
            @event = "subscription.charged",
            payload = new
            {
                subscription = new
                {
                    entity = new
                    {
                        id = providerSubscriptionId,
                        status = "active"
                    }
                },
                payment = new
                {
                    entity = new
                    {
                        id = paymentId,
                        order_id = providerOrderId,
                        amount = amountPaise,
                        currency,
                        status = "captured",
                        captured = true,
                        created_at = 1789413600
                    }
                }
            }
        });
    private HttpClient CreateTenantAClient()
    {
        var client = FreeTestingFactory().CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "tenant-a-staging-token");
        return client;
    }
    private HttpClient CreateTenantBClient()
    {
        var client = FreeTestingFactory().CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "tenant-b-staging-token");
        return client;
    }

    private WebApplicationFactory<Program> FreeTestingFactory() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BusinessOS:DeploymentMode"] = "FreeTesting",
                    ["BusinessOS:StorageMode"] = "InMemory",
                    ["BusinessOS:Payments:Mode"] = "RazorpayTestPending",
                    ["Payments:RazorpayWebhookSecret"] = "autopay-test-webhook-secret",
                    ["Payments:RazorpayKeySecret"] = "autopay-test-key-secret",
                    ["BusinessOS:Auth:BearerTokens:0:Token"] = "tenant-a-staging-token",
                    ["BusinessOS:Auth:BearerTokens:0:Subject"] = "poc-user-a",
                    ["BusinessOS:Auth:BearerTokens:0:TenantCode"] = "TENANT-A",
                    ["BusinessOS:Auth:BearerTokens:1:Token"] = "tenant-b-staging-token",
                    ["BusinessOS:Auth:BearerTokens:1:Subject"] = "poc-user-b",
                    ["BusinessOS:Auth:BearerTokens:1:TenantCode"] = "TENANT-B"
                }));
        });
    private WebApplicationFactory<Program> FreeTestingFactoryWithoutKeySecret() =>
        FreeTestingFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Payments:RazorpayKeySecret"] = ""
                })));

    private static async Task<Guid> ActivateSubscriptionAsync(HttpClient client)
    {
        var checkoutResponse = await client.PostAsJsonAsync(
            "/api/commerce/checkout/initial", InitialCheckoutRequest());
        Assert.Equal(HttpStatusCode.OK, checkoutResponse.StatusCode);
        var checkout = await checkoutResponse.Content
            .ReadFromJsonAsync<RazorpayCheckoutOrderResponse>();
        Assert.NotNull(checkout);

        var captureResponse = await client.PostAsJsonAsync(
            $"/api/testing/payments/razorpay/orders/{checkout!.RazorpayOrderId}/capture",
            new FreeTestingCaptureRequest(null, null));
        Assert.Equal(HttpStatusCode.OK, captureResponse.StatusCode);
        var capture = await captureResponse.Content
            .ReadFromJsonAsync<FreeTestingCaptureResponse>();
        Assert.NotNull(capture?.InitialActivation);
        return capture!.InitialActivation!.SubscriptionId;
    }

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
}
