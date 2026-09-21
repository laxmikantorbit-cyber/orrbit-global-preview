using BusinessOS.Sales;

namespace BusinessOS.Sales.Tests;

public sealed class SalesItemTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Item_Normalizes_Code_Rate_And_Tax()
    {
        var item = new SalesItem(
            Guid.NewGuid(), TenantId, " svc-001 ", " Annual Support ",
            " Support plan ", 5000.126m, 18.126m);

        Assert.Equal("SVC-001", item.Code);
        Assert.Equal("Annual Support", item.Name);
        Assert.Equal("Support plan", item.Description);
        Assert.Equal(5000.13m, item.DefaultRate);
        Assert.Equal(18.13m, item.DefaultTaxPercent);
        Assert.Equal(SalesItemStatus.Active, item.Status);
    }

    [Fact]
    public void Item_Update_Changes_Commercial_Defaults_And_Status()
    {
        var item = new SalesItem(
            Guid.NewGuid(), TenantId, "SVC-001", "Support", null, 5000m, 18m);

        item.Update("Support Plus", "Priority support", 7500m, 12m, SalesItemStatus.Inactive);

        Assert.Equal("Support Plus", item.Name);
        Assert.Equal("Priority support", item.Description);
        Assert.Equal(7500m, item.DefaultRate);
        Assert.Equal(12m, item.DefaultTaxPercent);
        Assert.Equal(SalesItemStatus.Inactive, item.Status);
    }

    [Fact]
    public void Invalid_Rate_Or_Tax_Is_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SalesItem(Guid.NewGuid(), TenantId, "BAD-1", "Bad", null, -1m, 18m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SalesItem(Guid.NewGuid(), TenantId, "BAD-2", "Bad", null, 1m, 101m));
    }
}
