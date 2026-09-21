using BusinessOS.Api.Commerce;
using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmBusinessRecordEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmBusinessRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/business-records", async (
            string? module,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmBusinessRecordStore records,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var parsed = ParseModule(module, required: false);
            var result = await records.ListAsync(DemoTenantId, parsed, cancellationToken);
            return Results.Ok(new { records = result.Select(ToResponse).ToArray() });
        });

        group.MapGet("/business-records/{recordId:guid}", async (
            Guid recordId,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmBusinessRecordStore records,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var record = await records.GetAsync(DemoTenantId, recordId, cancellationToken);
            return record is null
                ? Results.NotFound(new ErrorResponse("Business record not found."))
                : Results.Ok(ToResponse(record));
        });

        group.MapPost("/business-records", async (
            CreateCrmBusinessRecordRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmBusinessRecordStore records,
            ICrmAccountStore accounts,
            ICrmTeamRepository team,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            try
            {
                var module = ParseModule(request.Module, required: true)!.Value;
                var validation = await ValidateLinksAsync(request.AccountId, request.OwnerUserId, accounts, team, cancellationToken);
                if (validation is not null) return validation;

                var record = new CrmBusinessRecord(
                    Guid.NewGuid(), DemoTenantId, module, request.Title, request.AccountId,
                    request.Amount, request.Category, request.Priority, request.StartDate,
                    request.DueDate, request.OwnerUserId, request.Description, request.Metadata);
                if (!string.IsNullOrWhiteSpace(request.Status)) record.ChangeStatus(request.Status);
                await records.AddAsync(record, cancellationToken);
                await AuditAsync(management, context, "BusinessRecordCreated", record.Module.ToString(), record.Id,
                    $"{record.Title}; status={record.Status}", cancellationToken);
                return Results.Ok(ToResponse(record));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/business-records/{recordId:guid}/profile", async (
            Guid recordId,
            UpdateCrmBusinessRecordRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmBusinessRecordStore records,
            ICrmAccountStore accounts,
            ICrmTeamRepository team,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var record = await records.GetAsync(DemoTenantId, recordId, cancellationToken);
            if (record is null) return Results.NotFound(new ErrorResponse("Business record not found."));

            var validation = await ValidateLinksAsync(request.AccountId, request.OwnerUserId, accounts, team, cancellationToken);
            if (validation is not null) return validation;

            try
            {
                record.UpdateProfile(
                    request.Title, request.AccountId, request.Amount, request.Category,
                    request.Priority, request.StartDate, request.DueDate, request.OwnerUserId,
                    request.Description, request.Metadata);
                await records.SaveAsync(record, cancellationToken);
                await AuditAsync(management, context, "BusinessRecordUpdated", record.Module.ToString(), record.Id,
                    $"{record.Title}; status={record.Status}", cancellationToken);
                return Results.Ok(ToResponse(record));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/business-records/{recordId:guid}/status", async (
            Guid recordId,
            ChangeCrmBusinessRecordStatusRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmBusinessRecordStore records,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var record = await records.GetAsync(DemoTenantId, recordId, cancellationToken);
            if (record is null) return Results.NotFound(new ErrorResponse("Business record not found."));
            try
            {
                record.ChangeStatus(request.Status);
                await records.SaveAsync(record, cancellationToken);
                await AuditAsync(management, context, "BusinessRecordStatusChanged", record.Module.ToString(), record.Id,
                    $"{record.Title}; status={record.Status}", cancellationToken);
                return Results.Ok(ToResponse(record));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        return app;
    }

    private static CrmBusinessModule? ParseModule(string? value, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) throw new ArgumentException("Business module is required.");
            return null;
        }
        return Enum.TryParse<CrmBusinessModule>(value, true, out var module)
            ? module
            : throw new ArgumentException("Module must be Expense, Contract, Project or Ticket.");
    }

    private static async Task<IResult?> ValidateLinksAsync(
        Guid? accountId,
        Guid? ownerUserId,
        ICrmAccountStore accounts,
        ICrmTeamRepository team,
        CancellationToken cancellationToken)
    {
        if (accountId.HasValue && await accounts.GetAsync(DemoTenantId, accountId.Value, cancellationToken) is null)
            return Results.NotFound(new ErrorResponse("Customer not found."));
        if (ownerUserId.HasValue && await team.GetAsync(DemoTenantId, ownerUserId.Value, cancellationToken) is null)
            return Results.NotFound(new ErrorResponse("Owner team member not found."));
        return null;
    }

    private static CrmBusinessRecordResponse ToResponse(CrmBusinessRecord record) =>
        new(record.Id, record.Module.ToString(), record.AccountId, record.Title, record.Status,
            record.Amount, record.Category, record.Priority, record.StartDate, record.DueDate,
            record.OwnerUserId, record.Description, record.Metadata, record.CreatedAtUtc, record.UpdatedAtUtc);

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() =>
        Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));

    private static async Task AuditAsync(
        ICrmManagementStore management, HttpContext context, string action, string entityType,
        Guid entityId, string? detail, CancellationToken cancellationToken)
    {
        var member = CrmFreeTestingAccessMiddleware.Current(context);
        await management.AddAuditAsync(new CrmAuditEntry(
            Guid.NewGuid(), DemoTenantId, member.Id, action, entityType, entityId.ToString(),
            detail, DateTimeOffset.UtcNow), cancellationToken);
    }
}

public sealed record CreateCrmBusinessRecordRequest(
    string Module,
    string Title,
    Guid? AccountId,
    decimal? Amount,
    string? Category,
    string? Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    Guid? OwnerUserId,
    string? Description,
    IReadOnlyDictionary<string, string>? Metadata,
    string? Status);

public sealed record UpdateCrmBusinessRecordRequest(
    string Title,
    Guid? AccountId,
    decimal? Amount,
    string? Category,
    string? Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    Guid? OwnerUserId,
    string? Description,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record ChangeCrmBusinessRecordStatusRequest(string Status);

public sealed record CrmBusinessRecordResponse(
    Guid Id,
    string Module,
    Guid? AccountId,
    string Title,
    string Status,
    decimal? Amount,
    string? Category,
    string? Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    Guid? OwnerUserId,
    string? Description,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
