using BusinessOS.Api;
using BusinessOS.Api.Commerce;
using BusinessOS.Api.Customers;
using BusinessOS.Api.Payments;
using BusinessOS.Api.Tenancy;
using BusinessOS.Application;
using BusinessOS.Customers;
using BusinessOS.Identity;
using BusinessOS.Licensing;
using BusinessOS.Payments;

const string BusinessOsCorsPolicy = "BusinessOSWebsite";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCors(options =>
{
    options.AddPolicy(BusinessOsCorsPolicy, policy =>
    {
        policy.WithOrigins(ResolveAllowedCorsOrigins(builder.Configuration, builder.Environment))
            .WithMethods("GET", "POST", "OPTIONS")
            .AllowAnyHeader()
            .SetPreflightMaxAge(TimeSpan.FromHours(1));
    });
});
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<CustomerStore>();
builder.Services.AddSingleton<IOrganisationRepository>(_ => CustomerSeed.CreateRepository());
builder.Services.AddSingleton<LeaseSigner>();
builder.Services.AddSingleton<PaymentProcessor>();
builder.Services.AddSingleton<PaymentSubscriptionActivationService>();
builder.Services.AddHttpClient<IRazorpayOrderClient, FreeTestingAwareRazorpayOrderClient>(client =>
{
    client.BaseAddress = new Uri("https://api.razorpay.com");
});
builder.Services.AddHttpClient<IRazorpayPaymentClient, RazorpayHttpPaymentClient>(client =>
{
    client.BaseAddress = new Uri("https://api.razorpay.com");
});
builder.Services.AddScoped<RazorpayCheckoutService>();
var commerceConnection = builder.Configuration.GetConnectionString("Commerce");
if (string.IsNullOrWhiteSpace(commerceConnection))
{
    builder.Services.AddSingleton<IPaymentEventStore, InMemoryPaymentEventStore>();
    builder.Services.AddSingleton<ICommerceActivationStore, InMemoryCommerceActivationStore>();
}
else
{
    builder.Services.AddSingleton<IPaymentEventStore>(_ =>
        new PostgresPaymentEventStore(commerceConnection));
    builder.Services.AddSingleton<ICommerceActivationStore>(sp =>
        new PostgresCommerceActivationStore(
            commerceConnection,
            sp.GetRequiredService<PaymentSubscriptionActivationService>(),
            sp.GetRequiredService<LeaseSigner>()));
}

var identityConnection = builder.Configuration.GetConnectionString("Identity");
if (string.IsNullOrWhiteSpace(identityConnection))
{
    builder.Services.AddSingleton(PocIdentitySeed.CreateDirectory());
    builder.Services.AddSingleton<IIdentityAccessRepository, InMemoryIdentityAccessRepository>();
}
else
{
    builder.Services.AddSingleton<IIdentityAccessRepository>(
        _ => new PostgresIdentityAccessRepository(identityConnection));
}

var app = builder.Build();

if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseCors(BusinessOsCorsPolicy);
app.UseMiddleware<TenantAuthenticationMiddleware>();
app.MapGet("/api/customers", async (
    CustomerStore store,
    CancellationToken cancellationToken) =>
    Results.Ok(await store.ListAsync(cancellationToken)));

app.MapGet("/api/customers/{id:guid}", async (
    Guid id,
    CustomerStore store,
    CancellationToken cancellationToken) =>
{
    var customer = await store.FindAsync(id, cancellationToken);
    return customer is null ? Results.NotFound() : Results.Ok(customer);
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapDeploymentReadinessEndpoints();
app.MapCommerceActivationEndpoints();
app.MapCommerceAdminEndpoints();
app.MapPaymentCheckoutEndpoints();
app.MapFreeTestingPaymentEndpoints();
app.MapFreeTestingPublicCheckoutEndpoints();
app.MapFreeTestingCheckoutPageEndpoints();
app.MapPaymentWebhookEndpoints();

app.Run();

static string[] ResolveAllowedCorsOrigins(
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    var configured = configuration.GetSection("BusinessOS:Cors:AllowedOrigins")
        .Get<string[]>()?
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim().TrimEnd('/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (configured is { Length: > 0 }) return configured;

    var defaults = new List<string>
    {
        "https://orrbitrepair.com",
        "https://www.orrbitrepair.com"
    };
    if (!environment.IsProduction())
    {
        defaults.Add("https://businessos-commerce-api-live.onrender.com");
        defaults.Add("https://businessos-web-staging-checkout.onrender.com");
        defaults.Add("http://localhost:3000");
        defaults.Add("http://localhost:5173");
    }

    return defaults.ToArray();
}

public partial class Program;
