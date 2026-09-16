using BusinessOS.Api.Commerce;
using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmOperationsEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/leads/{leadId:guid}/workspace", async (
            Guid leadId,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILeadRepository leads,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var lead = await leads.GetAsync(DemoTenantId, leadId, cancellationToken);
            if (lead is null) return Results.NotFound(new ErrorResponse("Lead not found."));
            var activities = await work.ListActivitiesAsync(DemoTenantId, leadId, cancellationToken);
            var followUps = await work.ListFollowUpsAsync(DemoTenantId, leadId, cancellationToken);
            var tasks = await work.ListTasksAsync(DemoTenantId, leadId, cancellationToken);            return Results.Ok(new LeadWorkspaceResponse(
                ToLead(lead),
                activities.Select(ToActivity).ToArray(),
                followUps.Select(ToFollowUp).ToArray(),
                tasks.Select(ToTask).ToArray()));
        });

        group.MapPost("/leads/{leadId:guid}/profile", async (
            Guid leadId,
            UpdateLeadProfileRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILeadRepository leads,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var lead = await leads.GetAsync(DemoTenantId, leadId, cancellationToken);
            if (lead is null) return Results.NotFound(new ErrorResponse("Lead not found."));
            try
            {
                lead.UpdateProfile(request.Title, request.ContactName, request.MobileNumber,
                    request.Email, request.ProductInterest, request.Notes);
                await AddActivity(work, leadId, CrmActivityType.ProfileUpdated,
                    "Lead profile updated", null, cancellationToken);
                return Results.Ok(ToLead(lead));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });
        group.MapPost("/leads/{leadId:guid}/assign", async (
            Guid leadId,
            AssignLeadRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILeadRepository leads,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var lead = await leads.GetAsync(DemoTenantId, leadId, cancellationToken);
            if (lead is null) return Results.NotFound(new ErrorResponse("Lead not found."));
            try
            {
                lead.AssignOwner(request.OwnerUserId);
                await AddActivity(work, leadId, CrmActivityType.AssignmentChanged,
                    request.OwnerUserId.HasValue ? "Lead assigned" : "Lead unassigned",
                    request.OwnerUserId?.ToString(), cancellationToken);
                return Results.Ok(ToLead(lead));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/leads/{leadId:guid}/priority", async (
            Guid leadId,
            ChangeLeadPriorityRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILeadRepository leads,
            CancellationToken cancellationToken) =>        {
            if (!Enabled(configuration, environment)) return Disabled();
            var lead = await leads.GetAsync(DemoTenantId, leadId, cancellationToken);
            if (lead is null) return Results.NotFound(new ErrorResponse("Lead not found."));
            if (!Enum.TryParse<LeadPriority>(request.Priority, true, out var priority))
                return Results.BadRequest(new ErrorResponse("Valid priority is required."));
            try
            {
                lead.SetPriority(priority);
                return Results.Ok(ToLead(lead));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/leads/{leadId:guid}/activities", async (
            Guid leadId,
            AddLeadActivityRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILeadRepository leads,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var lead = await leads.GetAsync(DemoTenantId, leadId, cancellationToken);
            if (lead is null) return Results.NotFound(new ErrorResponse("Lead not found."));            if (!Enum.TryParse<CrmActivityType>(request.Type, true, out var type))
                return Results.BadRequest(new ErrorResponse("Valid activity type is required."));
            try
            {
                var activity = new LeadActivity(Guid.NewGuid(), DemoTenantId, leadId, type,
                    request.Summary, request.Details, request.ActorUserId);
                await work.AddActivityAsync(activity, cancellationToken);
                if (type is CrmActivityType.Call or CrmActivityType.WhatsApp or CrmActivityType.Email or CrmActivityType.Meeting)
                    lead.RecordContact(activity.OccurredAtUtc);
                return Results.Ok(ToActivity(activity));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/leads/{leadId:guid}/follow-ups", async (
            Guid leadId,
            CreateFollowUpRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILeadRepository leads,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var lead = await leads.GetAsync(DemoTenantId, leadId, cancellationToken);
            if (lead is null) return Results.NotFound(new ErrorResponse("Lead not found."));            if (!Enum.TryParse<FollowUpChannel>(request.Channel, true, out var channel))
                return Results.BadRequest(new ErrorResponse("Valid follow-up channel is required."));
            try
            {
                var followUp = new LeadFollowUp(Guid.NewGuid(), DemoTenantId, leadId,
                    request.DueAtUtc, channel, request.Purpose, request.OwnerUserId);
                await work.AddFollowUpAsync(followUp, cancellationToken);
                lead.ScheduleNextFollowUp(request.DueAtUtc);
                await AddActivity(work, leadId, CrmActivityType.FollowUpScheduled,
                    $"{channel} follow-up scheduled", request.Purpose, cancellationToken);
                return Results.Ok(ToFollowUp(followUp));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapGet("/follow-ups", async (
            Guid? leadId,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var items = await work.ListFollowUpsAsync(DemoTenantId, leadId, cancellationToken);
            return Results.Ok(new { followUps = items.Select(ToFollowUp).ToArray() });
        });
        group.MapPost("/follow-ups/{followUpId:guid}/complete", async (
            Guid followUpId,
            CompleteFollowUpRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILeadRepository leads,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var followUp = await work.GetFollowUpAsync(DemoTenantId, followUpId, cancellationToken);
            if (followUp is null) return Results.NotFound(new ErrorResponse("Follow-up not found."));
            try
            {
                followUp.Complete(request.Outcome);
                var lead = await leads.GetAsync(DemoTenantId, followUp.LeadId, cancellationToken);
                lead?.RecordContact(followUp.CompletedAtUtc);
                if (lead is not null)
                {
                    var open = (await work.ListFollowUpsAsync(DemoTenantId, lead.Id, cancellationToken))
                        .Where(x => x.Status == CrmWorkStatus.Open).OrderBy(x => x.DueAtUtc).FirstOrDefault();
                    lead.ScheduleNextFollowUp(open?.DueAtUtc);
                }
                await AddActivity(work, followUp.LeadId, CrmActivityType.FollowUpCompleted,
                    "Follow-up completed", request.Outcome, cancellationToken);
                return Results.Ok(ToFollowUp(followUp));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });
        group.MapGet("/tasks", async (
            Guid? leadId,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var items = await work.ListTasksAsync(DemoTenantId, leadId, cancellationToken);
            return Results.Ok(new { tasks = items.Select(ToTask).ToArray() });
        });

        group.MapPost("/tasks", async (
            CreateCrmTaskRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILeadRepository leads,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            if (request.LeadId.HasValue && await leads.GetAsync(DemoTenantId, request.LeadId.Value, cancellationToken) is null)
                return Results.NotFound(new ErrorResponse("Lead not found."));
            if (!Enum.TryParse<LeadPriority>(request.Priority ?? "Normal", true, out var priority))
                return Results.BadRequest(new ErrorResponse("Valid task priority is required."));
            try
            {
                var task = new CrmTask(Guid.NewGuid(), DemoTenantId, request.Title, request.DueAtUtc,
                    request.LeadId, request.Details, priority, request.AssigneeUserId);                await work.AddTaskAsync(task, cancellationToken);
                if (request.LeadId.HasValue)
                    await AddActivity(work, request.LeadId.Value, CrmActivityType.TaskCreated,
                        "Task created", request.Title, cancellationToken);
                return Results.Ok(ToTask(task));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/tasks/{taskId:guid}/complete", async (
            Guid taskId,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var task = await work.GetTaskAsync(DemoTenantId, taskId, cancellationToken);
            if (task is null) return Results.NotFound(new ErrorResponse("Task not found."));
            try
            {
                task.Complete();
                if (task.LeadId.HasValue)
                    await AddActivity(work, task.LeadId.Value, CrmActivityType.TaskCompleted,
                        "Task completed", task.Title, cancellationToken);
                return Results.Ok(ToTask(task));
            }            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapGet("/work-summary", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var now = DateTimeOffset.UtcNow;
            var followUps = await work.ListFollowUpsAsync(DemoTenantId, null, cancellationToken);
            var tasks = await work.ListTasksAsync(DemoTenantId, null, cancellationToken);
            var openFollowUps = followUps.Where(x => x.Status == CrmWorkStatus.Open).ToArray();
            var openTasks = tasks.Where(x => x.Status == CrmWorkStatus.Open).ToArray();
            return Results.Ok(new CrmWorkSummaryResponse(
                openFollowUps.Length,
                openFollowUps.Count(x => x.DueAtUtc < now),
                openFollowUps.Count(x => x.DueAtUtc.UtcDateTime.Date == now.UtcDateTime.Date),
                openTasks.Length,
                openTasks.Count(x => x.DueAtUtc.HasValue && x.DueAtUtc.Value < now)));
        });

        return app;
    }
    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() =>
        Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));

    private static async Task AddActivity(
        ICrmWorkRepository work,
        Guid leadId,
        CrmActivityType type,
        string summary,
        string? details,
        CancellationToken cancellationToken)
    {
        await work.AddActivityAsync(new LeadActivity(
            Guid.NewGuid(), DemoTenantId, leadId, type, summary, details), cancellationToken);
    }

    private static CrmLeadDetailResponse ToLead(Lead lead) => new(
        lead.Id,
        lead.OrganisationId,
        lead.Title,
        lead.ContactName,
        lead.MobileNumber,
        lead.Email,
        lead.ProductInterest,        lead.Notes,
        lead.Status.ToString(),
        lead.Priority.ToString(),
        lead.Attribution.LeadSource,
        lead.Attribution.AccountOwnerUserId,
        lead.UnqualifiedReason,
        lead.CreatedAtUtc,
        lead.UpdatedAtUtc,
        lead.LastContactAtUtc,
        lead.NextFollowUpAtUtc,
        lead.Tags.ToArray());

    private static CrmActivityResponse ToActivity(LeadActivity x) => new(
        x.Id, x.LeadId, x.Type.ToString(), x.Summary, x.Details, x.ActorUserId, x.OccurredAtUtc);

    private static CrmFollowUpResponse ToFollowUp(LeadFollowUp x) => new(
        x.Id, x.LeadId, x.DueAtUtc, x.Channel.ToString(), x.Purpose,
        x.OwnerUserId, x.Status.ToString(), x.Outcome, x.CreatedAtUtc, x.CompletedAtUtc);

    private static CrmTaskResponse ToTask(CrmTask x) => new(
        x.Id, x.LeadId, x.Title, x.Details, x.DueAtUtc, x.Priority.ToString(),
        x.AssigneeUserId, x.Status.ToString(), x.CreatedAtUtc, x.CompletedAtUtc);
}

public sealed record UpdateLeadProfileRequest(
    string Title,
    string? ContactName,    string? MobileNumber,
    string? Email,
    string? ProductInterest,
    string? Notes);

public sealed record AssignLeadRequest(Guid? OwnerUserId);
public sealed record ChangeLeadPriorityRequest(string Priority);

public sealed record AddLeadActivityRequest(
    string Type,
    string Summary,
    string? Details,
    Guid? ActorUserId);

public sealed record CreateFollowUpRequest(
    DateTimeOffset DueAtUtc,
    string Channel,
    string Purpose,
    Guid? OwnerUserId);

public sealed record CompleteFollowUpRequest(string? Outcome);

public sealed record CreateCrmTaskRequest(
    string Title,
    Guid? LeadId,
    string? Details,
    DateTimeOffset? DueAtUtc,
    string? Priority,
    Guid? AssigneeUserId);
public sealed record CrmLeadDetailResponse(
    Guid Id,
    Guid OrganisationId,
    string Title,
    string? ContactName,
    string? MobileNumber,
    string? Email,
    string? ProductInterest,
    string? Notes,
    string Status,
    string Priority,
    string? LeadSource,
    Guid? OwnerUserId,
    string? UnqualifiedReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastContactAtUtc,
    DateTimeOffset? NextFollowUpAtUtc,
    IReadOnlyCollection<string> Tags);

public sealed record CrmActivityResponse(
    Guid Id,
    Guid LeadId,
    string Type,
    string Summary,
    string? Details,
    Guid? ActorUserId,
    DateTimeOffset OccurredAtUtc);
public sealed record CrmFollowUpResponse(
    Guid Id,
    Guid LeadId,
    DateTimeOffset DueAtUtc,
    string Channel,
    string Purpose,
    Guid? OwnerUserId,
    string Status,
    string? Outcome,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record CrmTaskResponse(
    Guid Id,
    Guid? LeadId,
    string Title,
    string? Details,
    DateTimeOffset? DueAtUtc,
    string Priority,
    Guid? AssigneeUserId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record LeadWorkspaceResponse(
    CrmLeadDetailResponse Lead,
    IReadOnlyList<CrmActivityResponse> Activities,
    IReadOnlyList<CrmFollowUpResponse> FollowUps,
    IReadOnlyList<CrmTaskResponse> Tasks);

public sealed record CrmWorkSummaryResponse(
    int OpenFollowUps,
    int OverdueFollowUps,
    int DueTodayFollowUps,
    int OpenTasks,
    int OverdueTasks);
