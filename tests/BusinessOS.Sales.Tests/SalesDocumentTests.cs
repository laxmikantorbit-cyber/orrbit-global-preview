using BusinessOS.Sales;

namespace BusinessOS.Sales.Tests;

public sealed class SalesDocumentTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Totals_Apply_Discount_Before_Tax()
    {
        var document = NewDocument(
            new SalesDocumentLine(Guid.NewGuid(), null, "Implementation", 2m, 1000m, 18m),
            discountPercent: 10m);

        Assert.Equal(2000m, document.Totals.Subtotal);
        Assert.Equal(200m, document.Totals.DiscountAmount);
        Assert.Equal(324m, document.Totals.TaxAmount);
        Assert.Equal(2124m, document.Totals.Total);
    }

    [Fact]
    public void Status_Lifecycle_Allows_Sent_Then_Accepted()
    {
        var document = NewDocument(DefaultLine());

        document.ChangeStatus(SalesDocumentStatus.Sent);
        document.ChangeStatus(SalesDocumentStatus.Accepted);

        Assert.Equal(SalesDocumentStatus.Accepted, document.Status);
        Assert.Throws<InvalidOperationException>(() => document.ChangeStatus(SalesDocumentStatus.Draft));
    }

    [Fact]
    public void Sent_Document_Cannot_Be_Edited()
    {
        var document = NewDocument(DefaultLine());
        document.ChangeStatus(SalesDocumentStatus.Sent);

        Assert.Throws<InvalidOperationException>(() => document.UpdateDraft(
            AccountId, "Changed", [DefaultLine()], "INR", DateOnly.FromDateTime(DateTime.UtcNow),
            null, 0m, null, null, null));
    }

    [Fact]
    public void Invalid_Line_And_Date_Are_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewDocument(
            new SalesDocumentLine(Guid.NewGuid(), null, "Bad qty", 0m, 100m, 18m)));

        var issue = new DateOnly(2026, 9, 21);
        Assert.Throws<ArgumentException>(() => new SalesDocument(
            Guid.NewGuid(), TenantId, SalesDocumentKind.Estimate, "EST-000001", AccountId, "Estimate",
            [DefaultLine()], "INR", issue, issue.AddDays(-1)));
    }

    [Fact]
    public void Restore_Preserves_Status_And_Totals()
    {
        var line = DefaultLine();
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var updated = DateTimeOffset.UtcNow;
        var document = SalesDocument.Restore(
            Guid.NewGuid(), TenantId, SalesDocumentKind.Proposal, "PRO-000009", AccountId,
            "Proposal", [line], "INR", new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 30),
            5m, null, "Note", "Terms", SalesDocumentStatus.Sent, created, updated);

        Assert.Equal(SalesDocumentStatus.Sent, document.Status);
        Assert.Equal(created, document.CreatedAtUtc);
        Assert.Equal(updated, document.UpdatedAtUtc);
        Assert.True(document.Totals.Total > 0m);
    }

    private static SalesDocument NewDocument(SalesDocumentLine line, decimal discountPercent = 0m) =>
        new(Guid.NewGuid(), TenantId, SalesDocumentKind.Proposal, "PRO-000001", AccountId,
            "Repair software proposal", [line], "INR", new DateOnly(2026, 9, 21),
            new DateOnly(2026, 9, 30), discountPercent);

    private static SalesDocumentLine DefaultLine() =>
        new(Guid.NewGuid(), null, "Software", 1m, 15000m, 18m);
}
