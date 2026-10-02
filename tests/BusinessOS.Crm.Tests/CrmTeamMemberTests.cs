namespace BusinessOS.Crm.Tests;

public sealed class CrmTeamMemberTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void UpdateProfile_Trims_And_Normalizes_Values()
    {
        var member = NewMember("Owner", "OWNER@EXAMPLE.COM", " 9999999999 ");

        member.UpdateProfile("  Updated Owner  ", " NEW@Example.COM ", " 8888888888 ");

        Assert.Equal("Updated Owner", member.DisplayName);
        Assert.Equal("new@example.com", member.Email);
        Assert.Equal("8888888888", member.MobileNumber);
    }

    [Fact]
    public void UpdateProfile_Rejects_Blank_Name_Or_Email()
    {
        var member = NewMember("Owner", "owner@example.com");

        Assert.Throws<ArgumentException>(() => member.UpdateProfile(" ", "owner@example.com", null));
        Assert.Throws<ArgumentException>(() => member.UpdateProfile("Owner", " ", null));
    }

    [Fact]
    public async Task Repository_Save_Rejects_Duplicate_Email_In_Same_Tenant()
    {
        var first = NewMember("First", "first@example.com");
        var second = NewMember("Second", "second@example.com");
        var repository = new InMemoryCrmTeamRepository([first, second]);

        second.UpdateProfile("Second", "FIRST@example.com", null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(second));
        Assert.Contains("email already exists", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repository_Allows_Same_Email_In_Different_Tenants()
    {
        var first = NewMember("First", "same@example.com");
        var second = new CrmTeamMember(Guid.NewGuid(), Guid.NewGuid(), "Second", "same@example.com", null, CrmRoleCode.Admin);
        var repository = new InMemoryCrmTeamRepository([first, second]);

        second.UpdateProfile("Second Updated", "same@example.com", null);
        await repository.SaveAsync(second);

        Assert.Equal("same@example.com", second.Email);
    }

    private static CrmTeamMember NewMember(string name, string email, string? mobile = null) =>
        new(Guid.NewGuid(), TenantId, name, email, mobile, CrmRoleCode.Owner);
}
