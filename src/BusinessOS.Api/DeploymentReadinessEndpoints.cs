namespace BusinessOS.Api;

public static class DeploymentReadinessEndpoints
{
    public static IEndpointRouteBuilder MapDeploymentReadinessEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/ready", (IConfiguration configuration, IHostEnvironment environment) =>
        {
            var report = DeploymentReadinessReport.Create(configuration, environment);
            return report.Ready
                ? Results.Ok(report)
                : Results.Json(report, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }
}

public sealed record DeploymentReadinessReport(
    bool Ready,
    string Environment,
    IReadOnlyList<string> MissingConfiguration,
    IReadOnlyList<string> UnsafeConfiguration,
    IReadOnlyList<string> Checks)
{
    private const string AuthTokensSection = "BusinessOS:Auth:BearerTokens";
    public static DeploymentReadinessReport Create(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var missing = new List<string>();
        var unsafeConfig = new List<string>();
        var checks = new List<string>
        {
            "health_endpoint_online"
        };

        Require(configuration, "ConnectionStrings:Commerce", missing);
        Require(configuration, "ConnectionStrings:Identity", missing);
        Require(configuration, "Payments:RazorpayKeyId", missing);
        Require(configuration, "Payments:RazorpayKeySecret", missing);
        Require(configuration, "Payments:RazorpayWebhookSecret", missing);

        if (HasBearerToken(configuration))
            checks.Add("bearer_token_configured");
        else
            missing.Add(AuthTokensSection);

        var pocAllowed = string.Equals(
            configuration["BusinessOS:Auth:AllowPocApiKeys"],
            "true", StringComparison.OrdinalIgnoreCase);
        if (environment.IsProduction() && pocAllowed)
            unsafeConfig.Add("BusinessOS:Auth:AllowPocApiKeys");
        if (!environment.IsProduction() && pocAllowed)
            checks.Add("poc_api_keys_explicitly_enabled_for_non_production");

        if (missing.Count == 0)
            checks.Add("required_external_configuration_present");
        if (unsafeConfig.Count == 0)
            checks.Add("no_unsafe_production_auth_flags");

        return new DeploymentReadinessReport(
            missing.Count == 0 && unsafeConfig.Count == 0,
            environment.EnvironmentName,
            missing,
            unsafeConfig,
            checks);
    }

    private static void Require(
        IConfiguration configuration,
        string key,
        ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
            missing.Add(key);
    }

    private static bool HasBearerToken(IConfiguration configuration) =>
        configuration.GetSection(AuthTokensSection)
            .GetChildren()
            .Any(x => !string.IsNullOrWhiteSpace(x["Token"]) &&
                !string.IsNullOrWhiteSpace(x["Subject"]));
}
