using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessOS.Api.Customers;
using BusinessOS.Api.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BusinessOS.Tenancy.Tests;

public sealed class TenantIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TenantIsolationTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = string.Empty
                })));

    [Fact]
    public async Task TenantA_List_Returns_Only_TenantA_Data()
    {
        var client = CreateClient("tenant-a-poc-key");
        var customers = await client.GetFromJsonAsync<Customer[]>("/api/customers");

        Assert.NotNull(customers);
        Assert.Single(customers!);
        Assert.Equal(PocIdentitySeed.TenantAId, customers![0].TenantId);
    }

    [Fact]
    public async Task TenantA_Cannot_Read_TenantB_Customer_By_Id()
    {
        var client = CreateClient("tenant-a-poc-key");
        var response = await client.GetAsync($"/api/customers/{CustomerStore.TenantBCustomerId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Forged_Tenant_Header_Cannot_Override_Credential_Tenant()
    {
        var client = CreateClient("tenant-a-poc-key");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "TENANT-B");

        var response = await client.GetAsync($"/api/customers/{CustomerStore.TenantBCustomerId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_User_Cannot_Select_Unauthorized_Tenant()
    {
        var client = CreateClient("tenant-a-poc-key");
        client.DefaultRequestHeaders.Add("X-Tenant-Code", "TENANT-B");

        var response = await client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_Credential_Is_Rejected()
    {
        var client = CreateClient("invalid-key");
        var response = await client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Poc_Api_Key_Is_Rejected_In_Production()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-POC-Api-Key", "tenant-a-poc-key");

        var response = await client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Configured_Bearer_Token_Works_In_Production()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BusinessOS:Auth:BearerTokens:0:Token"] = "tenant-a-prod-token",
                    ["BusinessOS:Auth:BearerTokens:0:Subject"] = "poc-user-a",
                    ["BusinessOS:Auth:BearerTokens:0:TenantCode"] = "TENANT-A"
                }));
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "tenant-a-prod-token");
        var customers = await client.GetFromJsonAsync<Customer[]>("/api/customers");

        Assert.NotNull(customers);
        Assert.Single(customers!);
        Assert.Equal(PocIdentitySeed.TenantAId, customers![0].TenantId);
    }

    [Fact]
    public async Task Commerce_Admin_Status_Allows_Owner_Role()
    {
        var client = CreateClient("tenant-a-poc-key");
        var response = await client.GetAsync("/api/commerce/admin/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Commerce_Admin_Status_Rejects_Non_Admin_Role()
    {
        var client = CreateClient("tenant-a-staff-poc-key");
        var response = await client.GetAsync("/api/commerce/admin/status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient(string apiKey)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-POC-Api-Key", apiKey);
        return client;
    }
}
