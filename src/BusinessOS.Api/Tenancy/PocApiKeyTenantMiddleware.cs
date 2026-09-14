using BusinessOS.Identity;

namespace BusinessOS.Api.Tenancy;

public sealed class PocApiKeyTenantMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly IReadOnlyDictionary<string, (string Subject, string TenantCode)> Credentials =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["tenant-a-poc-key"] = ("poc-user-a", "TENANT-A"),
            ["tenant-b-poc-key"] = ("poc-user-b", "TENANT-B")
        };

    public PocApiKeyTenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IIdentityAccessRepository repository)
    {
        if (context.Request.Path.StartsWithSegments("/api/payments/webhooks"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-POC-Api-Key", out var key) ||
            !Credentials.TryGetValue(key.ToString(), out var credential))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var tenantCode = context.Request.Headers.TryGetValue("X-Tenant-Code", out var requested)
            ? requested.ToString()
            : credential.TenantCode;

        var access = await repository.ResolveAccessAsync(
            credential.Subject,
            tenantCode,
            context.RequestAborted);

        if (access is null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Items[TenantContext.ItemKey] = access;
        await _next(context);
    }
}
