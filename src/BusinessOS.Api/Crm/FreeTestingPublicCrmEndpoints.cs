using BusinessOS.Api.Commerce;
using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmEndpoints
{
    private static readonly Guid DemoTenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DemoOrganisationId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/leads", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ILeadRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (!IsFreeTestingMode(configuration, environment))
                return Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
            var leads = await repository.ListAsync(DemoTenantId, cancellationToken);
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
                leads = leads.Where(x => x.Attribution.AccountOwnerUserId == member.Id).ToArray();
            return Results.Ok(new CrmLeadListResponse(
                leads.Select(ToResponse).OrderByDescending(x => x.CreatedSort).ToArray()));
        });

        group.MapPost("/leads", async (
            CreateCrmLeadRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ILeadRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (!IsFreeTestingMode(configuration, environment))
                return Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
            try
            {
                var existing = await repository.ListAsync(DemoTenantId, cancellationToken);
                var phone = Digits(request.MobileNumber);
                var email = Normalize(request.Email);
                var duplicate = existing.FirstOrDefault(x =>
                    (!string.IsNullOrWhiteSpace(phone) && Digits(x.MobileNumber) == phone) ||
                    (!string.IsNullOrWhiteSpace(email) && Normalize(x.Email) == email));
                if (duplicate is not null)
                    return Results.Json(new ErrorResponse(
                        $"Duplicate lead detected: {duplicate.Title} ({duplicate.Id}). Mobile/email already exists."),
                        statusCode: StatusCodes.Status409Conflict);

                var lead = new Lead(
                    Guid.NewGuid(),
                    DemoTenantId,
                    DemoOrganisationId,
                    request.Title,
                    new LeadAttribution(
                        request.LeadSource,
                        null, null, null, null, null),
                    request.ContactName,
                    request.MobileNumber,
                    request.Email,
                    request.ProductInterest,
                    request.Notes,
                    ParsePriority(request.Priority),
                    estimatedValue: request.EstimatedValue);
                lead.AssignOwner(CrmFreeTestingAccessMiddleware.Current(context).Id);
                await repository.AddAsync(lead, cancellationToken);
                return Results.Ok(ToResponse(lead));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });
        group.MapPost("/leads/{leadId:guid}/status", async (
            Guid leadId,
            ChangeCrmLeadStatusRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ILeadRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (!IsFreeTestingMode(configuration, environment))
                return Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
            var lead = await repository.GetAsync(DemoTenantId, leadId, cancellationToken);
            if (lead is null) return Results.NotFound(new ErrorResponse("Lead not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) &&
                lead.Attribution.AccountOwnerUserId != member.Id)
                return Results.Json(new ErrorResponse("Lead is outside your CRM scope."), statusCode: StatusCodes.Status403Forbidden);
            try
            {
                ApplyStatus(lead, request);
                await repository.SaveAsync(lead, cancellationToken);
                return Results.Ok(ToResponse(lead));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapGet("/dashboard", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ILeadRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (!IsFreeTestingMode(configuration, environment))
                return Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
            var leads = await repository.ListAsync(DemoTenantId, cancellationToken);
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
                leads = leads.Where(x => x.Attribution.AccountOwnerUserId == member.Id).ToArray();
            var statusCounts = leads
                .GroupBy(x => x.Status.ToString())
                .ToDictionary(x => x.Key, x => x.Count());
            return Results.Ok(new CrmDashboardResponse(
                leads.Count,
                statusCounts.GetValueOrDefault(nameof(LeadStatus.New)),
                statusCounts.GetValueOrDefault(nameof(LeadStatus.Contacted)),
                statusCounts.GetValueOrDefault(nameof(LeadStatus.Qualified)),
                statusCounts.GetValueOrDefault(nameof(LeadStatus.Converted)),
                statusCounts.GetValueOrDefault(nameof(LeadStatus.Unqualified)),
                statusCounts));
        });

        return app;
    }

    private static bool IsFreeTestingMode(
        IConfiguration configuration,
        IHostEnvironment environment) =>
        !environment.IsProduction() &&
        string.Equals(
            configuration["BusinessOS:DeploymentMode"],
            "FreeTesting",
            StringComparison.OrdinalIgnoreCase);

    private static void ApplyStatus(
        Lead lead,
        ChangeCrmLeadStatusRequest request)
    {
        if (!Enum.TryParse<LeadStatus>(request.Status, true, out var status))
            throw new ArgumentException("Valid status is required.");
        if (lead.Status is LeadStatus.Converted or LeadStatus.Unqualified &&
            status is LeadStatus.New or LeadStatus.Contacted or LeadStatus.Qualified)
        {
            lead.Reopen(status);
            return;
        }
        switch (status)
        {
            case LeadStatus.New:
                break;
            case LeadStatus.Contacted:
                lead.MarkContacted();
                break;
            case LeadStatus.Qualified:
                lead.Qualify();
                break;
            case LeadStatus.Unqualified:
                lead.MarkUnqualified(request.Reason ?? "Not a fit");
                break;
            case LeadStatus.Converted:
                if (lead.Status != LeadStatus.Qualified)
                    lead.Qualify();
                lead.Convert();
                break;
            default:
                throw new ArgumentException("Unsupported status.");
        }
    }

    private static LeadPriority ParsePriority(string? value) =>
        Enum.TryParse<LeadPriority>(value, true, out var priority) ? priority : LeadPriority.Normal;

    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static CrmLeadResponse ToResponse(Lead lead) =>
        new(lead.Id, lead.OrganisationId, lead.Title, lead.Status.ToString(),
            lead.Attribution.LeadSource, lead.ContactName, lead.MobileNumber, lead.Email,
            lead.ProductInterest, lead.Notes, lead.EstimatedValue, lead.Priority.ToString(),
            lead.Attribution.AccountOwnerUserId, lead.UnqualifiedReason,
            lead.CreatedAtUtc, lead.UpdatedAtUtc, lead.LastContactAtUtc, lead.NextFollowUpAtUtc,
            lead.Tags.OrderBy(x => x).ToArray(), lead.CreatedAtUtc.ToString("O"));
}

public sealed record CreateCrmLeadRequest(
    string Title,
    string? LeadSource,
    string? ContactName,
    string? MobileNumber,
    string? Email,
    string? ProductInterest,
    string? Notes,
    string? Priority,
    decimal? EstimatedValue);

public sealed record ChangeCrmLeadStatusRequest(
    string Status,
    string? Reason);

public sealed record CrmLeadResponse(
    Guid Id,
    Guid OrganisationId,
    string Title,
    string Status,
    string? LeadSource,
    string? ContactName,
    string? MobileNumber,
    string? Email,
    string? ProductInterest,
    string? Notes,
    decimal? EstimatedValue,
    string Priority,
    Guid? OwnerUserId,
    string? UnqualifiedReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastContactAtUtc,
    DateTimeOffset? NextFollowUpAtUtc,
    IReadOnlyList<string> Tags,
    string CreatedSort);
public sealed record CrmLeadListResponse(
    IReadOnlyList<CrmLeadResponse> Leads);

public sealed record CrmDashboardResponse(
    int TotalLeads,
    int New,
    int Contacted,
    int Qualified,
    int Converted,
    int Unqualified,
    IReadOnlyDictionary<string, int> StatusCounts);
