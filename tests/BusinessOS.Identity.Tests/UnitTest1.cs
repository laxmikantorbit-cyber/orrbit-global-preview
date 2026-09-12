using BusinessOS.Identity;

namespace BusinessOS.Identity.Tests;

public sealed class IdentityDirectoryTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Same_User_Can_Belong_To_Multiple_Tenants_With_Different_Roles()
    {
        var directory = CreateDirectory(
            Membership(TenantA, "OWNER"),
            Membership(TenantB, "TECHNICIAN"));

        Assert.Equal("OWNER", directory.ResolveAccess(UserId, TenantA)!.RoleCode);
        Assert.Equal("TECHNICIAN", directory.ResolveAccess(UserId, TenantB)!.RoleCode);
        Assert.Equal(2, directory.GetActiveMemberships(UserId).Count);
    }

    [Fact]
    public void Suspended_Membership_Denies_Access()
    {
        var directory = CreateDirectory(
            Membership(TenantA, "OWNER", MembershipStatus.Suspended));
        Assert.Null(directory.ResolveAccess(UserId, TenantA));
    }

    [Fact]
    public void Inactive_User_Denies_All_Tenant_Access()
    {
        var directory = CreateDirectory(Membership(TenantA, "OWNER"), userActive: false);
        Assert.Null(directory.ResolveAccess(UserId, TenantA));
    }

    [Fact]
    public void Suspended_Tenant_Denies_Access()
    {
        var directory = CreateDirectory(
            Membership(TenantA, "OWNER"),
            tenantAStatus: TenantStatus.Suspended);
        Assert.Null(directory.ResolveAccess(UserId, TenantA));
    }

    [Fact]
    public void Duplicate_Membership_For_Same_User_And_Tenant_Is_Rejected()
    {
        Assert.Throws<InvalidOperationException>(() => CreateDirectory(
            Membership(TenantA, "OWNER"),
            Membership(TenantA, "ADMIN")));
    }

    [Fact]
    public void Subject_Lookup_Is_Global_And_Exact()
    {
        var directory = CreateDirectory(Membership(TenantA, "OWNER"));
        Assert.Equal(UserId, directory.FindBySubject("oidc|user-1")!.Id);
        Assert.Null(directory.FindBySubject("OIDC|USER-1"));
    }

    private static IdentityDirectory CreateDirectory(
        TenantMembership membership,
        TenantMembership? second = null,
        bool userActive = true,
        TenantStatus tenantAStatus = TenantStatus.Active)
    {
        var tenants = new[]
        {
            new Tenant(TenantA, "TENANT-A", "Alpha", tenantAStatus),
            new Tenant(TenantB, "TENANT-B", "Beta", TenantStatus.Active)
        };
        var users = new[]
        {
            new UserIdentity(UserId, "oidc|user-1", "user@example.test", "User One", userActive)
        };
        var memberships = second is null
            ? new[] { membership }
            : new[] { membership, second };

        return new IdentityDirectory(tenants, users, memberships);
    }

    private static TenantMembership Membership(
        Guid tenantId,
        string role,
        MembershipStatus status = MembershipStatus.Active) =>
        new(Guid.NewGuid(), UserId, tenantId, role, status);
}
