using BusinessOS.Api;
using BusinessOS.Api.Billing;
using BusinessOS.Api.Commerce;
using BusinessOS.Api.Crm;
using BusinessOS.Api.Customers;
using BusinessOS.Api.Payments;
using BusinessOS.Api.SoftwareDelivery;
using BusinessOS.Api.Tenancy;
using BusinessOS.Application;
using BusinessOS.Crm;
using BusinessOS.Customers;
using BusinessOS.Identity;
using BusinessOS.Licensing;
using BusinessOS.Payments;
using BusinessOS.Sales;

const string BusinessOsCorsPolicy = "BusinessOSWebsite";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCors(options =>
{
    options.AddPolicy(BusinessOsCorsPolicy, policy =>
    {
        policy.WithOrigins(ResolveAllowedCorsOrigins(builder.Configuration, builder.Environment))
            .WithMethods("GET", "POST", "PUT", "OPTIONS")
            .AllowAnyHeader()
            .SetPreflightMaxAge(TimeSpan.FromHours(1));
    });
});
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<CustomerStore>();
builder.Services.Configure<BillingOptions>(
    builder.Configuration.GetSection("BusinessOS:Billing"));
builder.Services.AddScoped<BillingAutomationService>();
var postgresRuntimeRole = builder.Configuration["BusinessOS:Storage:RuntimeRole"];
var crmConnection = builder.Configuration.GetConnectionString("Crm");
var useFreeTestingPostgres = string.Equals(
    builder.Configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(builder.Configuration["BusinessOS:StorageMode"], "Postgres", StringComparison.OrdinalIgnoreCase);
var allowCrmSchemaBootstrap = !builder.Environment.IsProduction() &&
    (builder.Environment.IsDevelopment() || string.Equals(
        builder.Configuration["BusinessOS:Testing:EnableCrmSchemaBootstrap"],
        "true",
        StringComparison.OrdinalIgnoreCase));
if (string.IsNullOrWhiteSpace(crmConnection) && useFreeTestingPostgres)
    crmConnection = builder.Configuration.GetConnectionString("Commerce");
if (string.IsNullOrWhiteSpace(crmConnection))
{
    builder.Services.AddSingleton<ILeadRepository, InMemoryLeadRepository>();
    builder.Services.AddSingleton<ICrmWorkRepository, InMemoryCrmWorkRepository>();
    builder.Services.AddSingleton<ICrmAccountStore, InMemoryCrmAccountStore>();
    builder.Services.AddSingleton<ICrmOpportunityStore, InMemoryCrmOpportunityStore>();
    builder.Services.AddSingleton<ICrmSalesDocumentStore, InMemoryCrmSalesDocumentStore>();
    builder.Services.AddSingleton<ICrmInvoiceStore, InMemoryCrmInvoiceStore>();
    builder.Services.AddSingleton<ICrmSalesItemStore, InMemoryCrmSalesItemStore>();
    builder.Services.AddSingleton<ICrmCreditNoteStore, InMemoryCrmCreditNoteStore>();
    builder.Services.AddSingleton<ICrmBusinessRecordStore, InMemoryCrmBusinessRecordStore>();
    builder.Services.AddSingleton<ICrmTeamRepository, InMemoryCrmTeamRepository>();
    builder.Services.AddSingleton<ICrmManagementStore, InMemoryCrmManagementStore>();
    builder.Services.AddSingleton<ICrmNotificationStore, InMemoryCrmNotificationStore>();
    builder.Services.AddSingleton<ICrmEntityActivityStore, InMemoryCrmEntityActivityStore>();
}
else
{
    builder.Services.AddSingleton(new CrmPostgresDatabase(
        crmConnection,
        postgresRuntimeRole,
        allowCrmSchemaBootstrap));
    builder.Services.AddSingleton<ILeadRepository, PostgresCrmLeadRepository>();
    builder.Services.AddSingleton<ICrmWorkRepository, PostgresCrmWorkRepository>();
    builder.Services.AddSingleton<ICrmAccountStore, PostgresCrmAccountStore>();
    builder.Services.AddSingleton<ICrmOpportunityStore, PostgresCrmOpportunityStore>();
    builder.Services.AddSingleton<ICrmSalesDocumentStore, PostgresCrmSalesDocumentStore>();
    builder.Services.AddSingleton<ICrmInvoiceStore, PostgresCrmInvoiceStore>();
    builder.Services.AddSingleton<ICrmSalesItemStore, PostgresCrmSalesItemStore>();
    builder.Services.AddSingleton<ICrmCreditNoteStore, PostgresCrmCreditNoteStore>();
    builder.Services.AddSingleton<ICrmBusinessRecordStore, PostgresCrmBusinessRecordStore>();
    builder.Services.AddSingleton<ICrmTeamRepository, PostgresCrmTeamRepository>();
    builder.Services.AddSingleton<ICrmManagementStore, PostgresCrmManagementStore>();
    builder.Services.AddSingleton<ICrmNotificationStore, PostgresCrmNotificationStore>();
    builder.Services.AddSingleton<ICrmEntityActivityStore, PostgresCrmEntityActivityStore>();
}
builder.Services.AddSingleton<LeaseSigner>();
builder.Services.AddSingleton<PaymentProcessor>();
builder.Services.AddSingleton<PaymentSubscriptionActivationService>();
builder.Services.AddHttpClient<IRazorpayOrderClient, FreeTestingAwareRazorpayOrderClient>(client =>
{
    client.BaseAddress = new Uri("https://api.razorpay.com");
});
builder.Services.AddHttpClient<IRazorpaySubscriptionClient, FreeTestingAwareRazorpaySubscriptionClient>(client =>
{
    client.BaseAddress = new Uri("https://api.razorpay.com");
});
builder.Services.AddHttpClient<IRazorpayPaymentClient, RazorpayHttpPaymentClient>(client =>
{
    client.BaseAddress = new Uri("https://api.razorpay.com");
});
builder.Services.AddScoped<RazorpayCheckoutService>();
builder.Services.AddScoped<RazorpayAutoPayService>();
var commerceConnection = builder.Configuration.GetConnectionString("Commerce");
if (string.IsNullOrWhiteSpace(commerceConnection))
{
    builder.Services.AddSingleton<IPaymentEventStore, InMemoryPaymentEventStore>();
    builder.Services.AddSingleton<ICommerceActivationStore, InMemoryCommerceActivationStore>();
    builder.Services.AddSingleton<IProviderOrderConcurrencyGate, InMemoryProviderOrderConcurrencyGate>();
    builder.Services.AddSingleton<IBillingStore, InMemoryBillingStore>();
    builder.Services.AddSingleton<ISoftwareReleaseStore, InMemorySoftwareReleaseStore>();
    builder.Services.AddSingleton<ISoftwareDeliveryEventStore, InMemorySoftwareDeliveryEventStore>();
    builder.Services.AddSingleton<IOrganisationRepository>(_ => CustomerSeed.CreateRepository());
}
else
{
    builder.Services.AddSingleton<IPaymentEventStore>(_ =>
        new PostgresPaymentEventStore(commerceConnection, postgresRuntimeRole));
    builder.Services.AddSingleton<ICommerceActivationStore>(sp =>
        new PostgresCommerceActivationStore(
            commerceConnection,
            sp.GetRequiredService<PaymentSubscriptionActivationService>(),
            sp.GetRequiredService<LeaseSigner>(),
            postgresRuntimeRole));
    builder.Services.AddSingleton<IProviderOrderConcurrencyGate>(_ =>
        new PostgresProviderOrderConcurrencyGate(commerceConnection, postgresRuntimeRole));
    builder.Services.AddSingleton<IBillingStore>(_ =>
        new PostgresBillingStore(
            commerceConnection,
            postgresRuntimeRole,
            allowSchemaBootstrap: false));
    builder.Services.AddSingleton<IOrganisationRepository>(_ =>
        new PostgresOrganisationRepository(commerceConnection, postgresRuntimeRole));
    builder.Services.AddSingleton<ISoftwareReleaseStore>(_ =>
        new PostgresSoftwareReleaseStore(commerceConnection, postgresRuntimeRole));
    builder.Services.AddSingleton<ISoftwareDeliveryEventStore>(_ =>
        new PostgresSoftwareDeliveryEventStore(commerceConnection, postgresRuntimeRole));
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
app.UseMiddleware<CrmFreeTestingAccessMiddleware>();
app.UseMiddleware<CrmAccountScopeMiddleware>();
app.UseMiddleware<CrmOpportunityProductCarryMiddleware>();
app.UseMiddleware<CrmMutationAuditMiddleware>();
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
app.MapDesktopLicenseEndpoints();
app.MapBillingEndpoints();
app.MapOrganisationProfileEndpoints();
app.MapSoftwareDeliveryEndpoints();
app.MapPaymentCheckoutEndpoints();
app.MapFreeTestingPaymentEndpoints();
app.MapFreeTestingPublicCheckoutEndpoints();
app.MapFreeTestingPostgresCommerceSmokeEndpoints();
app.MapFreeTestingPostgresCommerceSmokePageEndpoints();
app.MapFreeTestingPublicCrmEndpoints();
app.MapFreeTestingPublicCrmOperationsEndpoints();
app.MapFreeTestingPublicCrmSalesEndpoints();
app.MapFreeTestingPublicCrmSalesDocumentEndpoints();
app.MapFreeTestingPublicCrmInvoiceEndpoints();
app.MapFreeTestingPublicCrmCreditNoteItemEndpoints();
app.MapFreeTestingPublicCrmBusinessRecordEndpoints();
app.MapFreeTestingPublicCrmAddressEndpoints();
app.MapFreeTestingPublicCrmAdvancedEndpoints();
app.MapFreeTestingPublicCrmNotificationEndpoints();
app.MapFreeTestingPublicCrmEntityActivityEndpoints();
app.MapFreeTestingPublicCrmIntelligenceEndpoints();
app.MapFreeTestingPublicCrmQueryEndpoints();
app.MapFreeTestingPublicCrmAnalyticsEndpoints();
app.MapFreeTestingPublicCrmLeadAgingEndpoints();
app.MapFreeTestingPublicCrmOpportunityAgingEndpoints();
app.MapFreeTestingPublicCrmContactDirectoryEndpoints();
app.MapFreeTestingPublicCrmCommerceEndpoints();
app.MapFreeTestingPublicCrmManagementEndpoints();
app.MapFreeTestingPublicCrmMergeEndpoints();
app.MapFreeTestingPublicCrmOpportunityMaintenanceEndpoints();
app.MapFreeTestingPublicCrmOpportunityProductEndpoints();
app.MapFreeTestingPublicCrmTeamEndpoints();
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
