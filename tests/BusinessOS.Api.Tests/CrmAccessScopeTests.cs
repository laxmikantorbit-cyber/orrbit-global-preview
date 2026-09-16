using BusinessOS.Api.Crm;
using BusinessOS.Crm;

namespace BusinessOS.Api.Tests;

public sealed class CrmAccessScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData(CrmRoleCode.Owner)]
    [InlineData(CrmRoleCode.Admin)]
    [InlineData(CrmRoleCode.SalesManager)]
    public void Management_Roles_Have_Team_Scope(CrmRoleCode role)
    {
        var member = Member(Guid.NewGuid(), role);
        Assert.True(CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member));
        Assert.True(CrmFreeTestingAccessMiddleware.CanAccessLead(member, LeadOwnedBy(Guid.NewGuid())));
    }

    [Theory]
    [InlineData(CrmRoleCode.SalesExecutive)]
    [InlineData(CrmRoleCode.Telecaller)]
    [InlineData(CrmRoleCode.Support)]
    [InlineData(CrmRoleCode.Viewer)]
    public void Non_Management_Roles_Use_Owned_Record_Scope(CrmRoleCode role)
    {
        var member = Member(Guid.NewGuid(), role);
        Assert.False(CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member));
    }

    [Fact]
    public void Sales_Executive_Can_Access_Only_Own_Lead()
    {
        var userId = Guid.NewGuid();
        var member = Member(userId, CrmRoleCode.SalesExecutive);

        Assert.True(CrmFreeTestingAccessMiddleware.CanAccessLead(member, LeadOwnedBy(userId)));
        Assert.False(CrmFreeTestingAccessMiddleware.CanAccessLead(member, LeadOwnedBy(Guid.NewGuid())));
        Assert.False(CrmFreeTestingAccessMiddleware.CanAccessLead(member, LeadOwnedBy(null)));
    }

    private static CrmTeamMember Member(Guid id, CrmRoleCode role) =>
        new(id, TenantId, role.ToString(), $"{id:N}@qa.test", null, role);

    private static Lead LeadOwnedBy(Guid? ownerUserId) => new(
        Guid.NewGuid(),
        TenantId,
        Guid.NewGuid(),
        "RBAC lead",
        new LeadAttribution("QA", null, null, null, ownerUserId, null));
}
