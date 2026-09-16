using BusinessOS.Api.Commerce;
using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmContactDirectoryEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmContactDirectoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/contacts/directory", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ILeadRepository leads,
            ICrmOpportunityStore opportunities,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var accountItems = await accounts.ListAsync(DemoTenantId, ct);
            if (member.Role == CrmRoleCode.SalesExecutive)
            {
                var allowed = await AccountScopeAsync(member.Id, leads, opportunities, ct);
                accountItems = accountItems.Where(x => allowed.Contains(x.Id)).ToArray();
            }

            var contacts = accountItems
                .SelectMany(account => account.Contacts.Select(contact => ToItem(account.Id, account.Name, contact)))
                .OrderBy(x => x.AccountName)
                .ThenBy(x => x.Name)
                .ToArray();
            return Results.Ok(new { contacts });
        });

        group.MapPost("/accounts/{accountId:guid}/contacts/{contactId:guid}/designation", async (
            Guid accountId,
            Guid contactId,
            UpdateCrmContactDesignationRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ILeadRepository leads,
            ICrmOpportunityStore opportunities,
            ICrmManagementStore management,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (member.Role == CrmRoleCode.SalesExecutive)
            {
                var allowed = await AccountScopeAsync(member.Id, leads, opportunities, ct);
                if (!allowed.Contains(accountId)) return Forbidden("Account is outside your CRM scope.");
            }

            var account = await accounts.GetAsync(DemoTenantId, accountId, ct);
            if (account is null) return Results.NotFound(new ErrorResponse("Account not found."));
            var contact = account.Contacts.FirstOrDefault(x => x.Id == contactId);
            if (contact is null) return Results.NotFound(new ErrorResponse("Contact not found."));

            account.UpdateContact(contact.Id, contact.Name, contact.Email, contact.Phone, contact.IsPrimary, request.Designation);
            await accounts.SaveAsync(account, ct);
            var updated = account.Contacts.First(x => x.Id == contactId);
            await management.AddAuditAsync(new CrmAuditEntry(
                Guid.NewGuid(), DemoTenantId, member.Id, "ContactDesignationUpdated", "Contact",
                contactId.ToString(), $"Account={account.Name}; Designation={updated.Designation ?? "-"}", DateTimeOffset.UtcNow), ct);
            return Results.Ok(ToItem(account.Id, account.Name, updated));
        });

        return app;
    }

    private static async Task<HashSet<Guid>> AccountScopeAsync(
        Guid userId,
        ILeadRepository leads,
        ICrmOpportunityStore opportunities,
        CancellationToken ct)
    {
        var ids = (await opportunities.ListAsync(DemoTenantId, ct))
            .Where(x => x.OwnerUserId == userId)
            .Select(x => x.OrganisationId)
            .ToHashSet();
        foreach (var lead in (await leads.ListAsync(DemoTenantId, ct)).Where(x => x.Attribution.AccountOwnerUserId == userId))
            ids.Add(lead.OrganisationId);
        return ids;
    }

    private static CrmContactDirectoryItem ToItem(Guid accountId, string accountName, BusinessOS.Customers.ContactPerson contact) =>
        new(accountId, accountName, contact.Id, contact.Name, contact.Designation, contact.Email, contact.Phone, contact.IsPrimary);

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
    private static IResult Forbidden(string error) => Results.Json(new ErrorResponse(error), statusCode: StatusCodes.Status403Forbidden);
}

public sealed record UpdateCrmContactDesignationRequest(string? Designation);
public sealed record CrmContactDirectoryItem(
    Guid AccountId,
    string AccountName,
    Guid ContactId,
    string Name,
    string? Designation,
    string? Email,
    string? Phone,
    bool IsPrimary);
