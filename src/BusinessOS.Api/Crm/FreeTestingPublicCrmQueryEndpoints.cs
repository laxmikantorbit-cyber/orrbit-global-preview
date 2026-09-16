using BusinessOS.Api.Commerce;
using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmQueryEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/leads/query", async (
            string? q,
            string? status,
            string? priority,
            string? leadSource,
            string? product,
            string? tag,
            Guid? ownerUserId,
            DateTimeOffset? createdFromUtc,
            DateTimeOffset? createdToUtc,
            DateTimeOffset? followUpFromUtc,
            DateTimeOffset? followUpToUtc,
            int? page,
            int? pageSize,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ILeadRepository leads,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var items = await leads.ListAsync(DemoTenantId, cancellationToken);

            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
                items = items.Where(x => x.Attribution.AccountOwnerUserId == member.Id).ToArray();
            else if (ownerUserId.HasValue)
                items = items.Where(x => x.Attribution.AccountOwnerUserId == ownerUserId.Value).ToArray();

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<LeadStatus>(status, true, out var parsed))
                    return Results.BadRequest(new ErrorResponse("Valid lead status is required."));
                items = items.Where(x => x.Status == parsed).ToArray();
            }
            if (!string.IsNullOrWhiteSpace(priority))
            {
                if (!Enum.TryParse<LeadPriority>(priority, true, out var parsed))
                    return Results.BadRequest(new ErrorResponse("Valid lead priority is required."));
                items = items.Where(x => x.Priority == parsed).ToArray();
            }
            if (!string.IsNullOrWhiteSpace(leadSource))
                items = items.Where(x => Contains(x.Attribution.LeadSource, leadSource)).ToArray();
            if (!string.IsNullOrWhiteSpace(product))
                items = items.Where(x => Contains(x.ProductInterest, product)).ToArray();
            if (!string.IsNullOrWhiteSpace(tag))
                items = items.Where(x => x.Tags.Any(t => t.Equals(tag.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
            if (createdFromUtc.HasValue) items = items.Where(x => x.CreatedAtUtc >= createdFromUtc.Value).ToArray();
            if (createdToUtc.HasValue) items = items.Where(x => x.CreatedAtUtc <= createdToUtc.Value).ToArray();
            if (followUpFromUtc.HasValue) items = items.Where(x => x.NextFollowUpAtUtc.HasValue && x.NextFollowUpAtUtc >= followUpFromUtc.Value).ToArray();
            if (followUpToUtc.HasValue) items = items.Where(x => x.NextFollowUpAtUtc.HasValue && x.NextFollowUpAtUtc <= followUpToUtc.Value).ToArray();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                items = items.Where(x =>
                    Contains(x.Title, needle) || Contains(x.ContactName, needle) || Contains(x.MobileNumber, needle) ||
                    Contains(x.Email, needle) || Contains(x.ProductInterest, needle) || Contains(x.Notes, needle) ||
                    Contains(x.Attribution.LeadSource, needle) || x.Tags.Any(t => Contains(t, needle))).ToArray();
            }

            var ordered = items.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.CreatedAtUtc).ToArray();
            var total = ordered.Length;
            var currentPage = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 200);
            var pageItems = ordered.Skip((currentPage - 1) * size).Take(size).Select(ToResponse).ToArray();
            return Results.Ok(new CrmLeadQueryResponse(total, currentPage, size, pageItems));
        });

        return app;
    }

    private static CrmLeadQueryItem ToResponse(Lead x) => new(
        x.Id, x.Title, x.Status.ToString(), x.Priority.ToString(), x.Attribution.LeadSource,
        x.ContactName, x.MobileNumber, x.Email, x.ProductInterest, x.Attribution.AccountOwnerUserId,
        x.NextFollowUpAtUtc, x.CreatedAtUtc, x.UpdatedAtUtc, x.Tags.OrderBy(t => t).ToArray());

    private static bool Contains(string? value, string needle) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
}

public sealed record CrmLeadQueryItem(
    Guid Id,
    string Title,
    string Status,
    string Priority,
    string? LeadSource,
    string? ContactName,
    string? MobileNumber,
    string? Email,
    string? ProductInterest,
    Guid? OwnerUserId,
    DateTimeOffset? NextFollowUpAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<string> Tags);

public sealed record CrmLeadQueryResponse(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<CrmLeadQueryItem> Leads);
