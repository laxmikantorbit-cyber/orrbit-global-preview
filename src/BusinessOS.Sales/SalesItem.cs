namespace BusinessOS.Sales;

public enum SalesItemStatus
{
    Active = 1,
    Inactive = 2
}

public sealed class SalesItem
{
    public SalesItem(
        Guid id,
        Guid tenantId,
        string code,
        string name,
        string? description,
        decimal defaultRate,
        decimal defaultTaxPercent,
        SalesItemStatus status = SalesItemStatus.Active,
        Guid? catalogProductId = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Sales item id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Item code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Item name is required.", nameof(name));
        if (defaultRate < 0m) throw new ArgumentOutOfRangeException(nameof(defaultRate));
        if (defaultTaxPercent is < 0m or > 100m) throw new ArgumentOutOfRangeException(nameof(defaultTaxPercent));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));

        Id = id;
        TenantId = tenantId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Description = Clean(description);
        DefaultRate = Money(defaultRate);
        DefaultTaxPercent = decimal.Round(defaultTaxPercent, 2, MidpointRounding.AwayFromZero);
        Status = status;
        CatalogProductId = catalogProductId;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public string Code { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public decimal DefaultRate { get; private set; }
    public decimal DefaultTaxPercent { get; private set; }
    public SalesItemStatus Status { get; private set; }
    public Guid? CatalogProductId { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(
        string name,
        string? description,
        decimal defaultRate,
        decimal defaultTaxPercent,
        SalesItemStatus status)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Item name is required.", nameof(name));
        if (defaultRate < 0m) throw new ArgumentOutOfRangeException(nameof(defaultRate));
        if (defaultTaxPercent is < 0m or > 100m) throw new ArgumentOutOfRangeException(nameof(defaultTaxPercent));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));

        Name = name.Trim();
        Description = Clean(description);
        DefaultRate = Money(defaultRate);
        DefaultTaxPercent = decimal.Round(defaultTaxPercent, 2, MidpointRounding.AwayFromZero);
        Status = status;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public static SalesItem Restore(
        Guid id,
        Guid tenantId,
        string code,
        string name,
        string? description,
        decimal defaultRate,
        decimal defaultTaxPercent,
        SalesItemStatus status,
        Guid? catalogProductId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        var item = new SalesItem(
            id, tenantId, code, name, description, defaultRate, defaultTaxPercent,
            status, catalogProductId, createdAtUtc);
        item.UpdatedAtUtc = updatedAtUtc;
        return item;
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
