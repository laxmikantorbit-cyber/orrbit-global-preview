using BusinessOS.Api.Commerce;
using BusinessOS.Crm;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmEstimateRequestEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DemoOrganisationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmEstimateRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/estimate-requests", async (
            string? q, string? status, string? source, Guid? assignedUserId,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmEstimateRequestStore store, CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var items = await store.ListAsync(DemoTenantId, cancellationToken);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
                items = items.Where(x => x.AssignedUserId == member.Id).ToArray();
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<CrmEstimateRequestStatus>(status, true, out var parsedStatus))
                    return Results.BadRequest(new ErrorResponse("Status must be New, Reviewing, Converted or Closed."));
                items = items.Where(x => x.Status == parsedStatus).ToArray();
            }
            if (!string.IsNullOrWhiteSpace(source))
                items = items.Where(x => x.Source.Equals(source.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (assignedUserId.HasValue)
                items = items.Where(x => x.AssignedUserId == assignedUserId.Value).ToArray();
            if (!string.IsNullOrWhiteSpace(q))
                items = items.Where(x => Match(q, x.Source, x.Requirement, x.ContactName, x.MobileNumber,
                    x.Email, x.BusinessCompany, x.Notes, x.Status.ToString())).ToArray();
            return Results.Ok(new { requests = items.Select(ToResponse).ToArray() });
        });

        group.MapPost("/estimate-requests", async (
            CreateCrmEstimateRequestRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmEstimateRequestStore store, ICrmTeamRepository team, ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var assigned = CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member)
                ? request.AssignedUserId ?? member.Id
                : member.Id;
            var validation = await ValidateAssigneeAsync(assigned, team, cancellationToken);
            if (validation is not null) return validation;
            try
            {
                var item = new CrmEstimateRequest(
                    Guid.NewGuid(), DemoTenantId, request.Source, request.Requirement,
                    request.ContactName, request.MobileNumber, request.Email, request.ExpectedValue, assigned,
                    businessCompany: request.BusinessCompany, notes: request.Notes);
                await store.AddAsync(item, cancellationToken);
                await AuditAsync(management, member.Id, "EstimateRequestCreated", item.Id,
                    $"{item.Source}; {item.ContactName ?? item.Email ?? item.MobileNumber ?? "anonymous"}", cancellationToken);
                return Results.Ok(ToResponse(item));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/estimate-requests/{id:guid}/profile", async (
            Guid id, UpdateCrmEstimateRequestRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmEstimateRequestStore store, ICrmTeamRepository team, ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await store.GetAsync(DemoTenantId, id, cancellationToken);
            if (item is null) return Results.NotFound(new ErrorResponse("Estimate request not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CanAccess(member, item)) return Forbidden("Estimate request is outside your CRM scope.");
            var assigned = CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member)
                ? request.AssignedUserId ?? item.AssignedUserId
                : member.Id;
            var validation = await ValidateAssigneeAsync(assigned, team, cancellationToken);
            if (validation is not null) return validation;
            try
            {
                item.Update(request.Source, request.Requirement, request.ContactName, request.MobileNumber,
                    request.Email, request.ExpectedValue, assigned, request.BusinessCompany, request.Notes);
                await store.SaveAsync(item, cancellationToken);
                await AuditAsync(management, member.Id, "EstimateRequestUpdated", item.Id, item.Status.ToString(), cancellationToken);
                return Results.Ok(ToResponse(item));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/estimate-requests/{id:guid}/assign", async (
            Guid id, AssignCrmEstimateRequestRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmEstimateRequestStore store, ICrmTeamRepository team, ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await store.GetAsync(DemoTenantId, id, cancellationToken);
            if (item is null) return Results.NotFound(new ErrorResponse("Estimate request not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CanAccess(member, item)) return Forbidden("Estimate request is outside your CRM scope.");
            var assigned = CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member)
                ? request.AssignedUserId
                : member.Id;
            var validation = await ValidateAssigneeAsync(assigned, team, cancellationToken);
            if (validation is not null) return validation;
            try
            {
                item.AssignTo(assigned);
                await store.SaveAsync(item, cancellationToken);
                await AuditAsync(management, member.Id, "EstimateRequestAssigned", item.Id,
                    assigned?.ToString() ?? "unassigned", cancellationToken);
                return Results.Ok(ToResponse(item));
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new ErrorResponse(ex.Message)); }
        });

        group.MapPost("/estimate-requests/{id:guid}/status", async (
            Guid id, ChangeCrmEstimateRequestStatusRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmEstimateRequestStore store, ICrmManagementStore management, CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            if (!Enum.TryParse<CrmEstimateRequestStatus>(request.Status, true, out var status))
                return Results.BadRequest(new ErrorResponse("Status must be New, Reviewing, Converted or Closed."));
            var item = await store.GetAsync(DemoTenantId, id, cancellationToken);
            if (item is null) return Results.NotFound(new ErrorResponse("Estimate request not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CanAccess(member, item)) return Forbidden("Estimate request is outside your CRM scope.");
            try
            {
                item.ChangeStatus(status);
                await store.SaveAsync(item, cancellationToken);
                await AuditAsync(management, member.Id, "EstimateRequestStatusChanged", item.Id,
                    item.Status.ToString(), cancellationToken);
                return Results.Ok(ToResponse(item));
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new ErrorResponse(ex.Message)); }
        });

        group.MapPost("/estimate-requests/{id:guid}/review", async (
            Guid id, IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmEstimateRequestStore store, ICrmManagementStore management, CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await store.GetAsync(DemoTenantId, id, cancellationToken);
            if (item is null) return Results.NotFound(new ErrorResponse("Estimate request not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CanAccess(member, item)) return Forbidden("Estimate request is outside your CRM scope.");
            try
            {
                item.StartReview();
                await store.SaveAsync(item, cancellationToken);
                await AuditAsync(management, member.Id, "EstimateRequestReviewStarted", item.Id, null, cancellationToken);
                return Results.Ok(ToResponse(item));
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new ErrorResponse(ex.Message)); }
        });

        group.MapPost("/estimate-requests/{id:guid}/close", async (
            Guid id, IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmEstimateRequestStore store, ICrmManagementStore management, CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await store.GetAsync(DemoTenantId, id, cancellationToken);
            if (item is null) return Results.NotFound(new ErrorResponse("Estimate request not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CanAccess(member, item)) return Forbidden("Estimate request is outside your CRM scope.");
            try
            {
                item.Close();
                await store.SaveAsync(item, cancellationToken);
                await AuditAsync(management, member.Id, "EstimateRequestClosed", item.Id, null, cancellationToken);
                return Results.Ok(ToResponse(item));
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new ErrorResponse(ex.Message)); }
        });

        group.MapPost("/estimate-requests/{id:guid}/convert-to-lead", async (
            Guid id, IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmEstimateRequestStore requests, ILeadRepository leads, ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await requests.GetAsync(DemoTenantId, id, cancellationToken);
            if (item is null) return Results.NotFound(new ErrorResponse("Estimate request not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CanAccess(member, item)) return Forbidden("Estimate request is outside your CRM scope.");
            if (item.Status == CrmEstimateRequestStatus.Converted)
                return Results.Conflict(new ErrorResponse("Estimate request is already converted."));
            if (item.Status == CrmEstimateRequestStatus.Closed)
                return Results.BadRequest(new ErrorResponse("Closed estimate requests cannot be converted."));

            var existing = await leads.ListAsync(DemoTenantId, cancellationToken);
            var phone = Digits(item.MobileNumber);
            var email = Normalize(item.Email);
            var duplicate = existing.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(phone) && Digits(x.MobileNumber) == phone) ||
                (!string.IsNullOrWhiteSpace(email) && Normalize(x.Email) == email));
            if (duplicate is not null)
                return Results.Conflict(new ErrorResponse(
                    $"Duplicate lead detected: {duplicate.Title} ({duplicate.Id}). Mobile/email already exists."));

            try
            {
                var leadId = Guid.NewGuid();
                var title = item.BusinessCompany ?? item.ContactName ?? item.Email ?? item.MobileNumber ?? "Estimate request";
                var leadNotes = $"Converted from estimate request {item.Id}";
                if (!string.IsNullOrWhiteSpace(item.BusinessCompany)) leadNotes += $"; Company: {item.BusinessCompany}";
                if (!string.IsNullOrWhiteSpace(item.Notes)) leadNotes += $"; {item.Notes}";
                var lead = new Lead(
                    leadId, DemoTenantId, DemoOrganisationId, title,
                    new LeadAttribution(item.Source, null, null, null, null, null),
                    item.ContactName, item.MobileNumber, item.Email, item.Requirement,
                    leadNotes, LeadPriority.Normal,
                    estimatedValue: item.ExpectedValue);
                lead.AssignOwner(item.AssignedUserId ?? member.Id);
                await leads.AddAsync(lead, cancellationToken);
                item.MarkConverted(lead.Id);
                await requests.SaveAsync(item, cancellationToken);
                await AuditAsync(management, member.Id, "EstimateRequestConvertedToLead", item.Id,
                    $"lead={lead.Id}", cancellationToken);
                return Results.Ok(new CrmEstimateRequestConversionResponse(ToResponse(item), lead.Id));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/estimate-requests/{id:guid}/convert-to-estimate", async (
            Guid id, ConvertCrmEstimateRequestToEstimateRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmEstimateRequestStore requests, ICrmAccountStore accounts,
            ICrmSalesDocumentStore documents, ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await requests.GetAsync(DemoTenantId, id, cancellationToken);
            if (item is null) return Results.NotFound(new ErrorResponse("Estimate request not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CanAccess(member, item)) return Forbidden("Estimate request is outside your CRM scope.");
            if (!CrmRolePolicy.Allows(member.Role, CrmPermission.ManageSales))
                return Forbidden("Sales management permission is required to create an estimate.");
            if (item.Status == CrmEstimateRequestStatus.Converted)
                return Results.Conflict(new ErrorResponse("Estimate request is already converted."));
            if (item.Status == CrmEstimateRequestStatus.Closed)
                return Results.BadRequest(new ErrorResponse("Closed estimate requests cannot be converted."));
            if (await accounts.GetAsync(DemoTenantId, request.AccountId, cancellationToken) is null)
                return Results.NotFound(new ErrorResponse("Customer not found."));

            try
            {
                var number = await documents.NextNumberAsync(
                    DemoTenantId, SalesDocumentKind.Estimate, cancellationToken);
                var subject = string.IsNullOrWhiteSpace(request.Subject)
                    ? $"Estimate - {item.Requirement}"
                    : request.Subject.Trim();
                var sourceNotes = $"Converted from estimate request {item.Id}";
                if (!string.IsNullOrWhiteSpace(item.BusinessCompany)) sourceNotes += $"; Company: {item.BusinessCompany}";
                if (!string.IsNullOrWhiteSpace(item.Notes)) sourceNotes += $"; {item.Notes}";
                var notes = string.IsNullOrWhiteSpace(request.Notes)
                    ? sourceNotes
                    : $"{sourceNotes}. {request.Notes.Trim()}";
                var document = new SalesDocument(
                    Guid.NewGuid(), DemoTenantId, SalesDocumentKind.Estimate, number,
                    request.AccountId, subject,
                    [new SalesDocumentLine(Guid.NewGuid(), null, item.Requirement, 1m, request.Amount, request.TaxPercent)],
                    "INR", DateOnly.FromDateTime(DateTime.UtcNow), request.ExpiryDate, 0m,
                    null, notes, null);
                await documents.AddAsync(document, cancellationToken);
                item.MarkConvertedToEstimate(document.Id);
                await requests.SaveAsync(item, cancellationToken);
                await AuditAsync(management, member.Id, "EstimateRequestConvertedToEstimate", item.Id,
                    $"estimate={document.Id}; number={document.DocumentNumber}; total={document.Totals.Total:0.00}",
                    cancellationToken);
                return Results.Ok(new CrmEstimateRequestEstimateConversionResponse(
                    ToResponse(item), document.Id, document.DocumentNumber, document.Totals.Total));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        return app;
    }

    private static bool CanAccess(CrmTeamMember member, CrmEstimateRequest item) =>
        CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) || item.AssignedUserId == member.Id;

    private static async Task<IResult?> ValidateAssigneeAsync(
        Guid? assignedUserId, ICrmTeamRepository team, CancellationToken cancellationToken)
    {
        if (!assignedUserId.HasValue) return null;
        var member = await team.GetAsync(DemoTenantId, assignedUserId.Value, cancellationToken);
        return member is null || !member.Active
            ? Results.BadRequest(new ErrorResponse("Assigned CRM user must be active."))
            : null;
    }

    private static CrmEstimateRequestResponse ToResponse(CrmEstimateRequest item) =>
        new(item.Id, item.Source, item.Requirement, item.ContactName, item.MobileNumber, item.Email,
            item.ExpectedValue, item.AssignedUserId, item.BusinessCompany, item.Notes, item.Status.ToString(),
            item.ConvertedLeadId, item.ConvertedEstimateId, item.CreatedAtUtc, item.UpdatedAtUtc);

    private static bool Match(string? query, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var needle = query.Trim();
        return values.Any(value => value?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);
    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
    private static IResult Forbidden(string message) =>
        Results.Json(new ErrorResponse(message), statusCode: StatusCodes.Status403Forbidden);

    private static Task AuditAsync(
        ICrmManagementStore management, Guid actorUserId, string action, Guid entityId,
        string? detail, CancellationToken cancellationToken) =>
        management.AddAuditAsync(new CrmAuditEntry(
            Guid.NewGuid(), DemoTenantId, actorUserId, action, "EstimateRequest",
            entityId.ToString(), detail, DateTimeOffset.UtcNow), cancellationToken);
}

public sealed record CreateCrmEstimateRequestRequest(
    string Source, string Requirement, string? ContactName, string? MobileNumber,
    string? Email, decimal? ExpectedValue, Guid? AssignedUserId,
    string? BusinessCompany, string? Notes);

public sealed record UpdateCrmEstimateRequestRequest(
    string Source, string Requirement, string? ContactName, string? MobileNumber,
    string? Email, decimal? ExpectedValue, Guid? AssignedUserId,
    string? BusinessCompany, string? Notes);

public sealed record AssignCrmEstimateRequestRequest(Guid? AssignedUserId);
public sealed record ChangeCrmEstimateRequestStatusRequest(string Status);

public sealed record CrmEstimateRequestResponse(
    Guid Id, string Source, string Requirement, string? ContactName, string? MobileNumber,
    string? Email, decimal? ExpectedValue, Guid? AssignedUserId,
    string? BusinessCompany, string? Notes, string Status,
    Guid? ConvertedLeadId, Guid? ConvertedEstimateId,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record CrmEstimateRequestConversionResponse(
    CrmEstimateRequestResponse Request, Guid LeadId);

public sealed record ConvertCrmEstimateRequestToEstimateRequest(
    Guid AccountId,
    decimal Amount,
    decimal TaxPercent,
    DateOnly? ExpiryDate,
    string? Subject,
    string? Notes);

public sealed record CrmEstimateRequestEstimateConversionResponse(
    CrmEstimateRequestResponse Request,
    Guid EstimateId,
    string EstimateNumber,
    decimal Total);
