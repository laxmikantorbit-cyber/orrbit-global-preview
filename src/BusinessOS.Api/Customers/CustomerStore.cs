using BusinessOS.Api.Tenancy;
using BusinessOS.Customers;

namespace BusinessOS.Api.Customers;

public sealed class CustomerStore
{
    public static readonly Guid TenantACustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TenantBCustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly TenantContext _tenant;
    private readonly IOrganisationRepository _organisations;

    public CustomerStore(TenantContext tenant, IOrganisationRepository organisations)
    {
        _tenant = tenant;
        _organisations = organisations;
    }

    public async Task<IReadOnlyList<Customer>> ListAsync(CancellationToken cancellationToken = default)
    {
        var organisations = await _organisations.ListByRoleAsync(
            _tenant.TenantId,
            OrganisationRole.Customer,
            cancellationToken);

        return organisations.Select(ToCustomer).ToArray();
    }
    public async Task<Customer?> FindAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var organisation = await _organisations.GetAsync(_tenant.TenantId, id, cancellationToken);
        return organisation is not null && organisation.HasRole(OrganisationRole.Customer)
            ? ToCustomer(organisation)
            : null;
    }

    private static Customer ToCustomer(Organisation organisation) =>
        new(
            organisation.Id,
            organisation.TenantId,
            organisation.Name,
            organisation.PrimaryContact?.Email);
}
