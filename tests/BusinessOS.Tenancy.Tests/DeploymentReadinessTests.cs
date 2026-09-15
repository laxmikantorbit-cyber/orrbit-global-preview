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
        Assert.False(report!.Ready);
        Assert.Contains("ConnectionStrings:Commerce", report.MissingConfiguration);
        Assert.Contains("ConnectionStrings:Identity", report.MissingConfiguration);
        Assert.Contains("Payments:RazorpayKeySecret", report.MissingConfiguration);
        Assert.Contains("BusinessOS:Auth:BearerTokens", report.MissingConfiguration);
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
        Assert.Contains("required_external_configuration_present", report.Checks);
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
        IReadOnlyList<string> MissingConfiguration,
        IReadOnlyList<string> UnsafeConfiguration,
        IReadOnlyList<string> Checks);
}
