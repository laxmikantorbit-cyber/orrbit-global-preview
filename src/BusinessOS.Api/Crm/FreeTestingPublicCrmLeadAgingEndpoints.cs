using BusinessOS.Api.Commerce;
using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmLeadAgingEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmLeadAgingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");
        group.MapGet("/reports/lead-aging", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ILeadRepository leads,
            ICrmTeamRepository team,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var items = await leads.ListAsync(DemoTenantId, ct);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
                items = items.Where(x => x.Attribution.AccountOwnerUserId == member.Id).ToArray();

            var teamItems = await team.ListAsync(DemoTenantId, ct);
            var names = teamItems.ToDictionary(x => x.Id, x => x.DisplayName);
            var now = DateTimeOffset.UtcNow;
            var open = items.Where(x => x.Status is not (LeadStatus.Converted or LeadStatus.Unqualified))
                .Select(x => new CrmLeadAgingItem(
                    x.Id,
                    x.Title,
                    x.Status.ToString(),
                    x.Priority.ToString(),
                    x.Attribution.AccountOwnerUserId,
                    x.Attribution.AccountOwnerUserId.HasValue && names.TryGetValue(x.Attribution.AccountOwnerUserId.Value, out var owner) ? owner : "Unassigned",
                    Math.Max(0, (int)Math.Floor((now - x.CreatedAtUtc).TotalDays)),
                    Math.Max(0, (int)Math.Floor((now - x.UpdatedAtUtc).TotalDays)),
                    x.NextFollowUpAtUtc,
                    x.ProductInterest))
                .OrderByDescending(x => x.InactiveDays)
                .ThenByDescending(x => x.AgeDays)
                .ToArray();

            var buckets = new[]
            {
                Bucket("0-2 days", open, 0, 2),
                Bucket("3-7 days", open, 3, 7),
                Bucket("8-14 days", open, 8, 14),
                Bucket("15-30 days", open, 15, 30),
                new CrmLeadAgingBucket("31+ days", open.Count(x => x.AgeDays >= 31), open.Count(x => x.InactiveDays >= 31)),
            };

            return Results.Ok(new CrmLeadAgingResponse(open.Length, buckets, open));
        });
        return app;
    }

    private static CrmLeadAgingBucket Bucket(string label, IReadOnlyList<CrmLeadAgingItem> items, int from, int to) =>
        new(label,
            items.Count(x => x.AgeDays >= from && x.AgeDays <= to),
            items.Count(x => x.InactiveDays >= from && x.InactiveDays <= to));

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
}

public sealed record CrmLeadAgingBucket(string Label, int LeadAgeCount, int InactivityCount);
public sealed record CrmLeadAgingItem(
    Guid LeadId,
    string Title,
    string Status,
    string Priority,
    Guid? OwnerUserId,
    string OwnerName,
    int AgeDays,
    int InactiveDays,
    DateTimeOffset? NextFollowUpAtUtc,
    string? ProductInterest);
public sealed record CrmLeadAgingResponse(
    int OpenLeadCount,
    IReadOnlyList<CrmLeadAgingBucket> Buckets,
    IReadOnlyList<CrmLeadAgingItem> Leads);
