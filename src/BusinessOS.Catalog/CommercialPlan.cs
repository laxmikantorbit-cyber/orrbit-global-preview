namespace BusinessOS.Catalog;

public sealed class CommercialPlan
{
    private readonly List<PlanVersion> _versions = [];

    public CommercialPlan(
        Guid id,
        Guid tenantId,
        Guid productId,
        string code,
        string name,
        CatalogStatus status = CatalogStatus.Active)
    {
        if (id == Guid.Empty) throw new ArgumentException("Plan id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (productId == Guid.Empty) throw new ArgumentException("Product id is required.", nameof(productId));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Plan code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Plan name is required.", nameof(name));

        Id = id;
        TenantId = tenantId;
        ProductId = productId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Status = status;
    }
    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid ProductId { get; }
    public string Code { get; }
    public string Name { get; private set; }
    public CatalogStatus Status { get; private set; }
    public IReadOnlyList<PlanVersion> Versions => _versions.AsReadOnly();
    public PlanVersion? LatestVersion => _versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();

    public PlanVersion AddVersion(
        int versionNumber,
        BillingRule billing,
        EntitlementProfile entitlements,
        DateTimeOffset effectiveFromUtc)
    {
        if (_versions.Any(x => x.VersionNumber == versionNumber))
            throw new InvalidOperationException("Plan version number already exists.");

        var version = new PlanVersion(
            Guid.NewGuid(), TenantId, Id, versionNumber,
            billing, entitlements, effectiveFromUtc);
        _versions.Add(version);
        return version;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Plan name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetStatus(CatalogStatus status)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        Status = status;
    }
}
