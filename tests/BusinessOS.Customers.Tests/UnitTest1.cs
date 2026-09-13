using BusinessOS.Customers;

namespace BusinessOS.Customers.Tests;

public sealed class OrganisationTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Organisation_Requires_Tenant()
    {
        Assert.Throws<ArgumentException>(() =>
            new Organisation(Guid.NewGuid(), Guid.Empty, "Acme"));
    }

    [Fact]
    public void Organisation_Can_Have_Multiple_Business_Roles()
    {
        var organisation = NewOrganisation(TenantA, "Acme");
        organisation.AddRole(OrganisationRole.Customer);
        organisation.AddRole(OrganisationRole.Partner);

        Assert.True(organisation.HasRole(OrganisationRole.Customer));
        Assert.True(organisation.HasRole(OrganisationRole.Partner));
        Assert.Equal(2, organisation.Roles.Count);
    }
    [Fact]
    public void Duplicate_Role_Is_Idempotent()
    {
        var organisation = NewOrganisation(TenantA, "Acme");
        organisation.AddRole(OrganisationRole.Customer);
        organisation.AddRole(OrganisationRole.Customer);

        Assert.Single(organisation.Roles);
    }
    [Fact]
    public void New_Primary_Contact_Replaces_Old_Primary()
    {
        var organisation = NewOrganisation(TenantA, "Acme");
        organisation.AddContact(new ContactPerson(Guid.NewGuid(), "First", "one@test.local", "111", true));
        organisation.AddContact(new ContactPerson(Guid.NewGuid(), "Second", "two@test.local", "222", true));

        Assert.Equal("Second", organisation.PrimaryContact?.Name);
        Assert.Single(organisation.Contacts, x => x.IsPrimary);
    }
    [Fact]
    public void New_Primary_Address_Replaces_Old_Primary()
    {
        var organisation = NewOrganisation(TenantA, "Acme");
        organisation.AddAddress(NewAddress("Raipur", true));
        organisation.AddAddress(NewAddress("Bilaspur", true));

        Assert.Equal("Bilaspur", organisation.PrimaryAddress?.City);
        Assert.Single(organisation.Addresses, x => x.IsPrimary);
    }
    [Fact]
    public async Task Repository_Isolates_Tenants()
    {
        var a = NewOrganisation(TenantA, "Alpha");
        var b = NewOrganisation(TenantB, "Beta");
        var repository = new InMemoryOrganisationRepository(new[] { a, b });

        var list = await repository.ListAsync(TenantA);

        Assert.Single(list);
        Assert.Equal(a.Id, list[0].Id);
        Assert.Null(await repository.GetAsync(TenantA, b.Id));
    }

    [Fact]
    public async Task Repository_Filters_By_Business_Role()
    {
        var customer = NewOrganisation(TenantA, "Customer");
        customer.AddRole(OrganisationRole.Customer);
        var supplier = NewOrganisation(TenantA, "Supplier");
        supplier.AddRole(OrganisationRole.Supplier);
        var repository = new InMemoryOrganisationRepository(new[] { customer, supplier });

        var customers = await repository.ListByRoleAsync(TenantA, OrganisationRole.Customer);
        Assert.Single(customers);
        Assert.Equal(customer.Id, customers[0].Id);
    }
    [Fact]
    public async Task Duplicate_Internal_Id_Is_Rejected()
    {
        var id = Guid.NewGuid();
        var first = new Organisation(id, TenantA, "First");
        var second = new Organisation(id, TenantB, "Second");
        var repository = new InMemoryOrganisationRepository(new[] { first });

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddAsync(second));
    }

    [Fact]
    public async Task Contact_Email_Does_Not_Become_Organisation_Identity()
    {
        var first = NewOrganisation(TenantA, "First");
        var second = NewOrganisation(TenantA, "Second");
        first.AddContact(new ContactPerson(Guid.NewGuid(), "A", "shared@test.local", null, true));
        second.AddContact(new ContactPerson(Guid.NewGuid(), "B", "shared@test.local", null, true));
        var repository = new InMemoryOrganisationRepository(new[] { first, second });

        var list = await repository.ListAsync(TenantA);
        Assert.Equal(2, list.Count);
    }
    [Fact]
    public async Task Gstin_Is_Attribute_Not_Internal_Identity()
    {
        var first = new Organisation(Guid.NewGuid(), TenantA, "First", gstin: "22AAAAA0000A1Z5");
        var second = new Organisation(Guid.NewGuid(), TenantA, "Second", gstin: "22AAAAA0000A1Z5");
        var repository = new InMemoryOrganisationRepository(new[] { first, second });

        Assert.Equal(2, (await repository.ListAsync(TenantA)).Count);
    }

    private static Organisation NewOrganisation(Guid tenantId, string name) =>
        new(Guid.NewGuid(), tenantId, name);

    private static OrganisationAddress NewAddress(string city, bool primary) =>
        new(Guid.NewGuid(), "Main Road", null, city, "CG", "492001", "IN", primary);
}
