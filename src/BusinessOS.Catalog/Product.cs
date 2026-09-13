namespace BusinessOS.Catalog;

public sealed class Product
{
    public Product(Guid id, Guid tenantId, string code, string name, CatalogStatus status = CatalogStatus.Active)
    {
        if (id == Guid.Empty) throw new ArgumentException("Product id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Product code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Product name is required.", nameof(name));
        Id = id;
        TenantId = tenantId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Status = status;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public string Code { get; }
    public string Name { get; private set; }
    public CatalogStatus Status { get; private set; }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Product name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetStatus(CatalogStatus status) => Status = status;
}
