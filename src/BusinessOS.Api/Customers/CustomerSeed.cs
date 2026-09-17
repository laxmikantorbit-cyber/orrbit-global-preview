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
            "Alpha Customer",
            "Alpha Customer Pvt. Ltd. (FreeTesting)",
            "22ABCDE1234F1Z5");
        alpha.AddRole(OrganisationRole.Customer);
        alpha.AddContact(new ContactPerson(
            Guid.NewGuid(), "Alpha Contact", "alpha@example.test", null, true));
        alpha.AddAddress(new OrganisationAddress(
            Guid.NewGuid(), "FreeTesting Address", null, "Raipur",
            "Chhattisgarh", "492001", "IN", true));

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
