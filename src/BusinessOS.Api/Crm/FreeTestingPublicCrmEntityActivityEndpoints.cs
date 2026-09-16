using BusinessOS.Api.Commerce;
using BusinessOS.Crm;
using BusinessOS.Customers;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmEntityActivityEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly HashSet<string> Channels = new(StringComparer.OrdinalIgnoreCase)
    { "Call", "WhatsApp", "Email", "Meeting", "Note", "Other" };

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmEntityActivityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/activity/{entityType}/{entityId:guid}", async (
            string entityType, Guid entityId,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ILeadRepository leads, ICrmAccountStore accounts, ICrmOpportunityStore opportunities,
            ICrmEntityActivityStore activities, CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var access = await ValidateAccessAsync(entityType, entityId, member, leads, accounts, opportunities, ct);
            if (access.Result is not null) return access.Result;
            var items = entityType.Equals("Contact", StringComparison.OrdinalIgnoreCase)
                ? await activities.ListContactAsync(DemoTenantId, entityId, ct)
                : await activities.ListAsync(DemoTenantId, access.EntityType!, entityId, ct);
            return Results.Ok(new { activities = items });
        });

        group.MapPost("/activity/{entityType}/{entityId:guid}", async (
            string entityType, Guid entityId, CreateCrmEntityActivityRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ILeadRepository leads, ICrmAccountStore accounts, ICrmOpportunityStore opportunities,
            ICrmEntityActivityStore activities, ICrmManagementStore management, CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var channel = request.Channel?.Trim();
            if (string.IsNullOrWhiteSpace(channel) || !Channels.Contains(channel))
                return Results.BadRequest(new ErrorResponse("Channel must be Call, WhatsApp, Email, Meeting, Note or Other."));
            if (string.IsNullOrWhiteSpace(request.Summary))
                return Results.BadRequest(new ErrorResponse("Activity summary is required."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var access = await ValidateAccessAsync(entityType, entityId, member, leads, accounts, opportunities, ct);
            if (access.Result is not null) return access.Result;
            var item = new CrmEntityActivity(
                Guid.NewGuid(), DemoTenantId, access.EntityType!, entityId,
                access.ContactId, channel, request.Summary.Trim(), Clean(request.Details),
                member.Id, request.OccurredAtUtc ?? DateTimeOffset.UtcNow);
            await activities.AddAsync(item, ct);
            await management.AddAuditAsync(new CrmAuditEntry(
                Guid.NewGuid(), DemoTenantId, member.Id, "CommunicationLogged", access.EntityType!, entityId.ToString(),
                $"{item.Channel}: {item.Summary}", DateTimeOffset.UtcNow), ct);
            return Results.Ok(item);
        });

        return app;
    }

    private static async Task<(string? EntityType, Guid? ContactId, IResult? Result)> ValidateAccessAsync(
        string rawType, Guid entityId, CrmTeamMember member,
        ILeadRepository leads, ICrmAccountStore accounts, ICrmOpportunityStore opportunities, CancellationToken ct)
    {
        if (entityId == Guid.Empty) return (null, null, Results.BadRequest(new ErrorResponse("Entity id is required.")));
        if (rawType.Equals("Opportunity", StringComparison.OrdinalIgnoreCase))
        {
            var opportunity = await opportunities.GetAsync(DemoTenantId, entityId, ct);
            if (opportunity is null) return (null, null, Results.NotFound(new ErrorResponse("Opportunity not found.")));
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) && opportunity.OwnerUserId != member.Id)
                return (null, null, Forbidden("Opportunity is outside your CRM scope."));
            return ("Opportunity", null, null);
        }

        if (rawType.Equals("Account", StringComparison.OrdinalIgnoreCase))
        {
            var account = await accounts.GetAsync(DemoTenantId, entityId, ct);
            if (account is null) return (null, null, Results.NotFound(new ErrorResponse("Account not found.")));
            if (!await CanAccessAccountAsync(account.Id, member, leads, opportunities, ct))
                return (null, null, Forbidden("Account is outside your CRM scope."));
            return ("Account", null, null);
        }

        if (rawType.Equals("Contact", StringComparison.OrdinalIgnoreCase))
        {
            var accountList = await accounts.ListAsync(DemoTenantId, ct);
            var account = accountList.FirstOrDefault(x => x.Contacts.Any(c => c.Id == entityId));
            if (account is null) return (null, null, Results.NotFound(new ErrorResponse("Contact not found.")));
            if (!await CanAccessAccountAsync(account.Id, member, leads, opportunities, ct))
                return (null, null, Forbidden("Contact is outside your CRM scope."));
            return ("Contact", entityId, null);
        }

        return (null, null, Results.BadRequest(new ErrorResponse("Entity type must be Account, Contact or Opportunity.")));
    }

    private static async Task<bool> CanAccessAccountAsync(
        Guid accountId, CrmTeamMember member, ILeadRepository leads, ICrmOpportunityStore opportunities, CancellationToken ct)
    {
        if (CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member)) return true;
        var ownedOpportunity = (await opportunities.ListAsync(DemoTenantId, ct))
            .Any(x => x.OrganisationId == accountId && x.OwnerUserId == member.Id);
        if (ownedOpportunity) return true;
        return (await leads.ListAsync(DemoTenantId, ct))
            .Any(x => x.OrganisationId == accountId && x.Attribution.AccountOwnerUserId == member.Id);
    }

    private static IResult Forbidden(string error) => Results.Json(new ErrorResponse(error), statusCode: StatusCodes.Status403Forbidden);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Enabled(IConfiguration c, IHostEnvironment e) => !e.IsProduction() && string.Equals(c["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);
    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
}

public sealed record CreateCrmEntityActivityRequest(string Channel, string Summary, string? Details, DateTimeOffset? OccurredAtUtc);
