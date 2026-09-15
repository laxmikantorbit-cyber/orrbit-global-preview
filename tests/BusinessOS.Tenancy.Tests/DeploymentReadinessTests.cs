using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BusinessOS.Tenancy.Tests;

public sealed class DeploymentReadinessTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DeploymentReadinessTests(WebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task Production_Readiness_Returns_Unavailable_When_Required_Config_Is_Missing()
    {
        var client = _factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"))
            .CreateClient();

        var response = await client.GetAsync("/health/ready");
        var report = await response.Content.ReadFromJsonAsync<ReadinessDto>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(report);
        Assert.False(report!.Ready);        Assert.Contains("ConnectionStrings:Commerce", report.MissingConfiguration);
        Assert.Contains("ConnectionStrings:Identity", report.MissingConfiguration);
        Assert.Contains("Payments:RazorpayKeySecret", report.MissingConfiguration);
        Assert.Contains("BusinessOS:Auth:BearerTokens", report.MissingConfiguration);
    }

    [Fact]
    public async Task Staging_FreeTesting_Readiness_Returns_Ok_Without_Paid_Db_Or_Live_Razorpay()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BusinessOS:DeploymentMode"] = "FreeTesting",
                    ["BusinessOS:StorageMode"] = "InMemory",
                    ["BusinessOS:Payments:Mode"] = "RazorpayTestPending",
                    ["BusinessOS:Auth:AllowPocApiKeys"] = "false",
                    ["BusinessOS:Auth:BearerTokens:0:Token"] = "staging-token",
                    ["BusinessOS:Auth:BearerTokens:0:Subject"] = "poc-user-a",
                    ["BusinessOS:Auth:BearerTokens:0:TenantCode"] = "TENANT-A"
                }));
        }).CreateClient();

        var response = await client.GetAsync("/health/ready");        var report = await response.Content.ReadFromJsonAsync<ReadinessDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(report);
        Assert.True(report!.Ready);
        Assert.Equal("Staging", report.Environment);
        Assert.Equal("FreeTesting", report.DeploymentMode);
        Assert.Equal("InMemory", report.StorageMode);
        Assert.Equal("RazorpayTestPending", report.PaymentsMode);
        Assert.Empty(report.MissingConfiguration);
        Assert.Empty(report.UnsafeConfiguration);
        Assert.Contains("free_testing_mode", report.Checks);
        Assert.Contains("external_db_and_live_payment_secrets_deferred_until_release", report.Checks);
        Assert.Contains("bearer_token_configured", report.Checks);
    }

    [Fact]
    public async Task Production_Readiness_Fails_When_Poc_Api_Keys_Are_Enabled()
    {
        var client = ReadyFactory(new Dictionary<string, string?>
            {
                ["BusinessOS:Auth:AllowPocApiKeys"] = "true"
            })
            .CreateClient();
        var response = await client.GetAsync("/health/ready");
        var report = await response.Content.ReadFromJsonAsync<ReadinessDto>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(report);
        Assert.False(report!.Ready);
        Assert.Contains("BusinessOS:Auth:AllowPocApiKeys", report.UnsafeConfiguration);
    }

    [Fact]
    public async Task Production_Readiness_Returns_Ok_When_Required_Config_Is_Present()
    {
        var client = ReadyFactory(new Dictionary<string, string?>()).CreateClient();

        var response = await client.GetAsync("/health/ready");
        var report = await response.Content.ReadFromJsonAsync<ReadinessDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(report);
        Assert.True(report!.Ready);
        Assert.Empty(report.MissingConfiguration);
        Assert.Empty(report.UnsafeConfiguration);
        Assert.Contains("required_configuration_present_for_current_mode", report.Checks);
    }
    private WebApplicationFactory<Program> ReadyFactory(
        IReadOnlyDictionary<string, string?> overrides)
    {
        var settings = new Dictionary<string, string?>(overrides)
        {
            ["ConnectionStrings:Commerce"] = "Host=127.0.0.1;Database=commerce",
            ["ConnectionStrings:Identity"] = "Host=127.0.0.1;Database=identity",
            ["Payments:RazorpayKeyId"] = "rzp_test_ready_key",
            ["Payments:RazorpayKeySecret"] = "ready-secret",
            ["Payments:RazorpayWebhookSecret"] = "ready-webhook-secret",
            ["BusinessOS:Auth:BearerTokens:0:Token"] = "ready-token",
            ["BusinessOS:Auth:BearerTokens:0:Subject"] = "ready-subject",
            ["BusinessOS:Auth:BearerTokens:0:TenantCode"] = "TENANT-A"
        };

        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(settings));
        });
    }
    private sealed record ReadinessDto(
        bool Ready,
        string Environment,
        string DeploymentMode,
        string StorageMode,
        string PaymentsMode,
        IReadOnlyList<string> MissingConfiguration,
        IReadOnlyList<string> UnsafeConfiguration,
        IReadOnlyList<string> Checks);
}
