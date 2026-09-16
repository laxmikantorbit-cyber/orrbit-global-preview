using System.Text.Json;
using BusinessOS.Api.Commerce;
using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmManagementEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/saved-views", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmManagementStore store,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var views = await store.ListViewsAsync(DemoTenantId, member.Id, cancellationToken);
            return Results.Ok(new { views });
        });

        group.MapPost("/saved-views", async (
            SaveCrmViewRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmManagementStore store,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            if (string.IsNullOrWhiteSpace(request.Module) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new ErrorResponse("View module and name are required."));
            if (!ValidJsonObject(request.FiltersJson))
                return Results.BadRequest(new ErrorResponse("Filters must be a valid JSON object."));

            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var now = DateTimeOffset.UtcNow;
            var id = request.Id is { } requestedId && requestedId != Guid.Empty ? requestedId : Guid.NewGuid();
            var existing = (await store.ListViewsAsync(DemoTenantId, member.Id, cancellationToken))
                .FirstOrDefault(x => x.Id == id);
            var view = new CrmSavedView(
                id,
                DemoTenantId,
                member.Id,
                request.Module.Trim(),
                request.Name.Trim(),
                request.FiltersJson.Trim(),
                request.IsDefault,
                existing?.CreatedAtUtc ?? now,
                now);
            await store.SaveViewAsync(view, cancellationToken);
            await AuditAsync(store, member.Id, existing is null ? "SavedViewCreated" : "SavedViewUpdated",
                "SavedView", view.Id.ToString(), $"{view.Module}:{view.Name}", cancellationToken);
            return Results.Ok(view);
        });

        group.MapPost("/saved-views/{viewId:guid}/delete", async (
            Guid viewId,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmManagementStore store,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            await store.RemoveViewAsync(DemoTenantId, member.Id, viewId, cancellationToken);
            await AuditAsync(store, member.Id, "SavedViewDeleted", "SavedView", viewId.ToString(), null, cancellationToken);
            return Results.Ok(new { deleted = true, id = viewId });
        });

        group.MapGet("/masters", async (
            string? category,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmManagementStore store,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            await EnsureDefaultsAsync(store, cancellationToken);
            var items = await store.ListMastersAsync(DemoTenantId, category, cancellationToken);
            return Results.Ok(new { masters = items });
        });

        group.MapPost("/masters", async (
            SaveCrmMasterRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmManagementStore store,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (member.Role is not (CrmRoleCode.Owner or CrmRoleCode.Admin))
                return Results.Json(new ErrorResponse("Owner or Admin role is required to change CRM masters."), statusCode: StatusCodes.Status403Forbidden);
            if (string.IsNullOrWhiteSpace(request.Category) || string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new ErrorResponse("Category, code and name are required."));

            var existing = (await store.ListMastersAsync(DemoTenantId, request.Category, cancellationToken))
                .FirstOrDefault(x => x.Code.Equals(request.Code.Trim(), StringComparison.OrdinalIgnoreCase));
            var item = new CrmMasterItem(
                existing?.Id ?? (request.Id is { } requestedId && requestedId != Guid.Empty ? requestedId : Guid.NewGuid()),
                DemoTenantId,
                request.Category.Trim(),
                request.Code.Trim(),
                request.Name.Trim(),
                request.Active,
                request.SortOrder,
                DateTimeOffset.UtcNow);
            await store.SaveMasterAsync(item, cancellationToken);
            await AuditAsync(store, member.Id, existing is null ? "MasterCreated" : "MasterUpdated",
                "CrmMaster", item.Id.ToString(), $"{item.Category}:{item.Code}:{item.Name}:active={item.Active}", cancellationToken);
            return Results.Ok(item);
        });

        group.MapGet("/audit", async (
            int? take,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmManagementStore store,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var entries = await store.ListAuditAsync(DemoTenantId, take ?? 100, cancellationToken);
            return Results.Ok(new { audit = entries });
        });

        return app;
    }

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));

    private static bool ValidJsonObject(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task AuditAsync(
        ICrmManagementStore store,
        Guid actorUserId,
        string action,
        string entityType,
        string? entityId,
        string? detail,
        CancellationToken cancellationToken)
    {
        await store.AddAuditAsync(new CrmAuditEntry(
            Guid.NewGuid(), DemoTenantId, actorUserId, action, entityType, entityId, detail, DateTimeOffset.UtcNow), cancellationToken);
    }

    private static async Task EnsureDefaultsAsync(ICrmManagementStore store, CancellationToken cancellationToken)
    {
        var existing = await store.ListMastersAsync(DemoTenantId, null, cancellationToken);
        if (existing.Count > 0) return;

        var seeds = new (string Category, string Code, string Name, int Sort)[]
        {
            ("LeadSource", "WHATSAPP", "WhatsApp", 10),
            ("LeadSource", "WEBSITE", "Website", 20),
            ("LeadSource", "CALL", "Inbound Call", 30),
            ("LeadSource", "PARTNER", "Partner / Reseller", 40),
            ("LeadSource", "REFERENCE", "Reference", 50),
            ("Priority", "LOW", "Low", 10),
            ("Priority", "NORMAL", "Normal", 20),
            ("Priority", "HIGH", "High", 30),
            ("Priority", "URGENT", "Urgent", 40),
            ("FollowUpChannel", "CALL", "Call", 10),
            ("FollowUpChannel", "WHATSAPP", "WhatsApp", 20),
            ("FollowUpChannel", "EMAIL", "Email", 30),
            ("FollowUpChannel", "MEETING", "Meeting", 40),
            ("FollowUpChannel", "OTHER", "Other", 50),
            ("TaskType", "FOLLOWUP", "Follow-up", 10),
            ("TaskType", "DEMO", "Demo", 20),
            ("TaskType", "PAYMENT", "Payment", 30),
            ("TaskType", "DOCUMENT", "Document", 40),
            ("OpportunityStage", "DISCOVERY", "Discovery", 10),
            ("OpportunityStage", "SOLUTIONFIT", "Solution Fit", 20),
            ("OpportunityStage", "PROPOSAL", "Proposal", 30),
            ("OpportunityStage", "NEGOTIATION", "Negotiation", 40),
            ("OpportunityStage", "WON", "Won", 50),
            ("OpportunityStage", "LOST", "Lost", 60),
            ("LostReason", "PRICE", "Price", 10),
            ("LostReason", "COMPETITOR", "Competitor", 20),
            ("LostReason", "TIMING", "Not ready / timing", 30),
            ("LostReason", "NO_RESPONSE", "No response", 40),
            ("LostReason", "OTHER", "Other", 50)
        };

        foreach (var seed in seeds)
            await store.SaveMasterAsync(new CrmMasterItem(
                Guid.NewGuid(), DemoTenantId, seed.Category, seed.Code, seed.Name, true, seed.Sort, DateTimeOffset.UtcNow), cancellationToken);
    }
}

public sealed record SaveCrmViewRequest(
    Guid? Id,
    string Module,
    string Name,
    string FiltersJson,
    bool IsDefault);

public sealed record SaveCrmMasterRequest(
    Guid? Id,
    string Category,
    string Code,
    string Name,
    bool Active,
    int SortOrder);
