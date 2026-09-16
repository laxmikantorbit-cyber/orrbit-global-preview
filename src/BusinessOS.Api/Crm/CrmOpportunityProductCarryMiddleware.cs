using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public sealed class CrmOpportunityProductCarryMiddleware
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Prefix = "/api/testing/public/crm/leads/";
    private readonly RequestDelegate _next;

    public CrmOpportunityProductCarryMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ILeadRepository leads,
        ICrmOpportunityStore opportunities,
        ICrmManagementStore management)
    {
        var leadId = ConversionLeadId(context.Request);
        Lead? lead = null;
        if (leadId.HasValue)
            lead = await leads.GetAsync(DemoTenantId, leadId.Value, context.RequestAborted);

        await _next(context);

        if (lead is null || string.IsNullOrWhiteSpace(lead.ProductInterest) ||
            context.Response.StatusCode is < 200 or >= 300)
            return;

        try
        {
            var items = await opportunities.ListAsync(DemoTenantId, context.RequestAborted);
            var opportunity = items.FirstOrDefault(x => x.OriginatingLeadId == lead.Id);
            if (opportunity is null || !string.IsNullOrWhiteSpace(opportunity.ProductService)) return;
            opportunity.SetProductService(lead.ProductInterest);
            await opportunities.SaveAsync(opportunity, context.RequestAborted);
            var actor = context.Items.TryGetValue(CrmFreeTestingAccessMiddleware.ItemKey, out var value) && value is CrmTeamMember member
                ? member.Id : (Guid?)null;
            await management.AddAuditAsync(new CrmAuditEntry(
                Guid.NewGuid(), DemoTenantId, actor, "OpportunityProductInherited", "Opportunity",
                opportunity.Id.ToString(), $"Product/service inherited from lead: {lead.ProductInterest}", DateTimeOffset.UtcNow),
                context.RequestAborted);
        }
        catch
        {
            // Conversion has already succeeded; product carry is best-effort and remains repairable from CRM maintenance.
        }
    }

    private static Guid? ConversionLeadId(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method)) return null;
        var path = request.Path.Value ?? string.Empty;
        if (!path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith("/convert", StringComparison.OrdinalIgnoreCase)) return null;
        var middle = path[Prefix.Length..^"/convert".Length].Trim('/');
        return Guid.TryParse(middle, out var id) && id != Guid.Empty ? id : null;
    }
}
