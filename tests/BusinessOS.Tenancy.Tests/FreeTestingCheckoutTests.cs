using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessOS.Api.Commerce;
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
