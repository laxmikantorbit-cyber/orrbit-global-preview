using BusinessOS.Crm;

namespace BusinessOS.Crm.Tests;

public sealed class CrmWorkTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Lead_Stores_Operational_Profile_And_Priority()
    {
        var lead = NewLead();
        lead.UpdateProfile("Acme Repair", "Ravi", "9999999999", "ravi@example.com", "AI Repair", "Demo requested");
        lead.SetPriority(LeadPriority.High);
        lead.AddTag("Hot");

        Assert.Equal("Ravi", lead.ContactName);
        Assert.Equal("9999999999", lead.MobileNumber);
        Assert.Equal("AI Repair", lead.ProductInterest);
        Assert.Equal(LeadPriority.High, lead.Priority);
        Assert.Contains("Hot", lead.Tags);
    }

    [Fact]
    public void Contact_And_FollowUp_Dates_Are_Tracked()
    {
        var lead = NewLead();        var contacted = DateTimeOffset.UtcNow.AddMinutes(-5);
        var due = DateTimeOffset.UtcNow.AddDays(1);
        lead.RecordContact(contacted);
        lead.ScheduleNextFollowUp(due);

        Assert.Equal(LeadStatus.Contacted, lead.Status);
        Assert.Equal(contacted, lead.LastContactAtUtc);
        Assert.Equal(due, lead.NextFollowUpAtUtc);
    }

    [Fact]
    public void Closed_Lead_Can_Be_Reopened()
    {
        var lead = NewLead();
        lead.MarkUnqualified("No budget");
        lead.Reopen(LeadStatus.Contacted);

        Assert.Equal(LeadStatus.Contacted, lead.Status);
        Assert.Null(lead.UnqualifiedReason);
    }

    [Fact]
    public void FollowUp_Completes_With_Outcome()
    {
        var item = new LeadFollowUp(Guid.NewGuid(), TenantA, Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(2), FollowUpChannel.WhatsApp, "Share demo");
        item.Complete("Demo link sent");

        Assert.Equal(CrmWorkStatus.Completed, item.Status);        Assert.Equal("Demo link sent", item.Outcome);
        Assert.NotNull(item.CompletedAtUtc);
        Assert.Throws<InvalidOperationException>(() => item.Complete());
    }

    [Fact]
    public void Task_Completes_Only_Once()
    {
        var task = new CrmTask(Guid.NewGuid(), TenantA, "Call lead",
            DateTimeOffset.UtcNow.AddHours(1), priority: LeadPriority.Urgent);
        task.Complete();

        Assert.Equal(CrmWorkStatus.Completed, task.Status);
        Assert.Throws<InvalidOperationException>(() => task.Cancel());
    }

    [Fact]
    public async Task Work_Repository_Isolates_Tenants_And_Leads()
    {
        var repository = new InMemoryCrmWorkRepository();
        var leadA = Guid.NewGuid();
        var leadB = Guid.NewGuid();
        await repository.AddActivityAsync(new LeadActivity(Guid.NewGuid(), TenantA, leadA,
            CrmActivityType.Note, "A note"));
        await repository.AddActivityAsync(new LeadActivity(Guid.NewGuid(), TenantA, leadB,
            CrmActivityType.Note, "B note"));
        await repository.AddActivityAsync(new LeadActivity(Guid.NewGuid(), TenantB, leadA,
            CrmActivityType.Note, "Other tenant"));
        var items = await repository.ListActivitiesAsync(TenantA, leadA);
        Assert.Single(items);
        Assert.Equal("A note", items[0].Summary);
    }

    [Fact]
    public async Task FollowUps_And_Tasks_Can_Be_Listed_Per_Lead()
    {
        var repository = new InMemoryCrmWorkRepository();
        var leadId = Guid.NewGuid();
        await repository.AddFollowUpAsync(new LeadFollowUp(Guid.NewGuid(), TenantA, leadId,
            DateTimeOffset.UtcNow.AddDays(1), FollowUpChannel.Call, "Check decision"));
        await repository.AddTaskAsync(new CrmTask(Guid.NewGuid(), TenantA, "Prepare proposal",
            DateTimeOffset.UtcNow.AddHours(4), leadId));

        Assert.Single(await repository.ListFollowUpsAsync(TenantA, leadId));
        Assert.Single(await repository.ListTasksAsync(TenantA, leadId));
    }

    private static Lead NewLead() => new(
        Guid.NewGuid(),
        TenantA,
        Guid.NewGuid(),
        "Repair Software",
        new LeadAttribution("WhatsApp", null, null, null, null, null));
}
