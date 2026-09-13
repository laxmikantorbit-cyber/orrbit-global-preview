using BusinessOS.Identity;

namespace BusinessOS.Api.Tenancy;

public static class PocIdentitySeed
{
    public static readonly Guid TenantAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid TenantBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static IdentityDirectory CreateDirectory()
    {
        var tenantA = new Tenant(TenantAId, "TENANT-A", "Tenant A", TenantStatus.Active);
        var tenantB = new Tenant(TenantBId, "TENANT-B", "Tenant B", TenantStatus.Active);

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
        return new IdentityDirectory(new[] { tenantA, tenantB }, new[] { userA, userB }, memberships);
    }
}
