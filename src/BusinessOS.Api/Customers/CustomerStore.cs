using BusinessOS.Api.Tenancy;

namespace BusinessOS.Api.Customers;

public sealed class CustomerStore
{
    public static readonly Guid TenantACustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TenantBCustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly TenantContext _tenant;
    private readonly Customer[] _customers =
    [
        new(TenantACustomerId, "TENANT-A", "Alpha Customer", "alpha@example.test"),
        new(TenantBCustomerId, "TENANT-B", "Beta Customer", "beta@example.test")
    ];

    public CustomerStore(TenantContext tenant) => _tenant = tenant;

    public IReadOnlyList<Customer> List() =>
        _customers.Where(x => x.TenantId == _tenant.TenantId).ToArray();

    public Customer? Find(Guid id) =>
        _customers.SingleOrDefault(x => x.TenantId == _tenant.TenantId && x.Id == id);
}
