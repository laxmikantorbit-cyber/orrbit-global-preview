namespace BusinessOS.Sales;

public enum CreditNoteStatus
{
    Draft = 1,
    Issued = 2,
    Void = 3
}

public sealed class CreditNote
{
    public CreditNote(
        Guid id,
        Guid tenantId,
        string creditNoteNumber,
        Guid invoiceId,
        Guid accountId,
        DateOnly issueDate,
        decimal amount,
        string reason,
        string? notes = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Credit note id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (invoiceId == Guid.Empty) throw new ArgumentException("Invoice id is required.", nameof(invoiceId));
        if (accountId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(accountId));
        if (string.IsNullOrWhiteSpace(creditNoteNumber)) throw new ArgumentException("Credit note number is required.", nameof(creditNoteNumber));
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Credit note reason is required.", nameof(reason));

        Id = id;
        TenantId = tenantId;
        CreditNoteNumber = creditNoteNumber.Trim().ToUpperInvariant();
        InvoiceId = invoiceId;
        AccountId = accountId;
        IssueDate = issueDate;
        Amount = Money(amount);
        Reason = reason.Trim();
        Notes = Clean(notes);
        Status = CreditNoteStatus.Draft;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public string CreditNoteNumber { get; }
    public Guid InvoiceId { get; }
    public Guid AccountId { get; }
    public DateOnly IssueDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; }
    public string? Notes { get; private set; }
    public CreditNoteStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void UpdateDraft(DateOnly issueDate, decimal amount, string reason, string? notes)
    {
        if (Status != CreditNoteStatus.Draft)
            throw new InvalidOperationException("Only draft credit notes can be edited.");
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Credit note reason is required.", nameof(reason));

        IssueDate = issueDate;
        Amount = Money(amount);
        Reason = reason.Trim();
        Notes = Clean(notes);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Issue()
    {
        if (Status != CreditNoteStatus.Draft)
            throw new InvalidOperationException("Only draft credit notes can be issued.");
        Status = CreditNoteStatus.Issued;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Void()
    {
        if (Status != CreditNoteStatus.Issued)
            throw new InvalidOperationException("Only issued credit notes can be voided.");
        Status = CreditNoteStatus.Void;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public static CreditNote Restore(
        Guid id,
        Guid tenantId,
        string creditNoteNumber,
        Guid invoiceId,
        Guid accountId,
        DateOnly issueDate,
        decimal amount,
        string reason,
        string? notes,
        CreditNoteStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        var note = new CreditNote(
            id, tenantId, creditNoteNumber, invoiceId, accountId, issueDate,
            amount, reason, notes, createdAtUtc);
        note.Status = status;
        note.UpdatedAtUtc = updatedAtUtc;
        return note;
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
