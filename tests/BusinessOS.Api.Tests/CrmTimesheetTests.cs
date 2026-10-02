using BusinessOS.Api.Crm;

namespace BusinessOS.Api.Tests;

public sealed class CrmTimesheetTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Entry_Validates_Duration_And_Activity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewEntry(minutes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewEntry(minutes: 1441));
        Assert.Throws<ArgumentException>(() => NewEntry(activity: " "));
    }

    [Fact]
    public void Workflow_Enforces_Submit_Review_And_Approved_Immutability()
    {
        var entry = NewEntry();
        entry.ChangeStatus("Submitted");
        entry.ChangeStatus("Approved");

        Assert.Equal("Approved", entry.Status);
        Assert.Throws<InvalidOperationException>(() =>
            entry.Update(UserId, new DateOnly(2026, 10, 2), 30, "Changed", true, null, null, null, null));
        Assert.Throws<InvalidOperationException>(() => entry.ChangeStatus("Rejected"));
    }

    [Fact]
    public void Rejected_Entry_Can_Be_Edited_And_Resubmitted()
    {
        var entry = NewEntry();
        entry.ChangeStatus("Submitted");
        entry.ChangeStatus("Rejected");
        entry.Update(UserId, new DateOnly(2026, 10, 2), 90, "Corrected work", false, null, null, null, "Updated");
        entry.ChangeStatus("Submitted");

        Assert.Equal("Submitted", entry.Status);
        Assert.Equal(90, entry.Minutes);
        Assert.Equal("Corrected work", entry.Activity);
        Assert.False(entry.Billable);
    }

    [Fact]
    public async Task InMemory_Store_Persists_Updates_And_Isolates_Tenants()
    {
        var store = new InMemoryCrmTimesheetStore();
        var entry = NewEntry();

        await store.AddAsync(entry);
        Assert.Single(await store.ListAsync(TenantId));
        Assert.Empty(await store.ListAsync(Guid.NewGuid()));

        entry.Update(UserId, new DateOnly(2026, 10, 2), 120, "Implementation", true, null, null, null, "QA");
        await store.SaveAsync(entry);

        var loaded = await store.GetAsync(TenantId, entry.Id);
        Assert.NotNull(loaded);
        Assert.Equal(120, loaded!.Minutes);
        Assert.Equal(new DateOnly(2026, 10, 2), loaded.WorkDate);
    }

    [Fact]
    public void Task_Timer_Stop_Records_Duration_And_Cannot_Stop_Twice()
    {
        var started = new DateTimeOffset(2026, 10, 2, 6, 0, 0, TimeSpan.Zero);
        var timer = new CrmTaskTimerEntry(Guid.NewGuid(), TenantId, Guid.NewGuid(), UserId, started, "QA");

        timer.Stop(started.AddMinutes(12).AddSeconds(5));

        Assert.False(timer.IsRunning);
        Assert.Equal(725, timer.DurationSeconds);
        Assert.Throws<InvalidOperationException>(() => timer.Stop(started.AddMinutes(13)));
    }

    [Fact]
    public void Task_Timer_Rejects_Stop_Before_Start()
    {
        var started = new DateTimeOffset(2026, 10, 2, 6, 0, 0, TimeSpan.Zero);
        var timer = new CrmTaskTimerEntry(Guid.NewGuid(), TenantId, Guid.NewGuid(), UserId, started);

        Assert.Throws<ArgumentException>(() => timer.Stop(started.AddSeconds(-1)));
        Assert.True(timer.IsRunning);
    }

    [Fact]
    public async Task Task_Timer_Store_Allows_Multiple_Running_And_Isolates_Tenants()
    {
        var store = new InMemoryCrmTaskTimerStore();
        await store.AddAsync(new CrmTaskTimerEntry(Guid.NewGuid(), TenantId, Guid.NewGuid(), UserId, DateTimeOffset.UtcNow));
        await store.AddAsync(new CrmTaskTimerEntry(Guid.NewGuid(), TenantId, Guid.NewGuid(), UserId, DateTimeOffset.UtcNow));

        Assert.Equal(2, (await store.ListAsync(TenantId)).Count);
        Assert.Empty(await store.ListAsync(Guid.NewGuid()));
    }

    private static CrmTimesheetEntry NewEntry(int minutes = 60, string activity = "Customer demo") =>
        new(Guid.NewGuid(), TenantId, UserId, new DateOnly(2026, 10, 1), minutes, activity, true);
}
