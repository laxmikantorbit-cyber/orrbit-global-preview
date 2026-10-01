using BusinessOS.Crm;

namespace BusinessOS.Crm.Tests;

public sealed class CrmBusinessRecordTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Theory]
    [InlineData(CrmBusinessModule.Expense, "Draft")]
    [InlineData(CrmBusinessModule.Contract, "Draft")]
    [InlineData(CrmBusinessModule.Project, "Planned")]
    [InlineData(CrmBusinessModule.Ticket, "Open")]
    public void Default_Status_Is_Module_Specific(CrmBusinessModule module, string status)
    {
        var record = new CrmBusinessRecord(Guid.NewGuid(), TenantId, module, "Test");
        Assert.Equal(status, record.Status);
    }

    [Fact]
    public void Profile_Normalizes_Amount_Dates_And_Metadata()
    {
        var record = new CrmBusinessRecord(
            Guid.NewGuid(), TenantId, CrmBusinessModule.Expense, "  Software cost  ",
            amount: 123.456m, category: " SaaS ", startDate: new DateOnly(2026, 9, 21),
            dueDate: new DateOnly(2026, 9, 30), metadata: new Dictionary<string, string> { [" Bill No "] = " 123 " });
        Assert.Equal("Software cost", record.Title);
        Assert.Equal(123.46m, record.Amount);
        Assert.Equal("SaaS", record.Category);
        Assert.Equal("123", record.Metadata["Bill No"]);
    }

    [Fact]
    public void Expense_Profile_Requires_Positive_Amount_Category_And_Date()
    {
        Assert.Throws<ArgumentException>(() =>
            CrmBusinessRecord.ValidateModuleProfile(CrmBusinessModule.Expense, null, null, "Travel", new DateOnly(2026, 10, 1), null));
        Assert.Throws<ArgumentException>(() =>
            CrmBusinessRecord.ValidateModuleProfile(CrmBusinessModule.Expense, null, 100m, " ", new DateOnly(2026, 10, 1), null));
        Assert.Throws<ArgumentException>(() =>
            CrmBusinessRecord.ValidateModuleProfile(CrmBusinessModule.Expense, null, 100m, "Travel", null, null));

        CrmBusinessRecord.ValidateModuleProfile(
            CrmBusinessModule.Expense, null, 100m, "Travel", new DateOnly(2026, 10, 1), null);
        CrmBusinessRecord.ValidateModuleProfile(
            CrmBusinessModule.Project, null, null, null, new DateOnly(2026, 10, 1), null);
    }

    [Fact]
    public void Contract_Profile_Requires_Customer_And_Start_Date()
    {
        var customerId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() =>
            CrmBusinessRecord.ValidateModuleProfile(CrmBusinessModule.Contract, null, 5000m, "AMC", new DateOnly(2026, 10, 1), null));
        Assert.Throws<ArgumentException>(() =>
            CrmBusinessRecord.ValidateModuleProfile(CrmBusinessModule.Contract, customerId, 5000m, "AMC", null, null));

        CrmBusinessRecord.ValidateModuleProfile(
            CrmBusinessModule.Contract, customerId, 5000m, "AMC", new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31));
    }

    [Fact]
    public void Project_Profile_Requires_Start_Date_And_Valid_Deadline()
    {
        Assert.Throws<ArgumentException>(() =>
            CrmBusinessRecord.ValidateModuleProfile(CrmBusinessModule.Project, null, 50000m, "Implementation", null, null));
        Assert.Throws<ArgumentException>(() =>
            CrmBusinessRecord.ValidateModuleProfile(
                CrmBusinessModule.Project, null, 50000m, "Implementation",
                new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 1)));

        CrmBusinessRecord.ValidateModuleProfile(
            CrmBusinessModule.Project, null, 50000m, "Implementation",
            new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31));
    }

    [Fact]
    public void Ticket_Profile_Requires_Department()
    {
        Assert.Throws<ArgumentException>(() =>
            CrmBusinessRecord.ValidateModuleProfile(
                CrmBusinessModule.Ticket, null, null, " ", new DateOnly(2026, 10, 1), null));

        CrmBusinessRecord.ValidateModuleProfile(
            CrmBusinessModule.Ticket, null, null, "Support", new DateOnly(2026, 10, 1), null);
    }

    [Fact]
    public void Invalid_Status_And_Dates_Are_Rejected()
    {
        var record = new CrmBusinessRecord(Guid.NewGuid(), TenantId, CrmBusinessModule.Ticket, "Support");
        Assert.Throws<ArgumentException>(() => record.ChangeStatus("Paid"));
        Assert.Throws<ArgumentException>(() => record.UpdateProfile("Bad", null, null, null, null,
            new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 21), null, null, null));
    }
}
