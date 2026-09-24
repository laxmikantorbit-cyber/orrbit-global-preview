using BusinessOS.Api.Crm;
using BusinessOS.Crm;

namespace BusinessOS.Api.Tests;

public sealed class CrmEstimateRequestStoreTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task InMemory_Store_Persists_And_Scopes_By_Tenant()
    {
        var store = new InMemoryCrmEstimateRequestStore();
        var request = new CrmEstimateRequest(
            Guid.NewGuid(), TenantA, "Website", "Need implementation quote",
            "Ajay", "9999999999", "ajay@example.com", 25000m,
            businessCompany: "Ajay Repairs", notes: "Needs branch-wise pricing");

        await store.AddAsync(request);
        request.StartReview();
        await store.SaveAsync(request);

        var saved = Assert.Single(await store.ListAsync(TenantA));
        Assert.Equal(request.Id, saved.Id);
        Assert.Equal(CrmEstimateRequestStatus.Reviewing, saved.Status);
        Assert.Equal("Ajay Repairs", saved.BusinessCompany);
        Assert.Equal("Needs branch-wise pricing", saved.Notes);
        Assert.Empty(await store.ListAsync(TenantB));
    }

    [Fact]
    public async Task InMemory_Store_Rejects_Duplicate_Add_And_Missing_Save()
    {
        var store = new InMemoryCrmEstimateRequestStore();
        var request = new CrmEstimateRequest(Guid.NewGuid(), TenantA, "Call", "Need estimate");

        await store.AddAsync(request);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(request));
        var missing = new CrmEstimateRequest(Guid.NewGuid(), TenantA, "Call", "Missing");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(missing));
    }
}
