using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public sealed class CrmMutationAuditMiddleware
{
    private const string Prefix = "/api/testing/public/crm";
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CrmMutationAuditMiddleware> _logger;

    public CrmMutationAuditMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<CrmMutationAuditMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICrmManagementStore management)
    {
        var auditCandidate = Enabled() &&
            HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.StartsWithSegments(Prefix);

        await _next(context);

        if (!auditCandidate || context.Response.StatusCode >= 400) return;
        if (context.Items[CrmFreeTestingAccessMiddleware.ItemKey] is not CrmTeamMember member) return;

        try
        {
            var relative = context.Request.Path.Value?[Prefix.Length..].Trim('/') ?? string.Empty;
            var entityType = ResolveEntityType(relative);
            var entityId = ResolveEntityId(relative);
            await management.AddAuditAsync(new CrmAuditEntry(
                Guid.NewGuid(),
                FreeTestingPublicCrmTeamEndpoints.DemoTenantId,
                member.Id,
                $"ApiMutation:{context.Request.Method}",
                entityType,
                entityId,
                $"route={relative}; status={context.Response.StatusCode}",
                DateTimeOffset.UtcNow),
                context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CRM mutation audit write failed for {Path}", context.Request.Path);
        }
    }

    private bool Enabled() =>
        !_environment.IsProduction() &&
        string.Equals(_configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static string ResolveEntityType(string relative)
    {
        if (relative.StartsWith("leads", StringComparison.OrdinalIgnoreCase)) return "Lead";
        if (relative.StartsWith("follow-ups", StringComparison.OrdinalIgnoreCase)) return "FollowUp";
        if (relative.StartsWith("tasks", StringComparison.OrdinalIgnoreCase)) return "Task";
        if (relative.StartsWith("accounts", StringComparison.OrdinalIgnoreCase)) return "Account";
        if (relative.StartsWith("opportunities", StringComparison.OrdinalIgnoreCase)) return "Opportunity";
        if (relative.StartsWith("team", StringComparison.OrdinalIgnoreCase)) return "TeamMember";
        if (relative.StartsWith("masters", StringComparison.OrdinalIgnoreCase)) return "CrmMaster";
        if (relative.StartsWith("saved-views", StringComparison.OrdinalIgnoreCase)) return "SavedView";
        return "CRM";
    }

    private static string? ResolveEntityId(string relative)
    {
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Skip(1).FirstOrDefault(x => Guid.TryParse(x, out _));
    }
}
