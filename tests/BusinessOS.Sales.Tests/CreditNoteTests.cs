using BusinessOS.Sales;

namespace BusinessOS.Sales.Tests;

public sealed class CreditNoteTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid InvoiceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AccountId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void Credit_Note_Lifecycle_Is_Draft_Issued_Void()
    {
        var note = NewNote();

        note.Issue();
        Assert.Equal(CreditNoteStatus.Issued, note.Status);

        note.Void();
        Assert.Equal(CreditNoteStatus.Void, note.Status);
    }

    [Fact]
    public void Only_Draft_Credit_Note_Can_Be_Edited()
    {
        var note = NewNote();
        note.UpdateDraft(new DateOnly(2026, 9, 22), 750m, "Adjusted", "Updated");

        Assert.Equal(750m, note.Amount);
        Assert.Equal("Adjusted", note.Reason);

        note.Issue();
        Assert.Throws<InvalidOperationException>(() =>
            note.UpdateDraft(new DateOnly(2026, 9, 23), 500m, "No", null));
    }

    [Fact]
    public void Invalid_Amount_And_Reason_Are_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CreditNote(Guid.NewGuid(), TenantId, "CN-000001", InvoiceId, AccountId,
                new DateOnly(2026, 9, 21), 0m, "Reason"));
        Assert.Throws<ArgumentException>(() =>
            new CreditNote(Guid.NewGuid(), TenantId, "CN-000001", InvoiceId, AccountId,
                new DateOnly(2026, 9, 21), 100m, " "));
    }

    private static CreditNote NewNote() =>
        new(Guid.NewGuid(), TenantId, "CN-000001", InvoiceId, AccountId,
            new DateOnly(2026, 9, 21), 500m, "Service adjustment", "QA");
}
