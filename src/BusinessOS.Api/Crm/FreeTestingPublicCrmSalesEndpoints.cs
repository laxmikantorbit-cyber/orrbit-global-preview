using BusinessOS.Api.Commerce;
using BusinessOS.Crm;
using BusinessOS.Customers;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmSalesEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmSalesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/accounts", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            IOrganisationRepository organisations,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var items = await organisations.ListByRoleAsync(DemoTenantId, OrganisationRole.Customer, cancellationToken);
            return Results.Ok(new { accounts = items.Select(ToAccount).OrderBy(x => x.Name).ToArray() });
        });
        group.MapPost("/accounts", async (
            CreateCrmAccountRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            IOrganisationRepository organisations,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            try
            {
                var account = new Organisation(Guid.NewGuid(), DemoTenantId, request.Name,
                    request.LegalName, request.Gstin, request.DisplayCode);
                account.AddRole(OrganisationRole.Customer);
                if (!string.IsNullOrWhiteSpace(request.ContactName))
                    account.AddContact(new ContactPerson(Guid.NewGuid(), request.ContactName,
                        request.Email, request.Phone, true));
                await organisations.AddAsync(account, cancellationToken);
                return Results.Ok(ToAccount(account));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapGet("/accounts/{accountId:guid}", async (
            Guid accountId, IConfiguration configuration, IHostEnvironment environment,
            IOrganisationRepository organisations, CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var account = await organisations.GetAsync(DemoTenantId, accountId, cancellationToken);
            return account is null ? Results.NotFound(new ErrorResponse("Account not found.")) : Results.Ok(ToAccount(account));
        });
        group.MapPost("/accounts/{accountId:guid}/contacts", async (
            Guid accountId,
            AddCrmContactRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            IOrganisationRepository organisations,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var account = await organisations.GetAsync(DemoTenantId, accountId, cancellationToken);
            if (account is null) return Results.NotFound(new ErrorResponse("Account not found."));
            try
            {
                account.AddContact(new ContactPerson(Guid.NewGuid(), request.Name,
                    request.Email, request.Phone, request.IsPrimary));
                return Results.Ok(ToAccount(account));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapGet("/opportunities", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            IOpportunityRepository opportunities,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var items = await opportunities.ListAsync(DemoTenantId, cancellationToken);
            return Results.Ok(new { opportunities = items.Select(ToOpportunity).OrderByDescending(x => x.EstimatedValue).ToArray() });
        });
        group.MapPost("/opportunities", async (
            CreateCrmOpportunityRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            IOrganisationRepository organisations,
            IOpportunityRepository opportunities,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            if (await organisations.GetAsync(DemoTenantId, request.AccountId, cancellationToken) is null)
                return Results.NotFound(new ErrorResponse("Account not found."));
            try
            {
                var item = new Opportunity(Guid.NewGuid(), DemoTenantId, request.AccountId,
                    request.Title, Forecast(request), request.OriginatingLeadId, request.OwnerUserId);
                await opportunities.AddAsync(item, cancellationToken);
                return Results.Ok(ToOpportunity(item));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/opportunities/{opportunityId:guid}/stage", async (
            Guid opportunityId,
            ChangeOpportunityStageRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            IOpportunityRepository opportunities,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await opportunities.GetAsync(DemoTenantId, opportunityId, cancellationToken);
            if (item is null) return Results.NotFound(new ErrorResponse("Opportunity not found."));
            if (!Enum.TryParse<OpportunityStage>(request.Stage, true, out var stage))
                return Results.BadRequest(new ErrorResponse("Valid opportunity stage is required."));
            try
            {
                switch (stage)
                {
                    case OpportunityStage.Won:
                        item.MarkWon();
                        break;
                    case OpportunityStage.Lost:
                        item.MarkLost(request.Reason ?? "Lost");
                        break;
                    default:
                        item.MoveTo(stage);
                        break;
                }
                return Results.Ok(ToOpportunity(item));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/leads/{leadId:guid}/convert", async (
            Guid leadId,
            ConvertCrmLeadRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            ILeadRepository leads,
            ICrmWorkRepository work,
            IOrganisationRepository organisations,
            IOpportunityRepository opportunities,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var lead = await leads.GetAsync(DemoTenantId, leadId, cancellationToken);
            if (lead is null) return Results.NotFound(new ErrorResponse("Lead not found."));
            if (lead.Status == LeadStatus.Converted)
                return Results.BadRequest(new ErrorResponse("Lead is already converted."));
            try
            {
                var forecast = new OpportunityForecast(
                    request.EstimatedValue,
                    string.IsNullOrWhiteSpace(request.CurrencyCode) ? "INR" : request.CurrencyCode,
                    request.ProbabilityPercent,
                    request.ExpectedCloseDate);
                var account = new Organisation(Guid.NewGuid(), DemoTenantId,
                    string.IsNullOrWhiteSpace(request.AccountName) ? lead.Title : request.AccountName);
                account.AddRole(OrganisationRole.Customer);
                if (!string.IsNullOrWhiteSpace(lead.ContactName))
                    account.AddContact(new ContactPerson(Guid.NewGuid(), lead.ContactName,
                        lead.Email, lead.MobileNumber, true));
                var opportunity = new Opportunity(Guid.NewGuid(), DemoTenantId, account.Id,
                    string.IsNullOrWhiteSpace(request.OpportunityTitle) ? lead.Title : request.OpportunityTitle,
                    forecast, lead.Id, lead.Attribution.AccountOwnerUserId);

                await organisations.AddAsync(account, cancellationToken);
                await opportunities.AddAsync(opportunity, cancellationToken);
                if (lead.Status == LeadStatus.Unqualified) lead.Reopen(LeadStatus.Qualified);
                else if (lead.Status != LeadStatus.Qualified) lead.Qualify();
                lead.LinkOrganisation(account.Id);
                lead.Convert();
                await work.AddActivityAsync(new LeadActivity(Guid.NewGuid(), DemoTenantId,
                    lead.Id, CrmActivityType.Converted, "Lead converted to customer",
                    $"Account: {account.Name}; Opportunity: {opportunity.Title}"), cancellationToken);
                return Results.Ok(new CrmConversionResponse(ToAccount(account), ToOpportunity(opportunity)));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        return app;
    }
    private static OpportunityForecast Forecast(CreateCrmOpportunityRequest request) =>
        new(request.EstimatedValue,
            string.IsNullOrWhiteSpace(request.CurrencyCode) ? "INR" : request.CurrencyCode,
            request.ProbabilityPercent,
            request.ExpectedCloseDate);

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() =>
        Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));

    private static CrmAccountResponse ToAccount(Organisation x) => new(
        x.Id, x.Name, x.LegalName, x.Gstin, x.DisplayCode, x.Status.ToString(),
        x.PrimaryContact is null ? null : new CrmContactResponse(x.PrimaryContact.Id,
            x.PrimaryContact.Name, x.PrimaryContact.Email, x.PrimaryContact.Phone, true),
        x.Contacts.Select(c => new CrmContactResponse(c.Id, c.Name, c.Email, c.Phone, c.IsPrimary)).ToArray());

    private static CrmOpportunityResponse ToOpportunity(Opportunity x) => new(
        x.Id, x.OrganisationId, x.OriginatingLeadId, x.Title, x.Stage.ToString(),
        x.Forecast.EstimatedValue, x.Forecast.CurrencyCode, x.Forecast.ProbabilityPercent,
        x.Forecast.ExpectedCloseDate, x.OwnerUserId, x.LossReason);
}
public sealed record CreateCrmAccountRequest(
    string Name,
    string? LegalName,
    string? Gstin,
    string? DisplayCode,
    string? ContactName,
    string? Email,
    string? Phone);

public sealed record AddCrmContactRequest(
    string Name,
    string? Email,
    string? Phone,
    bool IsPrimary);

public sealed record CreateCrmOpportunityRequest(
    Guid AccountId,
    string Title,
    decimal EstimatedValue,
    string CurrencyCode,
    int ProbabilityPercent,
    DateOnly? ExpectedCloseDate,
    Guid? OriginatingLeadId,
    Guid? OwnerUserId);
public sealed record ChangeOpportunityStageRequest(
    string Stage,
    string? Reason);

public sealed record ConvertCrmLeadRequest(
    string? AccountName,
    string? OpportunityTitle,
    decimal EstimatedValue,
    string CurrencyCode,
    int ProbabilityPercent,
    DateOnly? ExpectedCloseDate);

public sealed record CrmContactResponse(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    bool IsPrimary);

public sealed record CrmAccountResponse(
    Guid Id,
    string Name,
    string? LegalName,
    string? Gstin,
    string? DisplayCode,
    string Status,
    CrmContactResponse? PrimaryContact,
    IReadOnlyList<CrmContactResponse> Contacts);
public sealed record CrmOpportunityResponse(
    Guid Id,
    Guid AccountId,
    Guid? OriginatingLeadId,
    string Title,
    string Stage,
    decimal EstimatedValue,
    string CurrencyCode,
    int ProbabilityPercent,
    DateOnly? ExpectedCloseDate,
    Guid? OwnerUserId,
    string? LossReason);

public sealed record CrmConversionResponse(
    CrmAccountResponse Account,
    CrmOpportunityResponse Opportunity);
