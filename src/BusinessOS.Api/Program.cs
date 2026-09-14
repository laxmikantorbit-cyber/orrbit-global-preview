using BusinessOS.Api.Commerce;
using BusinessOS.Api.Customers;
using BusinessOS.Api.Payments;
using BusinessOS.Api.Tenancy;
using BusinessOS.Application;
using BusinessOS.Customers;
using BusinessOS.Identity;
using BusinessOS.Licensing;
using BusinessOS.Payments;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<CustomerStore>();
builder.Services.AddSingleton<IOrganisationRepository>(_ => CustomerSeed.CreateRepository());
builder.Services.AddSingleton<LeaseSigner>();
builder.Services.AddSingleton<PaymentProcessor>();
builder.Services.AddSingleton<PaymentSubscriptionActivationService>();
builder.Services.AddHttpClient<IRazorpayOrderClient, RazorpayHttpOrderClient>(client =>
{
    client.BaseAddress = new Uri("https://api.razorpay.com");
});
builder.Services.AddScoped<RazorpayCheckoutService>();
var commerceConnection = builder.Configuration.GetConnectionString("Commerce");
if (string.IsNullOrWhiteSpace(commerceConnection))
{
    builder.Services.AddSingleton<ICommerceActivationStore, InMemoryCommerceActivationStore>();
}
else
{
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
app.UseMiddleware<PocApiKeyTenantMiddleware>();
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
app.MapCommerceActivationEndpoints();
app.MapPaymentWebhookEndpoints();

app.Run();

public partial class Program;
