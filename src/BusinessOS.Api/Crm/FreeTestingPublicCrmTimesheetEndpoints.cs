using BusinessOS.Api.Commerce;
using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmTimesheetEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmTimesheetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/timesheets", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmTimesheetStore timesheets,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var current = CrmFreeTestingAccessMiddleware.Current(context);
            var items = await timesheets.ListAsync(DemoTenantId, cancellationToken);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(current))
                items = items.Where(x => x.UserId == current.Id).ToArray();
            return Results.Ok(new { timesheets = items.Select(ToResponse).ToArray() });
        });

        group.MapPost("/timesheets", async (
            CreateCrmTimesheetRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmTimesheetStore timesheets,
            ICrmTeamRepository team,
            ICrmBusinessRecordStore businessRecords,
            ICrmWorkRepository work,
            ICrmAccountStore accounts,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var current = CrmFreeTestingAccessMiddleware.Current(context);
            var userId = request.UserId ?? current.Id;
            if (userId != current.Id && !CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(current))
                return Results.Forbid();
            var validation = await ValidateLinksAsync(
                userId, request.ProjectId, request.TaskId, request.AccountId,
                team, businessRecords, work, accounts, cancellationToken);
            if (validation is not null) return validation;

            try
            {
                var entry = new CrmTimesheetEntry(
                    Guid.NewGuid(), DemoTenantId, userId, request.WorkDate, request.Minutes,
                    request.Activity, request.Billable, request.ProjectId, request.TaskId,
                    request.AccountId, request.Notes);
                await timesheets.AddAsync(entry, cancellationToken);
                await AuditAsync(management, context, "TimesheetCreated", entry.Id,
                    $"user={entry.UserId}; date={entry.WorkDate:yyyy-MM-dd}; minutes={entry.Minutes}; billable={entry.Billable}",
                    cancellationToken);
                return Results.Ok(ToResponse(entry));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/timesheets/{entryId:guid}/profile", async (
            Guid entryId,
            UpdateCrmTimesheetRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmTimesheetStore timesheets,
            ICrmTeamRepository team,
            ICrmBusinessRecordStore businessRecords,
            ICrmWorkRepository work,
            ICrmAccountStore accounts,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var entry = await timesheets.GetAsync(DemoTenantId, entryId, cancellationToken);
            if (entry is null) return Results.NotFound(new ErrorResponse("Timesheet entry not found."));
            var current = CrmFreeTestingAccessMiddleware.Current(context);
            var canReview = CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(current);
            if (!canReview && (entry.UserId != current.Id || request.UserId != current.Id))
                return Results.Forbid();
            var validation = await ValidateLinksAsync(
                request.UserId, request.ProjectId, request.TaskId, request.AccountId,
                team, businessRecords, work, accounts, cancellationToken);
            if (validation is not null) return validation;

            try
            {
                entry.Update(
                    request.UserId, request.WorkDate, request.Minutes, request.Activity,
                    request.Billable, request.ProjectId, request.TaskId, request.AccountId, request.Notes);
                await timesheets.SaveAsync(entry, cancellationToken);
                await AuditAsync(management, context, "TimesheetUpdated", entry.Id,
                    $"date={entry.WorkDate:yyyy-MM-dd}; minutes={entry.Minutes}; billable={entry.Billable}",
                    cancellationToken);
                return Results.Ok(ToResponse(entry));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/timesheets/{entryId:guid}/status", async (
            Guid entryId,
            ChangeCrmTimesheetStatusRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmTimesheetStore timesheets,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var entry = await timesheets.GetAsync(DemoTenantId, entryId, cancellationToken);
            if (entry is null) return Results.NotFound(new ErrorResponse("Timesheet entry not found."));
            var current = CrmFreeTestingAccessMiddleware.Current(context);
            var canReview = CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(current);
            var normalizedStatus = request.Status?.Trim();
            if (!canReview && (entry.UserId != current.Id || !string.Equals(normalizedStatus, "Submitted", StringComparison.OrdinalIgnoreCase)))
                return Results.Forbid();

            try
            {
                entry.ChangeStatus(request.Status ?? string.Empty);
                await timesheets.SaveAsync(entry, cancellationToken);
                await AuditAsync(management, context, "TimesheetStatusChanged", entry.Id,
                    $"status={entry.Status}", cancellationToken);
                return Results.Ok(ToResponse(entry));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        return app;
    }

    private static async Task<IResult?> ValidateLinksAsync(
        Guid userId,
        Guid? projectId,
        Guid? taskId,
        Guid? accountId,
        ICrmTeamRepository team,
        ICrmBusinessRecordStore businessRecords,
        ICrmWorkRepository work,
        ICrmAccountStore accounts,
        CancellationToken cancellationToken)
    {
        var member = await team.GetAsync(DemoTenantId, userId, cancellationToken);
        if (member is null || !member.Active)
            return Results.BadRequest(new ErrorResponse("Timesheet user was not found or is inactive."));

        if (projectId.HasValue)
        {
            var project = await businessRecords.GetAsync(DemoTenantId, projectId.Value, cancellationToken);
            if (project is null || project.Module != CrmBusinessModule.Project)
                return Results.BadRequest(new ErrorResponse("Timesheet project was not found."));
        }

        if (taskId.HasValue && await work.GetTaskAsync(DemoTenantId, taskId.Value, cancellationToken) is null)
            return Results.BadRequest(new ErrorResponse("Timesheet task was not found."));

        if (accountId.HasValue && await accounts.GetAsync(DemoTenantId, accountId.Value, cancellationToken) is null)
            return Results.BadRequest(new ErrorResponse("Timesheet customer was not found."));

        return null;
    }

    private static CrmTimesheetResponse ToResponse(CrmTimesheetEntry entry) => new(
        entry.Id, entry.UserId, entry.ProjectId, entry.TaskId, entry.AccountId,
        entry.WorkDate, entry.Minutes, entry.Activity, entry.Billable, entry.Notes,
        entry.Status, entry.CreatedAtUtc, entry.UpdatedAtUtc);

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() =>
        Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));

    private static async Task AuditAsync(
        ICrmManagementStore management,
        HttpContext context,
        string action,
        Guid entityId,
        string? detail,
        CancellationToken cancellationToken)
    {
        var member = CrmFreeTestingAccessMiddleware.Current(context);
        await management.AddAuditAsync(new CrmAuditEntry(
            Guid.NewGuid(), DemoTenantId, member.Id, action, "Timesheet", entityId.ToString(),
            detail, DateTimeOffset.UtcNow), cancellationToken);
    }
}

public sealed record CreateCrmTimesheetRequest(
    Guid? UserId,
    DateOnly WorkDate,
    int Minutes,
    string Activity,
    bool Billable,
    Guid? ProjectId,
    Guid? TaskId,
    Guid? AccountId,
    string? Notes);

public sealed record UpdateCrmTimesheetRequest(
    Guid UserId,
    DateOnly WorkDate,
    int Minutes,
    string Activity,
    bool Billable,
    Guid? ProjectId,
    Guid? TaskId,
    Guid? AccountId,
    string? Notes);

public sealed record ChangeCrmTimesheetStatusRequest(string Status);

public sealed record CrmTimesheetResponse(
    Guid Id,
    Guid UserId,
    Guid? ProjectId,
    Guid? TaskId,
    Guid? AccountId,
    DateOnly WorkDate,
    int Minutes,
    string Activity,
    bool Billable,
    string? Notes,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
