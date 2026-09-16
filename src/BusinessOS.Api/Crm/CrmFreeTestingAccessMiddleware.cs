using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public sealed class CrmFreeTestingAccessMiddleware
{
    public const string ItemKey = "BusinessOS.CrmAccess";
    public const string DemoUserHeader = "X-CRM-Demo-User-Id";
    private const string Prefix = "/api/testing/public/crm";
    private static readonly Guid DefaultUserId =
        Guid.Parse("11111111-aaaa-1111-1111-111111111111");

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public CrmFreeTestingAccessMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _next = next;
        _configuration = configuration;
        _environment = environment;
    }
    public async Task InvokeAsync(HttpContext context, ICrmTeamRepository team)
    {
        if (!context.Request.Path.StartsWithSegments(Prefix) || !Enabled())
        {
            await _next(context);
            return;
        }

        var userId = ResolveUserId(context);
        var member = await FreeTestingPublicCrmTeamEndpoints.GetActiveMemberAsync(
            team, userId, context.RequestAborted);
        if (member is null)
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden,
                "CRM user is inactive or does not exist.");
            return;
        }

        context.Items[ItemKey] = member;
        var permission = RequiredPermission(context.Request);
        if (permission.HasValue && !CrmRolePolicy.Allows(member.Role, permission.Value))
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden,
                $"{permission.Value} permission is required.");
            return;
        }
        await _next(context);
    }

    public static CrmTeamMember Current(HttpContext context) =>
        context.Items[ItemKey] as CrmTeamMember
        ?? throw new InvalidOperationException("CRM access context is unavailable.");

    public static bool CanViewAllOwnedRecords(CrmTeamMember member) =>
        member.Role is CrmRoleCode.Owner or CrmRoleCode.Admin or CrmRoleCode.SalesManager;

    public static bool CanAccessLead(CrmTeamMember member, Lead lead) =>
        CanViewAllOwnedRecords(member) || lead.Attribution.AccountOwnerUserId == member.Id;

    private bool Enabled() =>
        !_environment.IsProduction() &&
        string.Equals(_configuration["BusinessOS:DeploymentMode"], "FreeTesting",
            StringComparison.OrdinalIgnoreCase);

    private static Guid ResolveUserId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(DemoUserHeader, out var value) &&
            Guid.TryParse(value.ToString(), out var parsed) && parsed != Guid.Empty)
            return parsed;
        return DefaultUserId;
    }

    private static CrmPermission? RequiredPermission(HttpRequest request)
    {
        var value = request.Path.Value ?? string.Empty;
        var relative = value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? value[Prefix.Length..].Trim('/') : string.Empty;
        if (HttpMethods.IsGet(request.Method))
        {
            if (relative.Equals("session", StringComparison.OrdinalIgnoreCase)) return null;
            if (relative is "dashboard" or "work-summary" or "roles") return CrmPermission.ViewDashboard;
            if (relative.Equals("team", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ViewTeam;
            if (relative.Equals("leads", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("leads/", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ViewLeads;
            if (relative.Equals("follow-ups", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ManageFollowUps;
            if (relative.Equals("tasks", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ManageTasks;
            if (relative.Equals("accounts", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("accounts/", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ViewAccounts;
            if (relative.Equals("opportunities", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("opportunities/", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ViewOpportunities;
            return CrmPermission.ViewDashboard;
        }

        if (HttpMethods.IsPost(request.Method))
        {
            if (relative.Equals("leads", StringComparison.OrdinalIgnoreCase)) return CrmPermission.CreateLead;
            if (relative.StartsWith("leads/", StringComparison.OrdinalIgnoreCase))
            {
                if (relative.EndsWith("/assign", StringComparison.OrdinalIgnoreCase)) return CrmPermission.AssignLead;
                if (relative.EndsWith("/follow-ups", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ManageFollowUps;
                if (relative.EndsWith("/convert", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ManageOpportunities;
                return CrmPermission.EditLead;
            }
            if (relative.StartsWith("follow-ups/", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ManageFollowUps;
            if (relative.Equals("tasks", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("tasks/", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ManageTasks;
            if (relative.Equals("accounts", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("accounts/", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ManageAccounts;
            if (relative.Equals("opportunities", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("opportunities/", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ManageOpportunities;
            if (relative.Equals("team", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("team/", StringComparison.OrdinalIgnoreCase)) return CrmPermission.ManageTeam;
        }

        return CrmPermission.ViewDashboard;
    }

    private static async Task DenyAsync(HttpContext context, int status, string error)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error });
    }
}
