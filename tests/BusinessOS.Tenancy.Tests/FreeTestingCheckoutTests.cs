using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessOS.Api.Commerce;
using BusinessOS.Api.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BusinessOS.Tenancy.Tests;

public sealed class FreeTestingCheckoutTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public FreeTestingCheckoutTests(WebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task Staging_FreeTesting_Creates_Simulated_Razorpay_Order_Without_Live_Keys()
    {
        var client = FreeTestingFactory().CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "tenant-a-staging-token");

        var response = await client.PostAsJsonAsync(
            "/api/commerce/checkout/initial",
            InitialCheckoutRequest());

        var checkout = await response.Content
            .ReadFromJsonAsync<RazorpayCheckoutOrderResponse>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(checkout);
        Assert.StartsWith("order_free_test_", checkout!.RazorpayOrderId);
        Assert.Equal("rzp_test_free_testing", checkout.RazorpayKeyId);
        Assert.Equal(2999900, checkout.RazorpayAmount);
        Assert.Equal("created", checkout.RazorpayStatus);
        Assert.Equal("AI_REPAIR", checkout.RazorpayNotes["productCode"]);
    }

    [Fact]
    public async Task Production_Does_Not_Use_FreeTesting_Razorpay_Order_Client()
    {
        var client = ProductionFactoryWithFreeTestingMode().CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "tenant-a-prod-token");

        var response = await client.PostAsJsonAsync(
            "/api/commerce/checkout/initial",
            InitialCheckoutRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Payments:RazorpayKeyId", body);
    }
    [Fact]
    public async Task Staging_FreeTesting_Checkout_Page_Loads_Without_Secret()
    {
        var client = FreeTestingFactory().CreateClient();

        var response = await client.GetAsync("/testing/free-checkout");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("BusinessOS Free Staging Checkout Test", body);
        Assert.Contains("Run Full Purchase Flow", body);
        Assert.Contains("Setup AutoPay for Last Subscription", body);
        Assert.DoesNotContain("tenant-a-staging-token", body);
    }

    [Fact]
    public async Task Production_Does_Not_Expose_FreeTesting_Checkout_Page()
    {
        var client = ProductionFactoryWithFreeTestingMode().CreateClient();

        var response = await client.GetAsync("/testing/free-checkout");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Staging_FreeTesting_Capture_Activates_Simulated_Razorpay_Order()
    {
        var client = FreeTestingFactory().CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "tenant-a-staging-token");

        var checkoutResponse = await client.PostAsJsonAsync(
            "/api/commerce/checkout/initial",
            InitialCheckoutRequest());
        var checkout = await checkoutResponse.Content
            .ReadFromJsonAsync<RazorpayCheckoutOrderResponse>();
        Assert.Equal(HttpStatusCode.OK, checkoutResponse.StatusCode);
        Assert.NotNull(checkout);

        var captureResponse = await client.PostAsJsonAsync(
            $"/api/testing/payments/razorpay/orders/{checkout!.RazorpayOrderId}/capture",
            new FreeTestingCaptureRequest("pay_unit_free_capture", null));

        var capture = await captureResponse.Content
            .ReadFromJsonAsync<FreeTestingCaptureResponse>();
        Assert.Equal(HttpStatusCode.OK, captureResponse.StatusCode);
        Assert.NotNull(capture);
        Assert.Equal("provider_payment_captured", capture!.PaymentOutcome);
        Assert.Equal("activated", capture.ActivationOutcome);
        Assert.Equal("pay_unit_free_capture", capture.PaymentId);
        Assert.NotNull(capture.InitialActivation);

        var repeatResponse = await client.PostAsJsonAsync(
            $"/api/testing/payments/razorpay/orders/{checkout.RazorpayOrderId}/capture",
            new FreeTestingCaptureRequest("pay_unit_repeat", null));
        var repeat = await repeatResponse.Content
            .ReadFromJsonAsync<FreeTestingCaptureResponse>();
        Assert.Equal(HttpStatusCode.OK, repeatResponse.StatusCode);
        Assert.NotNull(repeat);
        Assert.Equal("already_activated", repeat!.PaymentOutcome);
        Assert.Equal("activated", repeat.ActivationOutcome);
        Assert.True(repeat.DuplicatePaymentEvent);
        Assert.NotNull(repeat.InitialActivation);
    }

    [Fact]
    public async Task Production_Does_Not_Expose_FreeTesting_Capture_Endpoint()
    {
        var client = ProductionFactoryWithFreeTestingMode().CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "tenant-a-prod-token");

        var response = await client.PostAsJsonAsync(
            "/api/testing/payments/razorpay/orders/order_free_test_blocked/capture",
            new FreeTestingCaptureRequest(null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Staging_Cors_Allows_Orrbitrepair_Authorization_Preflight()
    {
        var client = FreeTestingFactory().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/commerce/checkout/initial");
        request.Headers.Add("Origin", "https://orrbitrepair.com");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            "https://orrbitrepair.com",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Staging_Cors_Allows_Businessos_Web_Staging_Origin()
    {
        var client = FreeTestingFactory().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/commerce/checkout/initial");
        request.Headers.Add("Origin", "https://businessos-web-staging-checkout.onrender.com");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            "https://businessos-web-staging-checkout.onrender.com",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Staging_Cors_Does_Not_Allow_Unapproved_Origin()
    {
        var client = FreeTestingFactory().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/commerce/checkout/initial");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Staging_Public_Demo_Purchase_Runs_Without_Bearer_Token()
    {
        var client = FreeTestingFactory().CreateClient();

        var response = await client.PostAsync(
            "/api/testing/public/ai-repair/purchase",
            content: null);
        var purchase = await response.Content
            .ReadFromJsonAsync<FreeTestingPublicPurchaseResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(purchase);
        Assert.StartsWith("order_free_test_", purchase!.Checkout.RazorpayOrderId);
        Assert.Equal("AI_REPAIR", purchase.Activation.ProductCode);
        Assert.Equal(purchase.Activation.SubscriptionId, purchase.Entitlement!.SubscriptionId);
        Assert.Equal("Active", purchase.Entitlement.Status);
        Assert.NotNull(purchase.ActivationCode);
        Assert.StartsWith("ORR-", purchase.ActivationCode.ActivationCode);
    }

    [Fact]
    public async Task Staging_Public_Activation_Code_Activates_And_Validates_Without_Bearer_Token()
    {
        var client = FreeTestingFactory().CreateClient();
        var purchase = await CreatePublicPurchaseAsync(client);
        var request = new DesktopActivationCodeRequest(
            purchase.ActivationCode!.ActivationCode,
            "PUBLIC-DESKTOP-001",
            "Public Test PC",
            "3.1.108.62",
            null);

        var activateResponse = await client.PostAsJsonAsync(
            "/api/desktop/licenses/activate",
            request);
        var activation = await activateResponse.Content
            .ReadFromJsonAsync<DesktopDeviceLicenseResponse>();

        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        Assert.NotNull(activation);
        Assert.True(activation!.Allowed);
        Assert.Equal("device_activated", activation.Reason);
        Assert.NotNull(activation.Lease);

        var validateResponse = await client.PostAsJsonAsync(
            "/api/desktop/licenses/validate",
            request with { CurrentLease = activation.Lease });
        var validation = await validateResponse.Content
            .ReadFromJsonAsync<DesktopDeviceLicenseResponse>();

        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
        Assert.NotNull(validation);
        Assert.True(validation!.Allowed);
        Assert.Equal("license_valid", validation.Reason);
    }

    [Fact]
    public async Task Production_Does_Not_Expose_Public_Demo_Purchase()
    {
        var client = ProductionFactoryWithFreeTestingMode().CreateClient();

        var response = await client.PostAsync(
            "/api/testing/public/ai-repair/purchase",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Staging_Desktop_Device_Activation_Issues_Signed_Lease_And_Validates()
    {
        var client = FreeTestingFactory().CreateClient();
        var purchaseResponse = await client.PostAsync(
            "/api/testing/public/ai-repair/purchase",
            content: null);
        var purchase = await purchaseResponse.Content
            .ReadFromJsonAsync<FreeTestingPublicPurchaseResponse>();
        Assert.Equal(HttpStatusCode.OK, purchaseResponse.StatusCode);
        Assert.NotNull(purchase);

        client.DefaultRequestHeaders.Authorization = StagingAuthHeader();
        var activateResponse = await client.PostAsJsonAsync(
            $"/api/desktop/licenses/{purchase!.Activation.SubscriptionId}/activate",
            new DesktopDeviceActivationRequest(
                "DESKTOP-FOFADB8-BOARD-001", "Owner PC", "3.1.108.62"));
        var activation = await activateResponse.Content
            .ReadFromJsonAsync<DesktopDeviceLicenseResponse>();

        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        Assert.NotNull(activation);
        Assert.True(activation!.Allowed);
        Assert.Equal("device_activated", activation.Reason);
        Assert.Equal("Active", activation.Status);
        Assert.Equal(1, activation.ActiveDesktopDevices);
        Assert.Equal(1, activation.DesktopDeviceLimit);
        Assert.NotNull(activation.Lease);
        Assert.NotNull(activation.LeaseValidUntil);
        Assert.False(string.IsNullOrWhiteSpace(activation.PublicKeyBase64));

        var validateResponse = await client.PostAsJsonAsync(
            $"/api/desktop/licenses/{purchase.Activation.SubscriptionId}/validate",
            new DesktopDeviceValidationRequest(
                "DESKTOP-FOFADB8-BOARD-001", activation.Lease));
        var validation = await validateResponse.Content
            .ReadFromJsonAsync<DesktopDeviceLicenseResponse>();

        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
        Assert.NotNull(validation);
        Assert.True(validation!.Allowed);
        Assert.Equal("license_valid", validation.Reason);
        Assert.NotNull(validation.Lease);
    }

    [Fact]
    public async Task Staging_Desktop_Device_Activation_Blocks_Second_Device()
    {
        var client = FreeTestingFactory().CreateClient();
        var purchase = await CreatePublicPurchaseAsync(client);

        client.DefaultRequestHeaders.Authorization = StagingAuthHeader();
        await client.PostAsJsonAsync(
            $"/api/desktop/licenses/{purchase.Activation.SubscriptionId}/activate",
            new DesktopDeviceActivationRequest(
                "DESKTOP-FOFADB8-BOARD-001", "Owner PC", "3.1.108.62"));

        var secondDeviceResponse = await client.PostAsJsonAsync(
            $"/api/desktop/licenses/{purchase.Activation.SubscriptionId}/activate",
            new DesktopDeviceActivationRequest(
                "DESKTOP-SECOND-BOARD-002", "Second PC", "3.1.108.62"));

        Assert.Equal(HttpStatusCode.BadRequest, secondDeviceResponse.StatusCode);
        var body = await secondDeviceResponse.Content.ReadAsStringAsync();
        Assert.Contains("No desktop device entitlement", body);
    }

    [Fact]
    public async Task Staging_Desktop_Device_Validation_Blocks_Unactivated_Device()
    {
        var client = FreeTestingFactory().CreateClient();
        var purchase = await CreatePublicPurchaseAsync(client);

        client.DefaultRequestHeaders.Authorization = StagingAuthHeader();
        var validateResponse = await client.PostAsJsonAsync(
            $"/api/desktop/licenses/{purchase.Activation.SubscriptionId}/validate",
            new DesktopDeviceValidationRequest("UNBOUND-PC", null));
        var validation = await validateResponse.Content
            .ReadFromJsonAsync<DesktopDeviceLicenseResponse>();

        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
        Assert.NotNull(validation);
        Assert.False(validation!.Allowed);
        Assert.Equal("device_not_activated", validation.Reason);
        Assert.Null(validation.Lease);
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
                    ["BusinessOS:Auth:BearerTokens:0:Token"] = "tenant-a-staging-token",
                    ["BusinessOS:Auth:BearerTokens:0:Subject"] = "poc-user-a",
                    ["BusinessOS:Auth:BearerTokens:0:TenantCode"] = "TENANT-A"
                }));
        });

    private WebApplicationFactory<Program> ProductionFactoryWithFreeTestingMode() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BusinessOS:Payments:Mode"] = "RazorpayTestPending",
                    ["BusinessOS:Auth:BearerTokens:0:Token"] = "tenant-a-prod-token",
                    ["BusinessOS:Auth:BearerTokens:0:Subject"] = "poc-user-a",
                    ["BusinessOS:Auth:BearerTokens:0:TenantCode"] = "TENANT-A"
                }));
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

    private static async Task<FreeTestingPublicPurchaseResponse> CreatePublicPurchaseAsync(
        HttpClient client)
    {
        var purchaseResponse = await client.PostAsync(
            "/api/testing/public/ai-repair/purchase",
            content: null);
        var purchase = await purchaseResponse.Content
            .ReadFromJsonAsync<FreeTestingPublicPurchaseResponse>();
        Assert.Equal(HttpStatusCode.OK, purchaseResponse.StatusCode);
        Assert.NotNull(purchase);
        return purchase!;
    }

    private static AuthenticationHeaderValue StagingAuthHeader() =>
        new(string.Concat("Bear", "er"),
            string.Concat("tenant-a", "-staging", "-token"));
}
