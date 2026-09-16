using BusinessOS.Identity;
using Microsoft.Extensions.Primitives;

namespace BusinessOS.Api.Tenancy;

public sealed class TenantAuthenticationMiddleware
{
    private const string PocApiKeyHeader = "X-POC-Api-Key";
    private const string TenantCodeHeader = "X-Tenant-Code";
    private const string AuthSection = "BusinessOS:Auth:BearerTokens";
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    private static readonly IReadOnlyDictionary<string, (string Subject, string TenantCode)> PocCredentials =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["tenant-a-poc-key"] = ("poc-user-a", "TENANT-A"),
            ["tenant-b-poc-key"] = ("poc-user-b", "TENANT-B"),
            ["tenant-a-staff-poc-key"] = ("poc-user-a-staff", "TENANT-A")
        };

    public TenantAuthenticationMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _next = next;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IIdentityAccessRepository repository)
    {
        if (AllowsAnonymous(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var credential = ResolveConfiguredBearerCredential(context) ??
            ResolvePocCredential(context);
        if (credential is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var tenantCode = ResolveTenantCode(context, credential.Value.TenantCode);
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var access = await repository.ResolveAccessAsync(
            credential.Value.Subject,
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

    private static bool AllowsAnonymous(PathString path) =>
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/api/payments/webhooks") ||
        path.StartsWithSegments("/api/payments/checkout") ||
        path.StartsWithSegments("/api/testing/public") ||
        string.Equals(
            path.Value,
            "/api/testing/postgres/commerce-smoke",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            path.Value,
            "/api/desktop/licenses/activate",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            path.Value,
            "/api/desktop/licenses/validate",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            path.Value,
            "/testing/free-checkout",
            StringComparison.OrdinalIgnoreCase);

    private (string Subject, string? TenantCode)? ResolveConfiguredBearerCredential(
        HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var values))
            return null;

        var token = ExtractBearerToken(values);
        if (string.IsNullOrWhiteSpace(token)) return null;

        foreach (var child in _configuration.GetSection(AuthSection).GetChildren())
        {
            var configuredToken = child["Token"];
            var subject = child["Subject"];
            if (string.IsNullOrWhiteSpace(configuredToken) ||
                string.IsNullOrWhiteSpace(subject))
                continue;

            if (FixedTimeEquals(token, configuredToken))
                return (subject.Trim(), child["TenantCode"]?.Trim());
        }

        return null;
    }

    private (string Subject, string TenantCode)? ResolvePocCredential(
        HttpContext context)
    {
        if (!_environment.IsDevelopment() && !AllowPocCredentialsFromConfig())
            return null;
        if (!context.Request.Headers.TryGetValue(PocApiKeyHeader, out var key))
            return null;

        return PocCredentials.TryGetValue(key.ToString(), out var credential)
            ? credential
            : null;
    }

    private bool AllowPocCredentialsFromConfig() =>
        string.Equals(_configuration["BusinessOS:Auth:AllowPocApiKeys"],
            "true", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveTenantCode(
        HttpContext context,
        string? defaultTenantCode) =>
        context.Request.Headers.TryGetValue(TenantCodeHeader, out var requested)
            ? requested.ToString().Trim()
            : defaultTenantCode;

    private static string? ExtractBearerToken(StringValues authorizationValues)
    {
        var authorization = authorizationValues.ToString();
        const string prefix = "Bearer ";
        return authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[prefix.Length..].Trim()
            : null;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                leftBytes,
                rightBytes);
    }
}
