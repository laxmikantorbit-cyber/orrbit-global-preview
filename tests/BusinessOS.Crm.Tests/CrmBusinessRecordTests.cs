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
    public void Invalid_Status_And_Dates_Are_Rejected()
    {
        var record = new CrmBusinessRecord(Guid.NewGuid(), TenantId, CrmBusinessModule.Ticket, "Support");
        Assert.Throws<ArgumentException>(() => record.ChangeStatus("Paid"));
        Assert.Throws<ArgumentException>(() => record.UpdateProfile("Bad", null, null, null, null,
            new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 21), null, null, null));
    }
}
