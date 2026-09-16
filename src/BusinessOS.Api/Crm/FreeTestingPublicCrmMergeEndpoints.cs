using BusinessOS.Api.Commerce;
using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmMergeEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmMergeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapPost("/leads/{sourceLeadId:guid}/merge", async (
            Guid sourceLeadId,
            MergeCrmLeadRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ILeadRepository leads,
            ICrmWorkRepository work,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            if (request.TargetLeadId == Guid.Empty || request.TargetLeadId == sourceLeadId)
                return Results.BadRequest(new ErrorResponse("A different target lead is required."));

            var source = await leads.GetAsync(DemoTenantId, sourceLeadId, cancellationToken);
            var target = await leads.GetAsync(DemoTenantId, request.TargetLeadId, cancellationToken);
            if (source is null || target is null)
                return Results.NotFound(new ErrorResponse("Source or target lead was not found."));

            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanAccessLead(member, source) ||
                !CrmFreeTestingAccessMiddleware.CanAccessLead(member, target))
                return Results.Json(new ErrorResponse("Both leads must be inside your CRM scope."), statusCode: StatusCodes.Status403Forbidden);
            if (source.Status is LeadStatus.Converted or LeadStatus.Unqualified)
                return Results.BadRequest(new ErrorResponse("Closed source lead cannot be merged."));
            if (target.Status is LeadStatus.Converted or LeadStatus.Unqualified)
                return Results.BadRequest(new ErrorResponse("Closed target lead cannot receive a merge."));

            try
            {
                var notes = MergeNotes(target.Notes, source.Notes, source.Id);
                target.UpdateProfile(
                    target.Title,
                    target.ContactName ?? source.ContactName,
                    target.MobileNumber ?? source.MobileNumber,
                    target.Email ?? source.Email,
                    target.ProductInterest ?? source.ProductInterest,
                    notes);
                if (!target.Attribution.AccountOwnerUserId.HasValue && source.Attribution.AccountOwnerUserId.HasValue)
                    target.AssignOwner(source.Attribution.AccountOwnerUserId);
                foreach (var tag in source.Tags) target.AddTag(tag);

                var movedFollowUps = 0;
                var sourceFollowUps = await work.ListFollowUpsAsync(DemoTenantId, source.Id, cancellationToken);
                foreach (var item in sourceFollowUps.Where(x => x.Status == CrmWorkStatus.Open))
                {
                    item.Cancel($"Merged into lead {target.Id}");
                    await work.SaveFollowUpAsync(item, cancellationToken);
                    await work.AddFollowUpAsync(new LeadFollowUp(
                        Guid.NewGuid(), DemoTenantId, target.Id, item.DueAtUtc, item.Channel,
                        item.Purpose, item.OwnerUserId), cancellationToken);
                    movedFollowUps += 1;
                }

                var movedTasks = 0;
                var sourceTasks = await work.ListTasksAsync(DemoTenantId, source.Id, cancellationToken);
                foreach (var item in sourceTasks.Where(x => x.Status == CrmWorkStatus.Open))
                {
                    item.Cancel();
                    await work.SaveTaskAsync(item, cancellationToken);
                    await work.AddTaskAsync(new CrmTask(
                        Guid.NewGuid(), DemoTenantId, item.Title, item.DueAtUtc, target.Id,
                        item.Details, item.Priority, item.AssigneeUserId), cancellationToken);
                    movedTasks += 1;
                }

                var nextFollowUp = (await work.ListFollowUpsAsync(DemoTenantId, target.Id, cancellationToken))
                    .Where(x => x.Status == CrmWorkStatus.Open)
                    .OrderBy(x => x.DueAtUtc)
                    .FirstOrDefault();
                target.ScheduleNextFollowUp(nextFollowUp?.DueAtUtc);
                await leads.SaveAsync(target, cancellationToken);

                source.MarkUnqualified($"Merged into lead {target.Id}");
                await leads.SaveAsync(source, cancellationToken);

                var detail = $"Source={source.Id}; Target={target.Id}; FollowUpsMoved={movedFollowUps}; TasksMoved={movedTasks}";
                await work.AddActivityAsync(new LeadActivity(Guid.NewGuid(), DemoTenantId, target.Id,
                    CrmActivityType.ProfileUpdated, "Duplicate lead merged", detail, member.Id), cancellationToken);
                await work.AddActivityAsync(new LeadActivity(Guid.NewGuid(), DemoTenantId, source.Id,
                    CrmActivityType.StatusChanged, "Lead merged into another record", detail, member.Id), cancellationToken);
                await management.AddAuditAsync(new CrmAuditEntry(
                    Guid.NewGuid(), DemoTenantId, member.Id, "LeadMerged", "Lead", source.Id.ToString(),
                    detail, DateTimeOffset.UtcNow), cancellationToken);

                return Results.Ok(new MergeCrmLeadResponse(source.Id, target.Id, movedFollowUps, movedTasks,
                    target.Tags.OrderBy(x => x).ToArray()));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        return app;
    }

    private static string? MergeNotes(string? target, string? source, Guid sourceId)
    {
        if (string.IsNullOrWhiteSpace(source)) return target;
        var merged = $"Merged from lead {sourceId}: {source.Trim()}";
        return string.IsNullOrWhiteSpace(target) ? merged : $"{target.Trim()}\n{merged}";
    }

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
}

public sealed record MergeCrmLeadRequest(Guid TargetLeadId);
public sealed record MergeCrmLeadResponse(
    Guid SourceLeadId,
    Guid TargetLeadId,
    int FollowUpsMoved,
    int TasksMoved,
    IReadOnlyList<string> TargetTags);
