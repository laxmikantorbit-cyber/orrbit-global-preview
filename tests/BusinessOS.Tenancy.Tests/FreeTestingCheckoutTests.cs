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
}
