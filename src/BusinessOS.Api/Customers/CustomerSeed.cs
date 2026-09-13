using BusinessOS.Api.Tenancy;
using BusinessOS.Customers;

namespace BusinessOS.Api.Customers;

public static class CustomerSeed
{
    public static IOrganisationRepository CreateRepository()
    {
        var alpha = new Organisation(
            CustomerStore.TenantACustomerId,
            PocIdentitySeed.TenantAId,
            "Alpha Customer");
        alpha.AddRole(OrganisationRole.Customer);
        alpha.AddContact(new ContactPerson(
            Guid.NewGuid(), "Alpha Contact", "alpha@example.test", null, true));

        var beta = new Organisation(
            CustomerStore.TenantBCustomerId,
            PocIdentitySeed.TenantBId,
            "Beta Customer");
        beta.AddRole(OrganisationRole.Customer);
        beta.AddContact(new ContactPerson(
            Guid.NewGuid(), "Beta Contact", "beta@example.test", null, true));

        return new InMemoryOrganisationRepository(new[] { alpha, beta });
    }
}
