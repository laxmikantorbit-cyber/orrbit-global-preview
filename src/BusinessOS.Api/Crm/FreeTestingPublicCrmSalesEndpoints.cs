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
            ICrmAccountStore accounts,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var items = await accounts.ListAsync(DemoTenantId, cancellationToken);
            return Results.Ok(new { accounts = items.Select(ToAccount).OrderBy(x => x.Name).ToArray() });
        });

        group.MapPost("/accounts", async (
            CreateCrmAccountRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            try
            {
                var existing = await accounts.ListAsync(DemoTenantId, cancellationToken);
                var duplicate = FindDuplicateAccount(existing, request.Name, request.Gstin, request.Email, request.Phone);
                if (duplicate.HasValue)
                    return Results.Conflict(new ErrorResponse($"Duplicate customer prevented: {duplicate.Value.Reason} matches '{duplicate.Value.Account.Name}'."));

                var account = new Organisation(Guid.NewGuid(), DemoTenantId, request.Name,
                    request.LegalName, request.Gstin, request.DisplayCode);
                account.AddRole(OrganisationRole.Customer);
                if (!string.IsNullOrWhiteSpace(request.ContactName))
                    account.AddContact(new ContactPerson(Guid.NewGuid(), request.ContactName,
                        request.Email, request.Phone, true));
                await accounts.AddAsync(account, cancellationToken);
                await AuditAsync(management, context, "AccountCreated", "Account", account.Id,
                    $"{account.Name}; GSTIN={account.Gstin ?? "-"}", cancellationToken);
                return Results.Ok(ToAccount(account));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapGet("/accounts/{accountId:guid}", async (
            Guid accountId, IConfiguration configuration, IHostEnvironment environment,
            ICrmAccountStore accounts, CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var account = await accounts.GetAsync(DemoTenantId, accountId, cancellationToken);
            return account is null ? Results.NotFound(new ErrorResponse("Account not found.")) : Results.Ok(ToAccount(account));
        });

        group.MapPost("/accounts/{accountId:guid}/profile", async (
            Guid accountId,
            UpdateCrmAccountRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var account = await accounts.GetAsync(DemoTenantId, accountId, cancellationToken);
            if (account is null) return Results.NotFound(new ErrorResponse("Account not found."));
            try
            {
                var all = await accounts.ListAsync(DemoTenantId, cancellationToken);
                var duplicate = FindDuplicateAccount(all, request.Name, request.Gstin, null, null, accountId);
                if (duplicate.HasValue)
                    return Results.Conflict(new ErrorResponse($"Duplicate customer prevented: {duplicate.Value.Reason} matches '{duplicate.Value.Account.Name}'."));
                account.UpdateProfile(request.Name, request.LegalName, request.Gstin);
                account.SetDisplayCode(request.DisplayCode);
                if (!string.IsNullOrWhiteSpace(request.Status))
                {
                    if (!Enum.TryParse<OrganisationStatus>(request.Status, true, out var status))
                        return Results.BadRequest(new ErrorResponse("Valid account status is required."));
                    account.SetStatus(status);
                }
                await accounts.SaveAsync(account, cancellationToken);
                await AuditAsync(management, context, "AccountUpdated", "Account", account.Id,
                    $"{account.Name}; status={account.Status}; GSTIN={account.Gstin ?? "-"}", cancellationToken);
                return Results.Ok(ToAccount(account));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/accounts/{accountId:guid}/contacts", async (
            Guid accountId,
            AddCrmContactRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var account = await accounts.GetAsync(DemoTenantId, accountId, cancellationToken);
            if (account is null) return Results.NotFound(new ErrorResponse("Account not found."));
            try
            {
                var all = await accounts.ListAsync(DemoTenantId, cancellationToken);
                var contactDuplicate = FindDuplicateContact(all, request.Email, request.Phone, null);
                if (contactDuplicate.HasValue)
                    return Results.Conflict(new ErrorResponse($"Duplicate contact prevented: {contactDuplicate.Value.Reason} already belongs to '{contactDuplicate.Value.Account.Name}'."));
                var contact = new ContactPerson(Guid.NewGuid(), request.Name, request.Email, request.Phone, request.IsPrimary);
                account.AddContact(contact);
                await accounts.SaveAsync(account, cancellationToken);
                await AuditAsync(management, context, "ContactCreated", "Contact", contact.Id,
                    $"Account={account.Name}; {contact.Name}", cancellationToken);
                return Results.Ok(ToAccount(account));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/accounts/{accountId:guid}/contacts/{contactId:guid}/profile", async (
            Guid accountId,
            Guid contactId,
            UpdateCrmContactRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var account = await accounts.GetAsync(DemoTenantId, accountId, cancellationToken);
            if (account is null) return Results.NotFound(new ErrorResponse("Account not found."));
            if (account.Contacts.All(x => x.Id != contactId)) return Results.NotFound(new ErrorResponse("Contact not found."));
            try
            {
                var all = await accounts.ListAsync(DemoTenantId, cancellationToken);
                var contactDuplicate = FindDuplicateContact(all, request.Email, request.Phone, contactId);
                if (contactDuplicate.HasValue)
                    return Results.Conflict(new ErrorResponse($"Duplicate contact prevented: {contactDuplicate.Value.Reason} already belongs to '{contactDuplicate.Value.Account.Name}'."));
                account.UpdateContact(contactId, request.Name, request.Email, request.Phone, request.IsPrimary);
                await accounts.SaveAsync(account, cancellationToken);
                await AuditAsync(management, context, "ContactUpdated", "Contact", contactId,
                    $"Account={account.Name}; {request.Name}", cancellationToken);
                return Results.Ok(ToAccount(account));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/accounts/{accountId:guid}/contacts/{contactId:guid}/deactivate", async (
            Guid accountId,
            Guid contactId,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var account = await accounts.GetAsync(DemoTenantId, accountId, cancellationToken);
            if (account is null) return Results.NotFound(new ErrorResponse("Account not found."));
            var contact = account.Contacts.FirstOrDefault(x => x.Id == contactId);
            if (contact is null) return Results.NotFound(new ErrorResponse("Contact not found."));
            try
            {
                account.RemoveContact(contactId);
                await accounts.SaveAsync(account, cancellationToken);
                await AuditAsync(management, context, "ContactDeactivated", "Contact", contactId,
                    $"Account={account.Name}; removed active contact={contact.Name}", cancellationToken);
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
            HttpContext context,
            ICrmOpportunityStore opportunities,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var items = await opportunities.ListAsync(DemoTenantId, cancellationToken);
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
                items = items.Where(x => x.OwnerUserId == member.Id).ToArray();
            return Results.Ok(new { opportunities = items.Select(ToOpportunity).OrderByDescending(x => x.EstimatedValue).ToArray() });
        });

        group.MapPost("/opportunities", async (
            CreateCrmOpportunityRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmOpportunityStore opportunities,
            ICrmTeamRepository team,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            if (await accounts.GetAsync(DemoTenantId, request.AccountId, cancellationToken) is null)
                return Results.NotFound(new ErrorResponse("Account not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var ownerUserId = request.OwnerUserId;
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
            {
                if (ownerUserId.HasValue && ownerUserId.Value != member.Id)
                    return Results.Json(new ErrorResponse("You can only own your opportunities."), statusCode: StatusCodes.Status403Forbidden);
                ownerUserId = member.Id;
            }
            if (ownerUserId.HasValue && await FreeTestingPublicCrmTeamEndpoints.GetActiveMemberAsync(team, ownerUserId, cancellationToken) is null)
                return Results.BadRequest(new ErrorResponse("Opportunity owner must be an active CRM user."));
            try
            {
                var item = new Opportunity(Guid.NewGuid(), DemoTenantId, request.AccountId,
                    request.Title, Forecast(request), request.OriginatingLeadId, ownerUserId);
                await opportunities.AddAsync(item, cancellationToken);
                await AuditAsync(management, context, "OpportunityCreated", "Opportunity", item.Id,
                    $"{item.Title}; value={item.Forecast.EstimatedValue} {item.Forecast.CurrencyCode}", cancellationToken);
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
            HttpContext context,
            ICrmOpportunityStore opportunities,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await opportunities.GetAsync(DemoTenantId, opportunityId, cancellationToken);
            if (item is null) return Results.NotFound(new ErrorResponse("Opportunity not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) && item.OwnerUserId != member.Id)
                return Results.Json(new ErrorResponse("Opportunity is outside your CRM scope."), statusCode: StatusCodes.Status403Forbidden);
            if (!Enum.TryParse<OpportunityStage>(request.Stage, true, out var stage))
                return Results.BadRequest(new ErrorResponse("Valid opportunity stage is required."));
            try
            {
                var previous = item.Stage;
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
                await opportunities.SaveAsync(item, cancellationToken);
                await AuditAsync(management, context, "OpportunityStageChanged", "Opportunity", item.Id,
                    $"{previous} -> {item.Stage}; reason={request.Reason ?? "-"}", cancellationToken);
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
            HttpContext context,
            ILeadRepository leads,
            ICrmWorkRepository work,
            ICrmAccountStore accounts,
            ICrmOpportunityStore opportunities,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var lead = await leads.GetAsync(DemoTenantId, leadId, cancellationToken);
            if (lead is null) return Results.NotFound(new ErrorResponse("Lead not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanAccessLead(member, lead))
                return Results.Json(new ErrorResponse("Lead is outside your CRM scope."), statusCode: StatusCodes.Status403Forbidden);
            if (lead.Status == LeadStatus.Converted)
                return Results.BadRequest(new ErrorResponse("Lead is already converted."));
            try
            {
                var forecast = new OpportunityForecast(
                    request.EstimatedValue,
                    string.IsNullOrWhiteSpace(request.CurrencyCode) ? "INR" : request.CurrencyCode,
                    request.ProbabilityPercent,
                    request.ExpectedCloseDate);
                var targetName = string.IsNullOrWhiteSpace(request.AccountName) ? lead.Title : request.AccountName.Trim();
                var existingAccounts = await accounts.ListAsync(DemoTenantId, cancellationToken);
                var duplicate = FindDuplicateAccount(existingAccounts, targetName, null, lead.Email, lead.MobileNumber);
                var reusedExistingAccount = duplicate.HasValue;
                Organisation account;
                if (reusedExistingAccount)
                {
                    account = duplicate!.Value.Account;
                    if (!string.IsNullOrWhiteSpace(lead.ContactName) &&
                        !ContactExists(account, lead.Email, lead.MobileNumber))
                    {
                        account.AddContact(new ContactPerson(Guid.NewGuid(), lead.ContactName, lead.Email, lead.MobileNumber,
                            account.PrimaryContact is null));
                        await accounts.SaveAsync(account, cancellationToken);
                    }
                }
                else
                {
                    account = new Organisation(Guid.NewGuid(), DemoTenantId, targetName);
                    account.AddRole(OrganisationRole.Customer);
                    if (!string.IsNullOrWhiteSpace(lead.ContactName))
                        account.AddContact(new ContactPerson(Guid.NewGuid(), lead.ContactName,
                            lead.Email, lead.MobileNumber, true));
                    await accounts.AddAsync(account, cancellationToken);
                }

                var opportunity = new Opportunity(Guid.NewGuid(), DemoTenantId, account.Id,
                    string.IsNullOrWhiteSpace(request.OpportunityTitle) ? lead.Title : request.OpportunityTitle,
                    forecast, lead.Id, lead.Attribution.AccountOwnerUserId);
                await opportunities.AddAsync(opportunity, cancellationToken);
                if (lead.Status == LeadStatus.Unqualified) lead.Reopen(LeadStatus.Qualified);
                else if (lead.Status != LeadStatus.Qualified) lead.Qualify();
                lead.LinkOrganisation(account.Id);
                lead.Convert();
                await leads.SaveAsync(lead, cancellationToken);
                var conversionDetail = $"Account: {account.Name}; Opportunity: {opportunity.Title}; ExistingAccountReused={reusedExistingAccount}";
                await work.AddActivityAsync(new LeadActivity(Guid.NewGuid(), DemoTenantId,
                    lead.Id, CrmActivityType.Converted, "Lead converted to customer",
                    conversionDetail, member.Id), cancellationToken);
                await AuditAsync(management, context, "LeadConverted", "Lead", lead.Id,
                    conversionDetail, cancellationToken);
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

    private static (Organisation Account, string Reason)? FindDuplicateAccount(
        IReadOnlyList<Organisation> accounts,
        string? name,
        string? gstin,
        string? email,
        string? phone,
        Guid? excludeAccountId = null)
    {
        var normalizedName = Normalize(name);
        var normalizedGstin = Normalize(gstin);
        var normalizedEmail = Normalize(email);
        var normalizedPhone = NormalizePhone(phone);
        foreach (var account in accounts.Where(x => !excludeAccountId.HasValue || x.Id != excludeAccountId.Value))
        {
            if (normalizedGstin is not null && Normalize(account.Gstin) == normalizedGstin)
                return (account, "GSTIN");
            if (normalizedEmail is not null && account.Contacts.Any(x => Normalize(x.Email) == normalizedEmail))
                return (account, "email");
            if (normalizedPhone is not null && account.Contacts.Any(x => NormalizePhone(x.Phone) == normalizedPhone))
                return (account, "mobile");
            if (normalizedName is not null && Normalize(account.Name) == normalizedName)
                return (account, "business name");
        }
        return null;
    }

    private static (Organisation Account, string Reason)? FindDuplicateContact(
        IReadOnlyList<Organisation> accounts,
        string? email,
        string? phone,
        Guid? excludeContactId)
    {
        var normalizedEmail = Normalize(email);
        var normalizedPhone = NormalizePhone(phone);
        if (normalizedEmail is null && normalizedPhone is null) return null;
        foreach (var account in accounts)
        foreach (var contact in account.Contacts.Where(x => !excludeContactId.HasValue || x.Id != excludeContactId.Value))
        {
            if (normalizedEmail is not null && Normalize(contact.Email) == normalizedEmail) return (account, "email");
            if (normalizedPhone is not null && NormalizePhone(contact.Phone) == normalizedPhone) return (account, "mobile");
        }
        return null;
    }

    private static bool ContactExists(Organisation account, string? email, string? phone)
    {
        var normalizedEmail = Normalize(email);
        var normalizedPhone = NormalizePhone(phone);
        return account.Contacts.Any(contact =>
            (normalizedEmail is not null && Normalize(contact.Email) == normalizedEmail) ||
            (normalizedPhone is not null && NormalizePhone(contact.Phone) == normalizedPhone));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits.Length > 10 ? digits[^10..] : digits;
    }

    private static async Task AuditAsync(
        ICrmManagementStore management,
        HttpContext context,
        string action,
        string entityType,
        Guid entityId,
        string? detail,
        CancellationToken cancellationToken)
    {
        var member = CrmFreeTestingAccessMiddleware.Current(context);
        await management.AddAuditAsync(new CrmAuditEntry(
            Guid.NewGuid(), DemoTenantId, member.Id, action, entityType, entityId.ToString(), detail, DateTimeOffset.UtcNow), cancellationToken);
    }
}

public sealed record CreateCrmAccountRequest(
    string Name,
    string? LegalName,
    string? Gstin,
    string? DisplayCode,
    string? ContactName,
    string? Email,
    string? Phone);

public sealed record UpdateCrmAccountRequest(
    string Name,
    string? LegalName,
    string? Gstin,
    string? DisplayCode,
    string? Status);

public sealed record AddCrmContactRequest(
    string Name,
    string? Email,
    string? Phone,
    bool IsPrimary);

public sealed record UpdateCrmContactRequest(
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
