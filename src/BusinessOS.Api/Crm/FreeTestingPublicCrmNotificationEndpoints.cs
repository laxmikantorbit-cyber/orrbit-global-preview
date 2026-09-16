using BusinessOS.Api.Commerce;
using BusinessOS.Crm;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmNotificationEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/notifications/inbox", async (
            bool? includeRead,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmWorkRepository work,
            ICrmOpportunityStore opportunities,
            ICrmNotificationStore store,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            await SyncAsync(member, work, opportunities, store, ct);
            var items = await store.ListAsync(DemoTenantId, member.Id, includeRead == true, ct);
            return Results.Ok(new CrmNotificationInboxResponse(
                items.Count(x => !x.IsRead), items.Count, items));
        });

        group.MapPost("/notifications/{notificationId:guid}/read", async (
            Guid notificationId,
            SetCrmNotificationReadRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmNotificationStore store,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var changed = await store.MarkReadAsync(DemoTenantId, member.Id, notificationId, request.IsRead, ct);
            return changed ? Results.Ok(new { notificationId, request.IsRead }) : Results.NotFound(new ErrorResponse("Notification not found."));
        });

        group.MapPost("/notifications/read-all", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmNotificationStore store,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var count = await store.MarkAllReadAsync(DemoTenantId, member.Id, ct);
            return Results.Ok(new { markedRead = count });
        });

        group.MapGet("/daily-summary", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ILeadRepository leads,
            ICrmWorkRepository work,
            ICrmOpportunityStore opportunities,
            ICrmNotificationStore store,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            await SyncAsync(member, work, opportunities, store, ct);
            var notifications = await store.ListAsync(DemoTenantId, member.Id, true, ct);
            var leadItems = await leads.ListAsync(DemoTenantId, ct);
            var followUps = await work.ListFollowUpsAsync(DemoTenantId, null, ct);
            var tasks = await work.ListTasksAsync(DemoTenantId, null, ct);
            var opps = await opportunities.ListAsync(DemoTenantId, ct);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
            {
                leadItems = leadItems.Where(x => x.Attribution.AccountOwnerUserId == member.Id).ToArray();
                followUps = followUps.Where(x => x.OwnerUserId == member.Id).ToArray();
                tasks = tasks.Where(x => x.AssigneeUserId == member.Id).ToArray();
                opps = opps.Where(x => x.OwnerUserId == member.Id).ToArray();
            }
            var now = DateTimeOffset.UtcNow;
            var todayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
            var tomorrow = todayStart.AddDays(1);
            var top = notifications.Where(x => x.Active && !x.IsRead)
                .OrderBy(x => x.Severity == "High" ? 0 : 1)
                .ThenBy(x => x.DueAtUtc ?? DateTimeOffset.MaxValue)
                .Take(7).ToArray();
            return Results.Ok(new CrmDailySummaryResponse(
                leadItems.Count(x => x.Status == LeadStatus.New),
                leadItems.Count(x => x.Status == LeadStatus.Qualified),
                followUps.Count(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc < now),
                followUps.Count(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc >= todayStart && x.DueAtUtc < tomorrow),
                tasks.Count(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc.HasValue && x.DueAtUtc.Value < now),
                tasks.Count(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc.HasValue && x.DueAtUtc.Value >= todayStart && x.DueAtUtc.Value < tomorrow),
                opps.Count(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost)),
                opps.Where(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost)).Sum(x => x.Forecast.EstimatedValue * x.Forecast.ProbabilityPercent / 100m),
                notifications.Count(x => !x.IsRead),
                top));
        });

        return app;
    }

    private static async Task SyncAsync(
        CrmTeamMember member,
        ICrmWorkRepository work,
        ICrmOpportunityStore opportunities,
        ICrmNotificationStore store,
        CancellationToken ct)
    {
        var followUps = await work.ListFollowUpsAsync(DemoTenantId, null, ct);
        var tasks = await work.ListTasksAsync(DemoTenantId, null, ct);
        var opps = await opportunities.ListAsync(DemoTenantId, ct);
        if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
        {
            followUps = followUps.Where(x => x.OwnerUserId == member.Id).ToArray();
            tasks = tasks.Where(x => x.AssigneeUserId == member.Id).ToArray();
            opps = opps.Where(x => x.OwnerUserId == member.Id).ToArray();
        }

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var nextWeek = today.AddDays(7);
        var generated = new List<CrmPersistentNotification>();
        void Add(string source, string type, string title, string detail, Guid? leadId, Guid? recordId, DateTimeOffset? due, string severity)
        {
            generated.Add(new CrmPersistentNotification(Guid.NewGuid(), DemoTenantId, member.Id, source, type, title, detail,
                leadId, recordId, due, severity, false, true, now, now));
        }

        foreach (var x in followUps.Where(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc <= now.AddDays(1)))
            Add($"followup:{x.Id}", x.DueAtUtc < now ? "FollowUpOverdue" : "FollowUpDue",
                x.DueAtUtc < now ? "Overdue follow-up" : "Follow-up due", x.Purpose, x.LeadId, x.Id, x.DueAtUtc,
                x.DueAtUtc < now ? "High" : "Normal");

        foreach (var x in tasks.Where(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc.HasValue && x.DueAtUtc.Value <= now.AddDays(1)))
            Add($"task:{x.Id}", x.DueAtUtc!.Value < now ? "TaskOverdue" : "TaskDue",
                x.DueAtUtc.Value < now ? "Overdue task" : "Task due", x.Title, x.LeadId, x.Id, x.DueAtUtc,
                x.DueAtUtc.Value < now ? "High" : "Normal");

        foreach (var x in opps.Where(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost) &&
                     x.Forecast.ExpectedCloseDate.HasValue && x.Forecast.ExpectedCloseDate.Value >= today &&
                     x.Forecast.ExpectedCloseDate.Value <= nextWeek))
            Add($"opportunity:{x.Id}", "OpportunityClosing", "Opportunity closing soon", x.Title, x.OriginatingLeadId, x.Id,
                new DateTimeOffset(x.Forecast.ExpectedCloseDate!.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), "Normal");

        foreach (var notification in generated) await store.UpsertAsync(notification, ct);
        await store.DeactivateMissingAsync(DemoTenantId, member.Id, generated.Select(x => x.SourceKey).ToArray(), ct);
    }

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);
    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
}

public sealed record SetCrmNotificationReadRequest(bool IsRead);
public sealed record CrmNotificationInboxResponse(int UnreadCount, int TotalCount, IReadOnlyList<CrmPersistentNotification> Items);
public sealed record CrmDailySummaryResponse(
    int NewLeads,
    int QualifiedLeads,
    int OverdueFollowUps,
    int DueTodayFollowUps,
    int OverdueTasks,
    int DueTodayTasks,
    int OpenOpportunities,
    decimal WeightedPipelineValue,
    int UnreadNotifications,
    IReadOnlyList<CrmPersistentNotification> TopActions);
