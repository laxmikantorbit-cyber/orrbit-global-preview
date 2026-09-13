using BusinessOS.Catalog;

namespace BusinessOS.Catalog.Tests;

public sealed class CatalogTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Product_Code_Is_Normalized()
    {
        var product = new Product(Guid.NewGuid(), TenantA, " product-one ", "Product One");
        Assert.Equal("PRODUCT-ONE", product.Code);
    }

    [Fact]
    public void Plan_Version_Normalizes_Commercial_Snapshot()
    {
        var plan = NewPlan(TenantA, Guid.NewGuid());
        var version = plan.AddVersion(
            1,
            Billing(29999, "inr"),
            Entitlements("web-admin", " field-app "),
            DateTimeOffset.Now);

        Assert.Equal("INR", version.Billing.CurrencyCode);
        Assert.Contains("WEB-ADMIN", version.Entitlements.Features);
        Assert.Contains("FIELD-APP", version.Entitlements.Features);
        Assert.Equal(TimeSpan.Zero, version.EffectiveFromUtc.Offset);
    }

    [Fact]
    public void Plan_Version_Copies_Feature_Set()
    {
        var features = new HashSet<string> { "WEB-ADMIN" };
        var plan = NewPlan(TenantA, Guid.NewGuid());
        var version = plan.AddVersion(1, Billing(100), Entitlements(features), DateTimeOffset.UtcNow);
        features.Add("LATE-MUTATION");
        Assert.DoesNotContain("LATE-MUTATION", version.Entitlements.Features);
    }

    [Fact]
    public void Duplicate_Plan_Version_Number_Is_Rejected()
    {
        var plan = NewPlan(TenantA, Guid.NewGuid());
        plan.AddVersion(1, Billing(100), Entitlements("A"), DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            plan.AddVersion(1, Billing(200), Entitlements("B"), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Latest_Version_Uses_Highest_Version_Number()
    {
        var plan = NewPlan(TenantA, Guid.NewGuid());
        plan.AddVersion(2, Billing(200), Entitlements("B"), DateTimeOffset.UtcNow);
        plan.AddVersion(1, Billing(100), Entitlements("A"), DateTimeOffset.UtcNow);
        Assert.Equal(2, plan.LatestVersion!.VersionNumber);
    }

    [Fact]
    public void Invalid_Billing_And_Entitlements_Are_Rejected()
    {
        var plan = NewPlan(TenantA, Guid.NewGuid());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            plan.AddVersion(1, Billing(-1), Entitlements("A"), DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            plan.AddVersion(1, Billing(10), new EntitlementProfile(-1, 1, 1, 1, false, new HashSet<string>()), DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Repository_Isolates_Tenants()
    {
        var a = NewProduct(TenantA, "A");
        var b = NewProduct(TenantB, "B");
        var repository = new InMemoryCatalogRepository(new[] { a, b });

        Assert.Single(await repository.ListProductsAsync(TenantA));
        Assert.Null(await repository.GetProductAsync(TenantA, b.Id));
    }

    [Fact]
    public async Task Plan_Must_Reference_Product_In_Same_Tenant()
    {
        var product = NewProduct(TenantB, "B");
        var repository = new InMemoryCatalogRepository(new[] { product });
        var plan = NewPlan(TenantA, product.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddPlanAsync(plan));
    }

    [Fact]
    public async Task Duplicate_Product_Code_In_Tenant_Is_Rejected()
    {
        var repository = new InMemoryCatalogRepository();
        await repository.AddProductAsync(NewProduct(TenantA, "CORE"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.AddProductAsync(NewProduct(TenantA, "core")));
    }

    [Fact]
    public async Task Duplicate_Plan_Code_For_Product_Is_Rejected()
    {
        var product = NewProduct(TenantA, "CORE");
        var repository = new InMemoryCatalogRepository(new[] { product });
        await repository.AddPlanAsync(NewPlan(TenantA, product.Id, "PRO"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.AddPlanAsync(NewPlan(TenantA, product.Id, "pro")));
    }

    [Fact]
    public async Task Same_Product_Code_Is_Allowed_In_Different_Tenants()
    {
        var repository = new InMemoryCatalogRepository();
        await repository.AddProductAsync(NewProduct(TenantA, "CORE"));
        await repository.AddProductAsync(NewProduct(TenantB, "CORE"));
        Assert.Single(await repository.ListProductsAsync(TenantA));
        Assert.Single(await repository.ListProductsAsync(TenantB));
    }

    private static Product NewProduct(Guid tenantId, string code) =>
        new(Guid.NewGuid(), tenantId, code, $"Product {code}");

    private static CommercialPlan NewPlan(Guid tenantId, Guid productId, string code = "STANDARD") =>
        new(Guid.NewGuid(), tenantId, productId, code, $"Plan {code}");

    private static BillingRule Billing(decimal amount, string currency = "USD") =>
        new(amount, currency, BillingCycle.Annual, 12);

    private static EntitlementProfile Entitlements(params string[] features) =>
        Entitlements(new HashSet<string>(features));

    private static EntitlementProfile Entitlements(IReadOnlySet<string> features) =>
        new(1, 1, 2, 2, true, features);
}
