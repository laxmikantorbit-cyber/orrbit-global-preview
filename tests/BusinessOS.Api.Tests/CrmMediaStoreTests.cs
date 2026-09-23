using BusinessOS.Api.Crm;
using BusinessOS.Crm;

namespace BusinessOS.Api.Tests;

public sealed class CrmMediaStoreTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid UserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task InMemory_Media_Store_Persists_Metadata_And_Tenant_Scope()
    {
        var store = new InMemoryCrmMediaStore();
        var entityId = Guid.NewGuid();
        var asset = new CrmMediaAsset(
            Guid.NewGuid(), TenantA, "invoice.pdf", "application/pdf", 2048,
            "Invoice attachment", "crm/invoices/invoice.pdf", UserId, "Invoice", entityId);

        await store.AddAsync(asset);

        var saved = Assert.Single(await store.ListAsync(TenantA));
        Assert.Equal("invoice.pdf", saved.FileName);
        Assert.Equal("application/pdf", saved.MimeType);
        Assert.Equal("crm/invoices/invoice.pdf", saved.StorageReference);
        Assert.Equal(entityId, saved.EntityId);
        Assert.Empty(await store.ListAsync(TenantB));
    }

    [Fact]
    public async Task InMemory_Media_Store_Updates_Active_State_And_Rejects_Duplicate()
    {
        var store = new InMemoryCrmMediaStore();
        var asset = new CrmMediaAsset(
            Guid.NewGuid(), TenantA, "guide.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            4096, "Knowledge attachment", "crm/knowledge/guide.docx", UserId);

        await store.AddAsync(asset);
        asset.SetActive(false);
        await store.SaveAsync(asset);

        var saved = Assert.Single(await store.ListAsync(TenantA));
        Assert.False(saved.Active);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(asset));
    }
}
