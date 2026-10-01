using BusinessOS.Api.Crm;
using BusinessOS.Sales;

namespace BusinessOS.Api.Tests;

public sealed class CrmRecurringInvoiceTests
{
    private static SalesInvoice SourceInvoice() => new(
        Guid.NewGuid(),
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "INV-000001",
        Guid.NewGuid(),
        "Monthly Support",
        [new SalesDocumentLine(Guid.NewGuid(), null, "Support", 1m, 5000m, 18m)],
        "INR",
        new DateOnly(2026, 10, 1),
        new DateOnly(2026, 10, 8));

    [Theory]
    [InlineData("Monthly", "2026-11-01")]
    [InlineData("Quarterly", "2027-01-01")]
    [InlineData("Yearly", "2027-10-01")]
    public void NextDate_Advances_Without_Drift(string frequency, string expected)
    {
        var next = CrmRecurringInvoiceRules.NextDate(new DateOnly(2026, 10, 1), frequency);
        Assert.Equal(DateOnly.Parse(expected), next);
    }

    [Fact]
    public void Create_Validates_Frequency_And_Due_Days()
    {
        var source = SourceInvoice();
        Assert.Throws<ArgumentException>(() =>
            CrmRecurringInvoiceRules.Create(source.TenantId, "Test", source, "Weekly", new DateOnly(2026, 11, 1), 7));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrmRecurringInvoiceRules.Create(source.TenantId, "Test", source, "Monthly", new DateOnly(2026, 11, 1), 366));
    }

    [Fact]
    public async Task InMemory_Store_Persists_And_Updates_Template()
    {
        var source = SourceInvoice();
        var template = CrmRecurringInvoiceRules.Create(
            source.TenantId, "Support recurring", source, "Monthly", new DateOnly(2026, 11, 1), 7);
        var store = new InMemoryCrmRecurringInvoiceStore();

        await store.AddAsync(template);
        var loaded = await store.GetAsync(source.TenantId, template.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Monthly", loaded!.Frequency);
        Assert.Single(loaded.Lines);

        var paused = loaded with { Active = false, UpdatedAtUtc = DateTimeOffset.UtcNow };
        await store.SaveAsync(paused);
        Assert.False((await store.GetAsync(source.TenantId, template.Id))!.Active);
    }
}
