using BusinessOS.Api.Commerce;
using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmTaskTimerEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmTaskTimerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/task-timers", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmTaskTimerStore timers,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var current = CrmFreeTestingAccessMiddleware.Current(context);
            var items = await timers.ListAsync(DemoTenantId, cancellationToken);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(current))
                items = items.Where(x => x.UserId == current.Id).ToArray();
            return Results.Ok(new { timers = items.Select(ToResponse).ToArray() });
        });

        group.MapPost("/task-timers/start", async (
            StartCrmTaskTimerRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmTaskTimerStore timers,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var current = CrmFreeTestingAccessMiddleware.Current(context);
            var task = await work.GetTaskAsync(DemoTenantId, request.TaskId, cancellationToken);
            if (task is null) return Results.NotFound(new ErrorResponse("Task not found."));
            if (task.Status != CrmWorkStatus.Open)
                return Results.BadRequest(new ErrorResponse("Only open tasks can be timed."));
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(current) && task.AssigneeUserId != current.Id)
                return Results.Json(new ErrorResponse("Task is outside your CRM scope."), statusCode: StatusCodes.Status403Forbidden);

            var timer = new CrmTaskTimerEntry(
                Guid.NewGuid(),
                DemoTenantId,
                task.Id,
                current.Id,
                DateTimeOffset.UtcNow,
                request.Note);
            await timers.AddAsync(timer, cancellationToken);
            return Results.Ok(ToResponse(timer));
        });

        group.MapPost("/task-timers/{timerId:guid}/stop", async (
            Guid timerId,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmTaskTimerStore timers,
            ICrmTimesheetStore timesheets,
            ICrmWorkRepository work,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var current = CrmFreeTestingAccessMiddleware.Current(context);
            var timer = await timers.GetAsync(DemoTenantId, timerId, cancellationToken);
            if (timer is null) return Results.NotFound(new ErrorResponse("Task timer not found."));
            if (timer.UserId != current.Id && !CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(current))
                return Results.Json(new ErrorResponse("Task timer is outside your CRM scope."), statusCode: StatusCodes.Status403Forbidden);

            var task = await work.GetTaskAsync(DemoTenantId, timer.TaskId, cancellationToken);
            if (task is null) return Results.BadRequest(new ErrorResponse("Task linked to this timer no longer exists."));

            try
            {
                timer.Stop(DateTimeOffset.UtcNow);
                await timers.SaveAsync(timer, cancellationToken);

                var minutes = (int)Math.Ceiling(timer.DurationSeconds / 60d);
                if (minutes is >= 1 and <= 1440)
                {
                    var timesheet = new CrmTimesheetEntry(
                        Guid.NewGuid(),
                        DemoTenantId,
                        timer.UserId,
                        DateOnly.FromDateTime(timer.StartedAtUtc.UtcDateTime),
                        minutes,
                        task.Title,
                        false,
                        taskId: task.Id,
                        notes: timer.Note);
                    await timesheets.AddAsync(timesheet, cancellationToken);
                }

                return Results.Ok(ToResponse(timer));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        return app;
    }

    private static CrmTaskTimerResponse ToResponse(CrmTaskTimerEntry entry) => new(
        entry.Id,
        entry.TaskId,
        entry.UserId,
        entry.StartedAtUtc,
        entry.StoppedAtUtc,
        entry.Note,
        entry.IsRunning,
        entry.DurationSeconds,
        entry.CreatedAtUtc,
        entry.UpdatedAtUtc);

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() =>
        Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
}

public sealed record StartCrmTaskTimerRequest(Guid TaskId, string? Note);

public sealed record CrmTaskTimerResponse(
    Guid Id,
    Guid TaskId,
    Guid UserId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? StoppedAtUtc,
    string? Note,
    bool IsRunning,
    long DurationSeconds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
