using BusinessOS.Api.Commerce;
using BusinessOS.Crm;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmOpportunityAgingEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmOpportunityAgingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");
        group.MapGet("/reports/opportunity-aging", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmOpportunityStore opportunities,
            ICrmManagementStore management,
            ICrmWorkRepository work,
            ICrmTeamRepository team,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var items = await opportunities.ListAsync(DemoTenantId, ct);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
                items = items.Where(x => x.OwnerUserId == member.Id).ToArray();

            var audit = await management.ListAuditAsync(DemoTenantId, 500, ct);
            var members = (await team.ListAsync(DemoTenantId, ct)).ToDictionary(x => x.Id, x => x.DisplayName);
            var now = DateTimeOffset.UtcNow;
            var result = new List<CrmOpportunityAgingItem>();

            foreach (var item in items)
            {
                var id = item.Id.ToString();
                var createdAudit = audit.Where(x => x.EntityType.Equals("Opportunity", StringComparison.OrdinalIgnoreCase) &&
                                                     x.EntityId == id &&
                                                     x.Action.Equals("OpportunityCreated", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.CreatedAtUtc).FirstOrDefault();
                DateTimeOffset? started = createdAudit?.CreatedAtUtc;
                if (!started.HasValue && item.OriginatingLeadId.HasValue)
                {
                    var activities = await work.ListActivitiesAsync(DemoTenantId, item.OriginatingLeadId.Value, ct);
                    started = activities.Where(x => x.Type == CrmActivityType.Converted)
                        .OrderBy(x => x.OccurredAtUtc).Select(x => (DateTimeOffset?)x.OccurredAtUtc).FirstOrDefault();
                }

                var stageChanged = audit.Where(x => x.EntityType.Equals("Opportunity", StringComparison.OrdinalIgnoreCase) &&
                                                     x.EntityId == id &&
                                                     x.Action.Equals("OpportunityStageChanged", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault()?.CreatedAtUtc;
                var stageStarted = stageChanged ?? started;
                var ageDays = started.HasValue ? Math.Max(0, (int)Math.Floor((now - started.Value).TotalDays)) : (int?)null;
                var stageAgeDays = stageStarted.HasValue ? Math.Max(0, (int)Math.Floor((now - stageStarted.Value).TotalDays)) : (int?)null;
                var ownerName = item.OwnerUserId.HasValue && members.TryGetValue(item.OwnerUserId.Value, out var name) ? name : "Unassigned";
                result.Add(new CrmOpportunityAgingItem(
                    item.Id, item.Title, item.ProductService, item.Stage.ToString(), item.OwnerUserId, ownerName,
                    item.Forecast.EstimatedValue, item.Forecast.ProbabilityPercent, item.Forecast.ExpectedCloseDate,
                    started, ageDays, stageAgeDays));
            }

            var ordered = result.OrderByDescending(x => x.AgeDays ?? -1).ThenByDescending(x => x.StageAgeDays ?? -1).ToArray();
            var buckets = new[]
            {
                Bucket("0-7 days", ordered, 0, 7),
                Bucket("8-14 days", ordered, 8, 14),
                Bucket("15-30 days", ordered, 15, 30),
                Bucket("31-60 days", ordered, 31, 60),
                new CrmOpportunityAgingBucket("61+ days", ordered.Count(x => x.AgeDays >= 61)),
                new CrmOpportunityAgingBucket("Unknown", ordered.Count(x => !x.AgeDays.HasValue)),
            };
            return Results.Ok(new CrmOpportunityAgingResponse(ordered.Length, buckets, ordered));
        });
        return app;
    }

    private static CrmOpportunityAgingBucket Bucket(string label, IReadOnlyList<CrmOpportunityAgingItem> items, int from, int to) =>
        new(label, items.Count(x => x.AgeDays >= from && x.AgeDays <= to));

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);
    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
}

public sealed record CrmOpportunityAgingBucket(string Label, int Count);
public sealed record CrmOpportunityAgingItem(
    Guid OpportunityId,
    string Title,
    string? ProductService,
    string Stage,
    Guid? OwnerUserId,
    string OwnerName,
    decimal EstimatedValue,
    int ProbabilityPercent,
    DateOnly? ExpectedCloseDate,
    DateTimeOffset? StartedAtUtc,
    int? AgeDays,
    int? StageAgeDays);
public sealed record CrmOpportunityAgingResponse(
    int OpportunityCount,
    IReadOnlyList<CrmOpportunityAgingBucket> Buckets,
    IReadOnlyList<CrmOpportunityAgingItem> Opportunities);
