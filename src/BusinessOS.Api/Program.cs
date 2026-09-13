using BusinessOS.Api.Customers;
using BusinessOS.Api.Tenancy;
using BusinessOS.Customers;
using BusinessOS.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<CustomerStore>();
builder.Services.AddSingleton<IOrganisationRepository>(_ => CustomerSeed.CreateRepository());

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

app.Run();

public partial class Program;
