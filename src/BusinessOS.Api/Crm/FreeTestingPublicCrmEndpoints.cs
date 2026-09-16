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
            ILeadRepository repository,
            CancellationToken cancellationToken) =>
        {
            var leads = await repository.ListAsync(DemoTenantId, cancellationToken);
            return Results.Ok(new CrmLeadListResponse(
                leads.Select(ToResponse).OrderByDescending(x => x.CreatedSort).ToArray()));
        });

        group.MapPost("/leads", async (
            CreateCrmLeadRequest request,
            ILeadRepository repository,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var lead = new Lead(
                    Guid.NewGuid(),
                    DemoTenantId,
                    DemoOrganisationId,
                    request.Title,
                    new LeadAttribution(
                        request.LeadSource,
                        null, null, null, null, null));
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
            ILeadRepository repository,
            CancellationToken cancellationToken) =>
        {
            var lead = await repository.GetAsync(DemoTenantId, leadId, cancellationToken);
            if (lead is null) return Results.NotFound(new ErrorResponse("Lead not found."));
            try
            {
                ApplyStatus(lead, request);
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
            ILeadRepository repository,
            CancellationToken cancellationToken) =>
        {
            var leads = await repository.ListAsync(DemoTenantId, cancellationToken);
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

    private static void ApplyStatus(
        Lead lead,
        ChangeCrmLeadStatusRequest request)
    {
        if (!Enum.TryParse<LeadStatus>(request.Status, true, out var status))
            throw new ArgumentException("Valid status is required.");
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

    private static CrmLeadResponse ToResponse(Lead lead) =>
        new(lead.Id, lead.OrganisationId, lead.Title, lead.Status.ToString(),
            lead.Attribution.LeadSource,
            lead.UnqualifiedReason,
            lead.Id.ToString("N"));
}

public sealed record CreateCrmLeadRequest(
    string Title,
    string? LeadSource,
    string? ContactName,
    string? MobileNumber,
    string? Notes);

public sealed record ChangeCrmLeadStatusRequest(
    string Status,
    string? Reason);

public sealed record CrmLeadResponse(
    Guid Id,
    Guid OrganisationId,
    string Title,
    string Status,
    string? LeadSource,
    string? UnqualifiedReason,
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
