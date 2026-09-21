using BusinessOS.Sales;

namespace BusinessOS.Sales.Tests;

public sealed class SalesInvoiceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Invoice_Totals_And_Balance_Are_Correct()
    {
        var invoice = NewInvoice(discountPercent: 10m);

        Assert.Equal(11000m, invoice.Totals.Subtotal);
        Assert.Equal(1100m, invoice.Totals.DiscountAmount);
        Assert.Equal(1782m, invoice.Totals.TaxAmount);
        Assert.Equal(11682m, invoice.Totals.Total);
        Assert.Equal(11682m, invoice.Balance);
    }

    [Fact]
    public void Partial_And_Full_Payments_Update_Status()
    {
        var invoice = NewInvoice();
        invoice.ChangeStatus(SalesInvoiceStatus.Sent);

        invoice.RecordPayment(1000m, new DateOnly(2026, 9, 21));
        Assert.Equal(SalesInvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal(1000m, invoice.AmountPaid);
        Assert.Equal(invoice.Totals.Total - 1000m, invoice.Balance);

        invoice.RecordPayment(invoice.Balance, new DateOnly(2026, 9, 21));
        Assert.Equal(SalesInvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0m, invoice.Balance);
    }

    [Fact]
    public void Payment_Cannot_Exceed_Balance_Or_Be_Recorded_On_Draft()
    {
        var invoice = NewInvoice();
        Assert.Throws<InvalidOperationException>(() => invoice.RecordPayment(1m));

        invoice.ChangeStatus(SalesInvoiceStatus.Sent);
        Assert.Throws<InvalidOperationException>(() => invoice.RecordPayment(invoice.Balance + 1m));
    }

    [Fact]
    public void Sent_Invoice_Cannot_Be_Edited()
    {
        var invoice = NewInvoice();
        invoice.ChangeStatus(SalesInvoiceStatus.Sent);

        Assert.Throws<InvalidOperationException>(() => invoice.UpdateDraft(
            AccountId, "Changed", [DefaultLine()], "INR",
            new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 28),
            0m, null, null, null));
    }

    [Fact]
    public void Invoice_Can_Be_Marked_Overdue_Only_After_Due_Date()
    {
        var invoice = NewInvoice();
        invoice.ChangeStatus(SalesInvoiceStatus.Sent);

        Assert.Throws<InvalidOperationException>(() =>
            invoice.ChangeStatus(SalesInvoiceStatus.Overdue, new DateOnly(2026, 9, 25)));

        invoice.ChangeStatus(SalesInvoiceStatus.Overdue, new DateOnly(2026, 10, 1));
        Assert.Equal(SalesInvoiceStatus.Overdue, invoice.Status);
    }

    [Fact]
    public void Invoice_With_Payment_Cannot_Be_Voided()
    {
        var invoice = NewInvoice();
        invoice.ChangeStatus(SalesInvoiceStatus.Sent);
        invoice.RecordPayment(100m, new DateOnly(2026, 9, 21));

        Assert.Throws<InvalidOperationException>(() => invoice.ChangeStatus(SalesInvoiceStatus.Void));
    }

    [Fact]
    public void Constructor_Preserves_Source_Document_Link()
    {
        var sourceDocumentId = Guid.NewGuid();
        var invoice = new SalesInvoice(
            Guid.NewGuid(), TenantId, "INV-000006", AccountId, "Converted",
            [DefaultLine()], "INR", new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 28),
            0m, null, sourceDocumentId);

        Assert.Equal(sourceDocumentId, invoice.SourceDocumentId);
    }

    [Fact]
    public void Restore_Preserves_Paid_Amount_And_Status()
    {
        var created = DateTimeOffset.UtcNow.AddDays(-2);
        var updated = DateTimeOffset.UtcNow.AddDays(-1);
        var invoice = SalesInvoice.Restore(
            Guid.NewGuid(), TenantId, "INV-000007", AccountId, "Restored",
            [DefaultLine()], "INR", new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 30),
            0m, 100m, null, null, "Note", "Terms",
            SalesInvoiceStatus.PartiallyPaid, created, updated);

        Assert.Equal(100m, invoice.AmountPaid);
        Assert.Equal(SalesInvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal(created, invoice.CreatedAtUtc);
        Assert.Equal(updated, invoice.UpdatedAtUtc);
    }

    private static SalesInvoice NewInvoice(decimal discountPercent = 0m) =>
        new(
            Guid.NewGuid(), TenantId, "INV-000001", AccountId, "Repair software invoice",
            [
                new SalesDocumentLine(Guid.NewGuid(), null, "Software", 1m, 10000m, 18m),
                new SalesDocumentLine(Guid.NewGuid(), null, "Implementation", 2m, 500m, 18m)
            ],
            "INR", new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 30),
            discountPercent);

    private static SalesDocumentLine DefaultLine() =>
        new(Guid.NewGuid(), null, "Software", 1m, 1000m, 18m);
}
