namespace BusinessOS.Api.Commerce;

public static class FreeTestingPostgresCommerceSmokePageEndpoints
{
    public static IEndpointRouteBuilder MapFreeTestingPostgresCommerceSmokePageEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet("/testing/postgres-commerce-smoke", (
            IConfiguration configuration,
            IHostEnvironment environment) =>
        {
            if (!IsEnabled(configuration, environment))
                return Results.NotFound();

            return Results.Content(PageHtml, "text/html; charset=utf-8");
        });
        return app;
    }

    private static bool IsEnabled(
        IConfiguration configuration,
        IHostEnvironment environment) =>
        !environment.IsProduction() &&
        string.Equals(configuration["BusinessOS:DeploymentMode"], "FreeTesting",
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(configuration["BusinessOS:StorageMode"], "Postgres",
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(configuration["BusinessOS:Testing:EnablePostgresSmoke"], "true",
            StringComparison.OrdinalIgnoreCase);
    private const string PageHtml = """
<!doctype html>
<html><head><meta charset='utf-8'><title>BusinessOS Postgres Smoke</title></head>
<body><main><h1>BusinessOS Postgres Smoke</h1><pre id='out'>Running...</pre></main>
<script>
(async () => {
  const out = document.getElementById('out');
  try {
    const response = await fetch('/api/testing/postgres/commerce-smoke', { method: 'POST' });
    const text = await response.text();
    if (!response.ok) throw new Error('HTTP ' + response.status + ': ' + text);
    const data = JSON.parse(text);
    out.textContent = JSON.stringify({
      subscriptionId: data.subscriptionId,
      initialValidUntil: data.initialValidUntil,
      renewedValidUntil: data.renewedValidUntil,
      persisted: data.persisted,
      paymentPersisted: data.paymentPersisted,
      paymentDuplicateReplay: data.paymentDuplicateReplay,
      paymentRows: data.paymentRows
    }, null, 2);
  } catch (error) {
    out.textContent = 'FAILED: ' + error.message;
  }
})();
</script></body></html>
""";
}
