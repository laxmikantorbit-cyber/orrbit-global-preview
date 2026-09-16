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
    string DeploymentMode,
    string StorageMode,
    string PaymentsMode,
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
        var checks = new List<string> { "health_endpoint_online" };
        var deploymentMode = ValueOrDefault(
            configuration["BusinessOS:DeploymentMode"],
            environment.IsProduction() ? "Production" : "Standard");
        var storageMode = ValueOrDefault(
            configuration["BusinessOS:StorageMode"],
            string.IsNullOrWhiteSpace(configuration.GetConnectionString("Commerce"))
                ? "InMemory"
                : "Postgres");
        var paymentsMode = ValueOrDefault(
            configuration["BusinessOS:Payments:Mode"],
            string.IsNullOrWhiteSpace(configuration["Payments:RazorpayKeyId"])
                ? "NotConfigured"
                : "Razorpay");

        if (IsFreeTesting(configuration, environment, deploymentMode, storageMode, paymentsMode))
            AddFreeTestingChecks(configuration, missing, checks, storageMode, paymentsMode);
        else
            RequireProductionExternalConfiguration(configuration, missing, checks);
        if (HasBearerToken(configuration))
            checks.Add("bearer_token_configured");
        else
            missing.Add(AuthTokensSection);

        AddUnsafeConfiguration(configuration, environment, unsafeConfig, checks);
        if (missing.Count == 0)
            checks.Add("required_configuration_present_for_current_mode");
        if (unsafeConfig.Count == 0)
            checks.Add("no_unsafe_production_auth_flags");

        return new DeploymentReadinessReport(
            missing.Count == 0 && unsafeConfig.Count == 0,
            environment.EnvironmentName,
            deploymentMode,
            storageMode,
            paymentsMode,
            missing,
            unsafeConfig,
            checks);
    }

    private static bool IsFreeTesting(
        IConfiguration configuration,
        IHostEnvironment environment,
        string deploymentMode,
        string storageMode,
        string paymentsMode) =>
        !environment.IsProduction() &&
        string.Equals(deploymentMode, "FreeTesting", StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(storageMode, "InMemory", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(storageMode, "Postgres", StringComparison.OrdinalIgnoreCase)) &&
        paymentsMode.StartsWith("RazorpayTest", StringComparison.OrdinalIgnoreCase);

    private static void AddFreeTestingChecks(
        IConfiguration configuration,
        ICollection<string> missing,
        ICollection<string> checks,
        string storageMode,
        string paymentsMode)
    {
        checks.Add("free_testing_mode");
        checks.Add($"storage_mode:{storageMode}");
        checks.Add($"payments_mode:{paymentsMode}");
        if (string.Equals(storageMode, "Postgres", StringComparison.OrdinalIgnoreCase))
        {
            Require(configuration, "ConnectionStrings:Commerce", missing);
            if (!missing.Contains("ConnectionStrings:Commerce"))
                checks.Add("commerce_database_configured");
        }
        checks.Add(string.Equals(storageMode, "Postgres", StringComparison.OrdinalIgnoreCase)
            ? "free_testing_postgres_persistence"
            : "external_db_and_live_payment_secrets_deferred_until_release");
    }

    private static void RequireProductionExternalConfiguration(
        IConfiguration configuration,
        ICollection<string> missing,
        ICollection<string> checks)
    {
        Require(configuration, "ConnectionStrings:Commerce", missing);
        Require(configuration, "ConnectionStrings:Identity", missing);
        Require(configuration, "Payments:RazorpayKeyId", missing);
        Require(configuration, "Payments:RazorpayKeySecret", missing);
        Require(configuration, "Payments:RazorpayWebhookSecret", missing);

        if (!missing.Contains("ConnectionStrings:Commerce"))
            checks.Add("commerce_database_configured");        if (!missing.Contains("ConnectionStrings:Identity"))
            checks.Add("identity_database_configured");
        if (!missing.Contains("Payments:RazorpayKeySecret") &&
            !missing.Contains("Payments:RazorpayWebhookSecret"))
            checks.Add("razorpay_configuration_present");
    }

    private static void AddUnsafeConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment,
        ICollection<string> unsafeConfig,
        ICollection<string> checks)
    {
        var pocAllowed = string.Equals(
            configuration["BusinessOS:Auth:AllowPocApiKeys"],
            "true", StringComparison.OrdinalIgnoreCase);
        if (environment.IsProduction() && pocAllowed)
            unsafeConfig.Add("BusinessOS:Auth:AllowPocApiKeys");
        if (!environment.IsProduction() && pocAllowed)
            checks.Add("poc_api_keys_explicitly_enabled_for_non_production");
    }

    private static void Require(
        IConfiguration configuration,
        string key,
        ICollection<string> missing)    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
            missing.Add(key);
    }

    private static bool HasBearerToken(IConfiguration configuration) =>
        configuration.GetSection(AuthTokensSection)
            .GetChildren()
            .Any(x => !string.IsNullOrWhiteSpace(x["Token"]) &&
                !string.IsNullOrWhiteSpace(x["Subject"]));

    private static string ValueOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
