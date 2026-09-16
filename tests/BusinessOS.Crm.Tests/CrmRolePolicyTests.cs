using BusinessOS.Crm;

namespace BusinessOS.Crm.Tests;

public sealed class CrmRolePolicyTests
{
    [Fact]
    public void Owner_And_Admin_Have_All_Crm_Permissions()
    {
        var all = Enum.GetValues<CrmPermission>();
        Assert.All(all, permission => Assert.True(CrmRolePolicy.Allows(CrmRoleCode.Owner, permission)));
        Assert.All(all, permission => Assert.True(CrmRolePolicy.Allows(CrmRoleCode.Admin, permission)));
    }

    [Fact]
    public void Sales_Manager_Can_Assign_But_Cannot_Manage_Team()
    {
        Assert.True(CrmRolePolicy.Allows(CrmRoleCode.SalesManager, CrmPermission.AssignLead));
        Assert.True(CrmRolePolicy.Allows(CrmRoleCode.SalesManager, CrmPermission.ExportData));
        Assert.False(CrmRolePolicy.Allows(CrmRoleCode.SalesManager, CrmPermission.ManageTeam));
    }

    [Fact]
    public void Sales_Executive_Cannot_Assign_Other_Users_Or_Manage_Team()
    {
        Assert.True(CrmRolePolicy.Allows(CrmRoleCode.SalesExecutive, CrmPermission.CreateLead));
        Assert.True(CrmRolePolicy.Allows(CrmRoleCode.SalesExecutive, CrmPermission.ManageOpportunities));
        Assert.False(CrmRolePolicy.Allows(CrmRoleCode.SalesExecutive, CrmPermission.AssignLead));
        Assert.False(CrmRolePolicy.Allows(CrmRoleCode.SalesExecutive, CrmPermission.ManageTeam));
    }    [Fact]
    public void Telecaller_Can_Work_Leads_But_Cannot_View_Accounts()
    {
        Assert.True(CrmRolePolicy.Allows(CrmRoleCode.Telecaller, CrmPermission.ViewLeads));
        Assert.True(CrmRolePolicy.Allows(CrmRoleCode.Telecaller, CrmPermission.ManageFollowUps));
        Assert.True(CrmRolePolicy.Allows(CrmRoleCode.Telecaller, CrmPermission.ManageTasks));
        Assert.False(CrmRolePolicy.Allows(CrmRoleCode.Telecaller, CrmPermission.ViewAccounts));
        Assert.False(CrmRolePolicy.Allows(CrmRoleCode.Telecaller, CrmPermission.ManageOpportunities));
    }

    [Fact]
    public void Viewer_Is_Read_Only()
    {
        Assert.True(CrmRolePolicy.Allows(CrmRoleCode.Viewer, CrmPermission.ViewDashboard));
        Assert.True(CrmRolePolicy.Allows(CrmRoleCode.Viewer, CrmPermission.ViewLeads));
        Assert.True(CrmRolePolicy.Allows(CrmRoleCode.Viewer, CrmPermission.ViewReports));
        Assert.False(CrmRolePolicy.Allows(CrmRoleCode.Viewer, CrmPermission.CreateLead));
        Assert.False(CrmRolePolicy.Allows(CrmRoleCode.Viewer, CrmPermission.EditLead));
        Assert.False(CrmRolePolicy.Allows(CrmRoleCode.Viewer, CrmPermission.ManageTasks));
        Assert.False(CrmRolePolicy.Allows(CrmRoleCode.Viewer, CrmPermission.ManageTeam));
    }
}
