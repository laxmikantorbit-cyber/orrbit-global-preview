namespace BusinessOS.Sales;

public enum SalesInvoiceStatus
{
    Draft = 1,
    Sent = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Overdue = 5,
    Void = 6
}

public sealed record SalesInvoiceTotals(
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal Total);

public sealed record SalesInvoicePayment(
    Guid Id,
    Guid TenantId,
    Guid InvoiceId,
    string PaymentNumber,
    decimal Amount,
    string Method,
    string? Reference,
    string? Notes,
    DateTimeOffset ReceivedAtUtc,
    Guid? ReceivedByUserId,
    DateTimeOffset CreatedAtUtc);

public sealed class SalesInvoice
{
    private List<SalesDocumentLine> _lines;

    public SalesInvoice(
        Guid id,
        Guid tenantId,
        string invoiceNumber,
        Guid accountId,
        string subject,
        IEnumerable<SalesDocumentLine> lines,
        string currencyCode = "INR",
        DateOnly? issueDate = null,
        DateOnly? dueDate = null,
        decimal discountPercent = 0m,
        Guid? opportunityId = null,
        Guid? sourceDocumentId = null,
        string? notes = null,
        string? terms = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Invoice id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (accountId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(accountId));
        if (string.IsNullOrWhiteSpace(invoiceNumber)) throw new ArgumentException("Invoice number is required.", nameof(invoiceNumber));

        var issue = issueDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var due = dueDate ?? issue.AddDays(7);

        Id = id;
        TenantId = tenantId;
        InvoiceNumber = invoiceNumber.Trim().ToUpperInvariant();
        AccountId = accountId;
        SourceDocumentId = sourceDocumentId;
        Status = SalesInvoiceStatus.Draft;
        AmountPaid = 0m;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        _lines = [];

        ApplyDraft(
            subject, lines, currencyCode, issue, due, discountPercent,
            opportunityId, sourceDocumentId, notes, terms, false);
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public string InvoiceNumber { get; }
    public Guid AccountId { get; private set; }
    public Guid? OpportunityId { get; private set; }
    public Guid? SourceDocumentId { get; }
    public string Subject { get; private set; } = string.Empty;
    public SalesInvoiceStatus Status { get; private set; }
    public string CurrencyCode { get; private set; } = "INR";
    public DateOnly IssueDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal AmountPaid { get; private set; }
    public decimal AmountCredited { get; private set; }
    public string? Notes { get; private set; }
    public string? Terms { get; private set; }
    public IReadOnlyList<SalesDocumentLine> Lines => _lines;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public SalesInvoiceTotals Totals
    {
        get
        {
            var subtotal = Money(_lines.Sum(LineSubtotal));
            var discount = Money(subtotal * DiscountPercent / 100m);
            var taxableFactor = 1m - DiscountPercent / 100m;
            var tax = Money(_lines.Sum(line => LineSubtotal(line) * taxableFactor * line.TaxPercent / 100m));
            return new SalesInvoiceTotals(subtotal, discount, tax, Money(subtotal - discount + tax));
        }
    }

    public decimal NetTotal => Money(Math.Max(0m, Totals.Total - AmountCredited));
    public decimal Balance => Money(Math.Max(0m, NetTotal - AmountPaid));
    public decimal OverpaidAmount => Money(Math.Max(0m, AmountPaid - NetTotal));

    public void UpdateDraft(
        Guid accountId,
        string subject,
        IEnumerable<SalesDocumentLine> lines,
        string currencyCode,
        DateOnly issueDate,
        DateOnly dueDate,
        decimal discountPercent,
        Guid? opportunityId,
        string? notes,
        string? terms)
    {
        if (Status != SalesInvoiceStatus.Draft)
            throw new InvalidOperationException("Only draft invoices can be edited.");
        if (accountId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(accountId));

        AccountId = accountId;
        ApplyDraft(
            subject, lines, currencyCode, issueDate, dueDate, discountPercent,
            opportunityId, SourceDocumentId, notes, terms, true);
    }

    public void ChangeStatus(SalesInvoiceStatus status, DateOnly? asOf = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (status == Status) return;

        var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        switch (status)
        {
            case SalesInvoiceStatus.Sent when Status == SalesInvoiceStatus.Draft:
                break;
            case SalesInvoiceStatus.Draft when Status == SalesInvoiceStatus.Sent && AmountPaid == 0m && AmountCredited == 0m:
                break;
            case SalesInvoiceStatus.Overdue
                when Status is SalesInvoiceStatus.Sent or SalesInvoiceStatus.PartiallyPaid
                     && DueDate < today
                     && Balance > 0m:
                break;
            case SalesInvoiceStatus.Void
                when Status is SalesInvoiceStatus.Draft or SalesInvoiceStatus.Sent or SalesInvoiceStatus.Overdue
                     && AmountPaid == 0m
                     && AmountCredited == 0m:
                break;
            default:
                throw new InvalidOperationException($"Cannot move {Status} invoice to {status}.");
        }

        Status = status;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void RecordPayment(decimal amount, DateOnly? asOf = null)
    {
        if (Status is SalesInvoiceStatus.Draft or SalesInvoiceStatus.Void or SalesInvoiceStatus.Paid)
            throw new InvalidOperationException("Invoice cannot receive a payment in its current state.");
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount));

        amount = Money(amount);
        if (amount > Balance)
            throw new InvalidOperationException("Payment cannot exceed invoice balance.");

        AmountPaid = Money(AmountPaid + amount);
        if (Balance == 0m)
        {
            Status = SalesInvoiceStatus.Paid;
        }
        else
        {
            var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            Status = DueDate < today ? SalesInvoiceStatus.Overdue : SalesInvoiceStatus.PartiallyPaid;
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void ApplyCredit(decimal amount, DateOnly? asOf = null)
    {
        if (Status is SalesInvoiceStatus.Draft or SalesInvoiceStatus.Void)
            throw new InvalidOperationException("Invoice cannot receive a credit note in its current state.");
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount));

        amount = Money(amount);
        if (amount > Totals.Total - AmountCredited)
            throw new InvalidOperationException("Credit note cannot exceed the remaining creditable invoice amount.");

        AmountCredited = Money(AmountCredited + amount);
        RecalculateSettlementStatus(asOf);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void ReverseCredit(decimal amount, DateOnly? asOf = null)
    {
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        amount = Money(amount);
        if (amount > AmountCredited)
            throw new InvalidOperationException("Credit reversal cannot exceed credited amount.");

        AmountCredited = Money(AmountCredited - amount);
        RecalculateSettlementStatus(asOf);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public static SalesInvoice Restore(
        Guid id,
        Guid tenantId,
        string invoiceNumber,
        Guid accountId,
        string subject,
        IEnumerable<SalesDocumentLine> lines,
        string currencyCode,
        DateOnly issueDate,
        DateOnly dueDate,
        decimal discountPercent,
        decimal amountPaid,
        decimal amountCredited,
        Guid? opportunityId,
        Guid? sourceDocumentId,
        string? notes,
        string? terms,
        SalesInvoiceStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        var invoice = new SalesInvoice(
            id, tenantId, invoiceNumber, accountId, subject, lines, currencyCode,
            issueDate, dueDate, discountPercent, opportunityId, sourceDocumentId,
            notes, terms, createdAtUtc);
        if (amountPaid < 0m || amountPaid > invoice.Totals.Total)
            throw new ArgumentOutOfRangeException(nameof(amountPaid));
        if (amountCredited < 0m || amountCredited > invoice.Totals.Total)
            throw new ArgumentOutOfRangeException(nameof(amountCredited));
        invoice.AmountPaid = Money(amountPaid);
        invoice.AmountCredited = Money(amountCredited);
        invoice.Status = status;
        invoice.UpdatedAtUtc = updatedAtUtc;
        return invoice;
    }

    private void ApplyDraft(
        string subject,
        IEnumerable<SalesDocumentLine> lines,
        string currencyCode,
        DateOnly issueDate,
        DateOnly dueDate,
        decimal discountPercent,
        Guid? opportunityId,
        Guid? sourceDocumentId,
        string? notes,
        string? terms,
        bool touch)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("Subject is required.", nameof(subject));
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
            throw new ArgumentException("Currency code must be three characters.", nameof(currencyCode));
        if (dueDate < issueDate) throw new ArgumentException("Due date cannot be before issue date.", nameof(dueDate));
        if (discountPercent is < 0m or > 100m) throw new ArgumentOutOfRangeException(nameof(discountPercent));

        var normalized = (lines ?? throw new ArgumentNullException(nameof(lines)))
            .Select(NormalizeLine)
            .ToList();
        if (normalized.Count == 0) throw new ArgumentException("At least one line item is required.", nameof(lines));

        Subject = subject.Trim();
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        IssueDate = issueDate;
        DueDate = dueDate;
        DiscountPercent = discountPercent;
        OpportunityId = opportunityId;
        Notes = Clean(notes);
        Terms = Clean(terms);
        _lines = normalized;
        if (touch) UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static SalesDocumentLine NormalizeLine(SalesDocumentLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Description)) throw new ArgumentException("Line description is required.");
        if (line.Quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(line.Quantity));
        if (line.UnitPrice < 0m) throw new ArgumentOutOfRangeException(nameof(line.UnitPrice));
        if (line.TaxPercent is < 0m or > 100m) throw new ArgumentOutOfRangeException(nameof(line.TaxPercent));
        return line with
        {
            Id = line.Id == Guid.Empty ? Guid.NewGuid() : line.Id,
            Description = line.Description.Trim()
        };
    }

    private void RecalculateSettlementStatus(DateOnly? asOf)
    {
        if (Balance == 0m)
        {
            Status = SalesInvoiceStatus.Paid;
            return;
        }

        var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (DueDate < today)
        {
            Status = SalesInvoiceStatus.Overdue;
            return;
        }

        Status = AmountPaid > 0m || AmountCredited > 0m
            ? SalesInvoiceStatus.PartiallyPaid
            : SalesInvoiceStatus.Sent;
    }

    private static decimal LineSubtotal(SalesDocumentLine line) => line.Quantity * line.UnitPrice;
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
