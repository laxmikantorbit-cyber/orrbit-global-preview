using BusinessOS.Identity;

namespace BusinessOS.Api.Tenancy;

public static class PocIdentitySeed
{
    public static IdentityDirectory CreateDirectory()
    {
        var tenantA = new Tenant(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "TENANT-A", "Tenant A", TenantStatus.Active);
        var tenantB = new Tenant(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "TENANT-B", "Tenant B", TenantStatus.Active);

        var userA = new UserIdentity(
            Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
            "poc-user-a", "a@example.test", "POC User A", true);
        var userB = new UserIdentity(
            Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222"),
            "poc-user-b", "b@example.test", "POC User B", true);

        var memberships = new[]
        {
            new TenantMembership(Guid.NewGuid(), userA.Id, tenantA.Id, "Owner", MembershipStatus.Active),
            new TenantMembership(Guid.NewGuid(), userB.Id, tenantB.Id, "Owner", MembershipStatus.Active)
        };

        return new IdentityDirectory(
            new[] { tenantA, tenantB },
            new[] { userA, userB },
            memberships);
    }
}
