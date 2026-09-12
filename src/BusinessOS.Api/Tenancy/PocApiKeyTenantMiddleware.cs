namespace BusinessOS.Api.Tenancy;

public sealed class PocApiKeyTenantMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly IReadOnlyDictionary<string, string> ApiKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant-a-poc-key"] = "TENANT-A",
            ["tenant-b-poc-key"] = "TENANT-B"
        };

    public PocApiKeyTenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("X-POC-Api-Key", out var key) ||
            !ApiKeys.TryGetValue(key.ToString(), out var tenantId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Items[TenantContext.ItemKey] = tenantId;
        await _next(context);
    }
}
