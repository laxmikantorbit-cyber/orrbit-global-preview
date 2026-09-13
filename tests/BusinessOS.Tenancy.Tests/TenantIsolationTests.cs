using System.Net;
using System.Net.Http.Json;
using BusinessOS.Api.Customers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessOS.Tenancy.Tests;

public sealed class TenantIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TenantIsolationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task TenantA_List_Returns_Only_TenantA_Data()
    {
        var client = CreateClient("tenant-a-poc-key");
        var customers = await client.GetFromJsonAsync<Customer[]>("/api/customers");

        Assert.NotNull(customers);
        Assert.Single(customers!);
        Assert.Equal("TENANT-A", customers![0].TenantId);
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

    private HttpClient CreateClient(string apiKey)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-POC-Api-Key", apiKey);
        return client;
    }
}
