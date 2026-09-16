using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public sealed class CrmAccountScopeMiddleware
{
    private const string AccountsPath = "/api/testing/public/crm/accounts";
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly RequestDelegate _next;

    public CrmAccountScopeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ICrmAccountStore accounts,
        ILeadRepository leads,
        ICrmOpportunityStore opportunities,
        ICrmManagementStore management)
    {
        if (!context.Request.Path.StartsWithSegments(AccountsPath) ||
            context.Items[CrmFreeTestingAccessMiddleware.ItemKey] is not CrmTeamMember member ||
            member.Role != CrmRoleCode.SalesExecutive)
        {
            await _next(context);
            return;
        }

        var allowed = await AllowedAccountIdsAsync(member.Id, leads, opportunities, management, context.RequestAborted);
        var path = context.Request.Path.Value ?? string.Empty;
        var relative = path.Length > AccountsPath.Length ? path[AccountsPath.Length..].Trim('/') : string.Empty;

        if (HttpMethods.IsGet(context.Request.Method) && string.IsNullOrEmpty(relative))
        {
            var visible = (await accounts.ListAsync(DemoTenantId, context.RequestAborted))
                .Where(x => allowed.Contains(x.Id))
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.LegalName,
                    x.Gstin,
                    x.DisplayCode,
                    Status = x.Status.ToString(),
                    PrimaryContact = x.PrimaryContact is null ? null : new
                    {
                        x.PrimaryContact.Id,
                        x.PrimaryContact.Name,
                        x.PrimaryContact.Email,
                        x.PrimaryContact.Phone,
                        x.PrimaryContact.IsPrimary,
                        x.PrimaryContact.Designation
                    },
                    Contacts = x.Contacts.Select(c => new { c.Id, c.Name, c.Email, c.Phone, c.IsPrimary, c.Designation }).ToArray()
                }).ToArray();
            await context.Response.WriteAsJsonAsync(new { accounts = visible }, context.RequestAborted);
            return;
        }

        var first = relative.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (Guid.TryParse(first, out var accountId) && !allowed.Contains(accountId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Account is outside your CRM scope." }, context.RequestAborted);
            return;
        }

        await _next(context);
    }

    private static async Task<HashSet<Guid>> AllowedAccountIdsAsync(
        Guid userId,
        ILeadRepository leads,
        ICrmOpportunityStore opportunities,
        ICrmManagementStore management,
        CancellationToken ct)
    {
        var ids = (await opportunities.ListAsync(DemoTenantId, ct))
            .Where(x => x.OwnerUserId == userId)
            .Select(x => x.OrganisationId)
            .ToHashSet();

        foreach (var lead in (await leads.ListAsync(DemoTenantId, ct)).Where(x => x.Attribution.AccountOwnerUserId == userId))
            ids.Add(lead.OrganisationId);

        foreach (var audit in (await management.ListAuditAsync(DemoTenantId, 500, ct))
                     .Where(x => x.ActorUserId == userId && x.Action == "AccountCreated" && x.EntityType == "Account"))
            if (Guid.TryParse(audit.EntityId, out var id)) ids.Add(id);

        return ids;
    }
}
