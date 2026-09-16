using BusinessOS.Api.Commerce;
using BusinessOS.Crm;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmAnalyticsEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");
        group.MapGet("/reports/detailed", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ILeadRepository leads,
            ICrmWorkRepository work,
            ICrmAccountStore accounts,
            ICrmOpportunityStore opportunities,
            ICrmTeamRepository team,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var current = CrmFreeTestingAccessMiddleware.Current(context);
            var canViewAll = CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(current);

            var leadItems = await leads.ListAsync(DemoTenantId, ct);
            var followUps = await work.ListFollowUpsAsync(DemoTenantId, null, ct);
            var tasks = await work.ListTasksAsync(DemoTenantId, null, ct);
            var opportunityItems = await opportunities.ListAsync(DemoTenantId, ct);
            var accountItems = await accounts.ListAsync(DemoTenantId, ct);
            var teamItems = await team.ListAsync(DemoTenantId, ct);

            if (!canViewAll)
            {
                leadItems = leadItems.Where(x => x.Attribution.AccountOwnerUserId == current.Id).ToArray();
                followUps = followUps.Where(x => x.OwnerUserId == current.Id).ToArray();
                tasks = tasks.Where(x => x.AssigneeUserId == current.Id).ToArray();
                opportunityItems = opportunityItems.Where(x => x.OwnerUserId == current.Id).ToArray();
            }

            var now = DateTimeOffset.UtcNow;
            var memberMap = teamItems.ToDictionary(x => x.Id, x => x.DisplayName);
            string MemberName(Guid? id) => id.HasValue && memberMap.TryGetValue(id.Value, out var name) ? name : "Unassigned";

            var executiveIds = canViewAll
                ? teamItems.Where(x => x.Active).Select(x => x.Id).ToArray()
                : new[] { current.Id };
            var executives = executiveIds.Select(id =>
            {
                var ownedLeads = leadItems.Where(x => x.Attribution.AccountOwnerUserId == id).ToArray();
                var ownedOpps = opportunityItems.Where(x => x.OwnerUserId == id).ToArray();
                var ownedFollowUps = followUps.Where(x => x.OwnerUserId == id).ToArray();
                var ownedTasks = tasks.Where(x => x.AssigneeUserId == id).ToArray();
                var converted = ownedLeads.Count(x => x.Status == LeadStatus.Converted);
                var openOpps = ownedOpps.Where(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost)).ToArray();
                return new CrmExecutivePerformance(
                    id, MemberName(id), ownedLeads.Length, converted,
                    ownedLeads.Length == 0 ? 0m : Math.Round(converted * 100m / ownedLeads.Length, 2),
                    ownedFollowUps.Count(x => x.Status == CrmWorkStatus.Completed),
                    ownedTasks.Count(x => x.Status == CrmWorkStatus.Completed),
                    openOpps.Length,
                    openOpps.Sum(x => x.Forecast.EstimatedValue),
                    openOpps.Sum(x => x.Forecast.EstimatedValue * x.Forecast.ProbabilityPercent / 100m),
                    ownedOpps.Where(x => x.Stage == OpportunityStage.Won).Sum(x => x.Forecast.EstimatedValue));
            }).OrderByDescending(x => x.WeightedPipelineValue).ToArray();

            var followUpProductivity = followUps.GroupBy(x => x.OwnerUserId)
                .Select(g => new CrmFollowUpProductivity(
                    g.Key, MemberName(g.Key), g.Count(),
                    g.Count(x => x.Status == CrmWorkStatus.Open),
                    g.Count(x => x.Status == CrmWorkStatus.Completed),
                    g.Count(x => x.Status == CrmWorkStatus.Cancelled),
                    g.Count(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc < now)))
                .OrderByDescending(x => x.Completed).ToArray();

            var taskPerformance = tasks.GroupBy(x => x.AssigneeUserId)
                .Select(g => new CrmTaskPerformance(
                    g.Key, MemberName(g.Key), g.Count(),
                    g.Count(x => x.Status == CrmWorkStatus.Open),
                    g.Count(x => x.Status == CrmWorkStatus.Completed),
                    g.Count(x => x.Status == CrmWorkStatus.Cancelled),
                    g.Count(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc.HasValue && x.DueAtUtc.Value < now)))
                .OrderByDescending(x => x.Completed).ToArray();

            var openOpportunities = opportunityItems.Where(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost)).ToArray();
            var ownerForecast = openOpportunities.GroupBy(x => x.OwnerUserId)
                .Select(g => new CrmForecastBucket(MemberName(g.Key), g.Count(),
                    g.Sum(x => x.Forecast.EstimatedValue),
                    g.Sum(x => x.Forecast.EstimatedValue * x.Forecast.ProbabilityPercent / 100m)))
                .OrderByDescending(x => x.WeightedValue).ToArray();

            var monthForecast = openOpportunities.Where(x => x.Forecast.ExpectedCloseDate.HasValue)
                .GroupBy(x => x.Forecast.ExpectedCloseDate!.Value.ToString("yyyy-MM"))
                .Select(g => new CrmForecastBucket(g.Key, g.Count(),
                    g.Sum(x => x.Forecast.EstimatedValue),
                    g.Sum(x => x.Forecast.EstimatedValue * x.Forecast.ProbabilityPercent / 100m)))
                .OrderBy(x => x.Label).ToArray();

            var accountBusiness = accountItems.Select(account =>
            {
                var accountOpps = opportunityItems.Where(x => x.OrganisationId == account.Id).ToArray();
                var open = accountOpps.Where(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost)).ToArray();
                return new CrmAccountBusiness(
                    account.Id, account.Name, accountOpps.Length,
                    open.Sum(x => x.Forecast.EstimatedValue),
                    open.Sum(x => x.Forecast.EstimatedValue * x.Forecast.ProbabilityPercent / 100m),
                    accountOpps.Where(x => x.Stage == OpportunityStage.Won).Sum(x => x.Forecast.EstimatedValue));
            }).Where(x => x.OpportunityCount > 0).OrderByDescending(x => x.WonValue + x.OpenPipelineValue).ToArray();

            var conversionEvents = new List<DateTimeOffset>();
            foreach (var lead in leadItems.Where(x => x.Status == LeadStatus.Converted))
            {
                var activities = await work.ListActivitiesAsync(DemoTenantId, lead.Id, ct);
                var convertedAt = activities.Where(x => x.Type == CrmActivityType.Converted)
                    .OrderBy(x => x.OccurredAtUtc).Select(x => (DateTimeOffset?)x.OccurredAtUtc).FirstOrDefault();
                conversionEvents.Add(convertedAt ?? lead.UpdatedAtUtc);
            }

            var months = leadItems.Select(x => x.CreatedAtUtc.ToString("yyyy-MM"))
                .Concat(conversionEvents.Select(x => x.ToString("yyyy-MM")))
                .Distinct().OrderBy(x => x).ToArray();
            var monthlyTrends = months.Select(month => new CrmMonthlyTrend(
                month,
                leadItems.Count(x => x.CreatedAtUtc.ToString("yyyy-MM") == month),
                conversionEvents.Count(x => x.ToString("yyyy-MM") == month),
                opportunityItems.Count(x => x.Forecast.ExpectedCloseDate?.ToString("yyyy-MM") == month)))
                .ToArray();

            return Results.Ok(new CrmDetailedAnalyticsResponse(
                executives, followUpProductivity, taskPerformance, ownerForecast,
                monthForecast, accountBusiness, monthlyTrends));
        });
        return app;
    }

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
}

public sealed record CrmExecutivePerformance(
    Guid UserId, string UserName, int Leads, int ConvertedLeads, decimal ConversionPercent,
    int CompletedFollowUps, int CompletedTasks, int OpenOpportunities,
    decimal OpenPipelineValue, decimal WeightedPipelineValue, decimal WonValue);
public sealed record CrmFollowUpProductivity(Guid? UserId, string UserName, int Total, int Open, int Completed, int Cancelled, int Overdue);
public sealed record CrmTaskPerformance(Guid? UserId, string UserName, int Total, int Open, int Completed, int Cancelled, int Overdue);
public sealed record CrmForecastBucket(string Label, int OpportunityCount, decimal PipelineValue, decimal WeightedValue);
public sealed record CrmAccountBusiness(Guid AccountId, string AccountName, int OpportunityCount, decimal OpenPipelineValue, decimal WeightedPipelineValue, decimal WonValue);
public sealed record CrmMonthlyTrend(string Month, int LeadsCreated, int LeadsConverted, int ExpectedClosures);
public sealed record CrmDetailedAnalyticsResponse(
    IReadOnlyList<CrmExecutivePerformance> ExecutivePerformance,
    IReadOnlyList<CrmFollowUpProductivity> FollowUpProductivity,
    IReadOnlyList<CrmTaskPerformance> TaskPerformance,
    IReadOnlyList<CrmForecastBucket> OwnerForecast,
    IReadOnlyList<CrmForecastBucket> MonthForecast,
    IReadOnlyList<CrmAccountBusiness> AccountBusiness,
    IReadOnlyList<CrmMonthlyTrend> MonthlyTrends);
