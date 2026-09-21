namespace BusinessOS.Sales;

public enum SalesDocumentKind
{
    Proposal = 1,
    Estimate = 2
}

public enum SalesDocumentStatus
{
    Draft = 1,
    Sent = 2,
    Accepted = 3,
    Rejected = 4,
    Expired = 5
}

public sealed record SalesDocumentLine(
    Guid Id,
    Guid? ItemId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxPercent);

public sealed record SalesDocumentTotals(
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal Total);

public sealed class SalesDocument
{
    private List<SalesDocumentLine> _lines;

    public SalesDocument(
        Guid id,
        Guid tenantId,
        SalesDocumentKind kind,
        string documentNumber,
        Guid accountId,
        string subject,
        IEnumerable<SalesDocumentLine> lines,
        string currencyCode = "INR",
        DateOnly? issueDate = null,
        DateOnly? expiryDate = null,
        decimal discountPercent = 0m,
        Guid? opportunityId = null,
        string? notes = null,
        string? terms = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Sales document id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (accountId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(accountId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (string.IsNullOrWhiteSpace(documentNumber)) throw new ArgumentException("Document number is required.", nameof(documentNumber));
        Id = id;
        TenantId = tenantId;
        Kind = kind;
        DocumentNumber = documentNumber.Trim().ToUpperInvariant();
        AccountId = accountId;
        Status = SalesDocumentStatus.Draft;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        _lines = [];
        ApplyDraft(subject, lines, currencyCode, issueDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            expiryDate, discountPercent, opportunityId, notes, terms, false);
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public SalesDocumentKind Kind { get; }
    public string DocumentNumber { get; }
    public Guid AccountId { get; private set; }
    public Guid? OpportunityId { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public SalesDocumentStatus Status { get; private set; }
    public string CurrencyCode { get; private set; } = "INR";
    public DateOnly IssueDate { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public string? Notes { get; private set; }
    public string? Terms { get; private set; }
    public IReadOnlyList<SalesDocumentLine> Lines => _lines;
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public SalesDocumentTotals Totals
    {
        get
        {
            var subtotal = Money(_lines.Sum(LineSubtotal));
            var discount = Money(subtotal * DiscountPercent / 100m);
            var taxableFactor = 1m - DiscountPercent / 100m;
            var tax = Money(_lines.Sum(line => LineSubtotal(line) * taxableFactor * line.TaxPercent / 100m));
            return new SalesDocumentTotals(subtotal, discount, tax, Money(subtotal - discount + tax));
        }
    }

    public void UpdateDraft(
        Guid accountId,
        string subject,
        IEnumerable<SalesDocumentLine> lines,
        string currencyCode,
        DateOnly issueDate,
        DateOnly? expiryDate,
        decimal discountPercent,
        Guid? opportunityId,
        string? notes,
        string? terms)
    {
        if (Status != SalesDocumentStatus.Draft)
            throw new InvalidOperationException("Only draft sales documents can be edited.");
        if (accountId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(accountId));
        AccountId = accountId;
        ApplyDraft(subject, lines, currencyCode, issueDate, expiryDate, discountPercent,
            opportunityId, notes, terms, true);
    }

    public void ChangeStatus(SalesDocumentStatus status)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (status == Status) return;
        switch (status)
        {
            case SalesDocumentStatus.Draft when Status is SalesDocumentStatus.Sent or SalesDocumentStatus.Rejected:
                break;
            case SalesDocumentStatus.Sent when Status == SalesDocumentStatus.Draft:
                break;
            case SalesDocumentStatus.Accepted when Status == SalesDocumentStatus.Sent:
                break;
            case SalesDocumentStatus.Rejected when Status == SalesDocumentStatus.Sent:
                break;
            case SalesDocumentStatus.Expired when Status is SalesDocumentStatus.Draft or SalesDocumentStatus.Sent:
                break;
            default:
                throw new InvalidOperationException($"Cannot move {Status} document to {status}.");
        }
        Status = status;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public static SalesDocument Restore(
        Guid id, Guid tenantId, SalesDocumentKind kind, string documentNumber, Guid accountId,
        string subject, IEnumerable<SalesDocumentLine> lines, string currencyCode, DateOnly issueDate,
        DateOnly? expiryDate, decimal discountPercent, Guid? opportunityId, string? notes, string? terms,
        SalesDocumentStatus status, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
    {
        var item = new SalesDocument(id, tenantId, kind, documentNumber, accountId, subject, lines,
            currencyCode, issueDate, expiryDate, discountPercent, opportunityId, notes, terms, createdAtUtc);
        item.Status = status;
        item.UpdatedAtUtc = updatedAtUtc;
        return item;
    }

    private void ApplyDraft(
        string subject, IEnumerable<SalesDocumentLine> lines, string currencyCode, DateOnly issueDate,
        DateOnly? expiryDate, decimal discountPercent, Guid? opportunityId, string? notes, string? terms,
        bool touch)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("Subject is required.", nameof(subject));
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
            throw new ArgumentException("Currency code must be three characters.", nameof(currencyCode));
        if (discountPercent is < 0m or > 100m) throw new ArgumentOutOfRangeException(nameof(discountPercent));
        if (expiryDate.HasValue && expiryDate.Value < issueDate)
            throw new ArgumentException("Expiry date cannot be before issue date.", nameof(expiryDate));
        var normalized = (lines ?? throw new ArgumentNullException(nameof(lines))).Select(NormalizeLine).ToList();
        if (normalized.Count == 0) throw new ArgumentException("At least one line item is required.", nameof(lines));
        Subject = subject.Trim();
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        IssueDate = issueDate;
        ExpiryDate = expiryDate;
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
        return line with { Id = line.Id == Guid.Empty ? Guid.NewGuid() : line.Id, Description = line.Description.Trim() };
    }

    private static decimal LineSubtotal(SalesDocumentLine line) => line.Quantity * line.UnitPrice;
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
