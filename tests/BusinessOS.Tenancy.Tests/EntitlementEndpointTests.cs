using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessOS.Api.Commerce;
using BusinessOS.Api.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BusinessOS.Tenancy.Tests;

public sealed class EntitlementEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EntitlementEndpointTests(WebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task Entitlement_Status_And_Cancel_At_Period_End_Work_End_To_End()
    {
        var client = FreeTestingFactory().CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "tenant-a-staging-token");

        var checkoutResponse = await client.PostAsJsonAsync(
            "/api/commerce/checkout/initial", InitialCheckoutRequest());
        Assert.Equal(HttpStatusCode.OK, checkoutResponse.StatusCode);
        var checkout = await checkoutResponse.Content
            .ReadFromJsonAsync<RazorpayCheckoutOrderResponse>();
        Assert.NotNull(checkout);

        var captureResponse = await client.PostAsJsonAsync(
            $"/api/testing/payments/razorpay/orders/{checkout!.RazorpayOrderId}/capture",
            new FreeTestingCaptureRequest("pay_entitlement_status", null));
        Assert.Equal(HttpStatusCode.OK, captureResponse.StatusCode);
        var capture = await captureResponse.Content
            .ReadFromJsonAsync<FreeTestingCaptureResponse>();
        Assert.NotNull(capture?.InitialActivation);
        var subscriptionId = capture!.InitialActivation!.SubscriptionId;

        var active = await client.GetFromJsonAsync<EntitlementStatusResponse>(
            $"/api/commerce/subscriptions/{subscriptionId}/entitlement");
        Assert.NotNull(active);
        Assert.Equal("Active", active!.Status);
        Assert.Equal("current_term_active", active.RenewalStatus);
        Assert.True(active.AutoRenewEnabled);
        Assert.False(active.CancelAtPeriodEnd);

        var cancelResponse = await client.PostAsync(
            $"/api/commerce/subscriptions/{subscriptionId}/cancel-at-period-end",
            content: null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await cancelResponse.Content
            .ReadFromJsonAsync<EntitlementStatusResponse>();
        Assert.NotNull(cancelled);
        Assert.Equal("Active", cancelled!.Status);
        Assert.Equal("cancelled_at_period_end", cancelled.RenewalStatus);
        Assert.False(cancelled.AutoRenewEnabled);
        Assert.True(cancelled.CancelAtPeriodEnd);

        var readBack = await client.GetFromJsonAsync<EntitlementStatusResponse>(
            $"/api/commerce/subscriptions/{subscriptionId}/entitlement");
        Assert.NotNull(readBack);
        Assert.True(readBack!.CancelAtPeriodEnd);
        Assert.False(readBack.AutoRenewEnabled);
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
