using BusinessOS.Api.Crm;
using BusinessOS.Sales;

namespace BusinessOS.Api.Tests;

public sealed class CrmSalesItemGroupTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task InMemory_Store_Persists_Group_And_Assigned_Item()
    {
        var store = new InMemoryCrmSalesItemStore();
        var group = new SalesItemGroup(Guid.NewGuid(), TenantId, "Services");
        await store.AddGroupAsync(group);

        var item = new SalesItem(
            Guid.NewGuid(), TenantId, "SUPPORT", "Annual Support", null,
            5000m, 18m, SalesItemStatus.Active, null, group.Id);
        await store.AddAsync(item);

        var loadedGroup = await store.GetGroupAsync(TenantId, group.Id);
        var loadedItem = await store.GetAsync(TenantId, item.Id);

        Assert.Equal("Services", loadedGroup!.Name);
        Assert.Equal(group.Id, loadedItem!.GroupId);
    }
    [Fact]
    public async Task InMemory_Store_Rejects_Duplicate_Group_Name_Case_Insensitively()
    {
        var store = new InMemoryCrmSalesItemStore();
        await store.AddGroupAsync(
            new SalesItemGroup(Guid.NewGuid(), TenantId, "Software"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AddGroupAsync(
                new SalesItemGroup(Guid.NewGuid(), TenantId, " software ")));
    }

    [Fact]
    public async Task Group_Update_Persists_Name_And_Active_State()
    {
        var store = new InMemoryCrmSalesItemStore();
        var group = new SalesItemGroup(Guid.NewGuid(), TenantId, "Old");
        await store.AddGroupAsync(group);

        group.Update("New", false);
        await store.SaveGroupAsync(group);

        var loaded = await store.GetGroupAsync(TenantId, group.Id);
        Assert.Equal("New", loaded!.Name);
        Assert.False(loaded.Active);
    }
}
