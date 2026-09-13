using BusinessOS.Api.Customers;
using BusinessOS.Api.Tenancy;
using BusinessOS.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<CustomerStore>();

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

app.MapGet("/api/customers", (CustomerStore store) => Results.Ok(store.List()));

app.MapGet("/api/customers/{id:guid}", (Guid id, CustomerStore store) =>
{
    var customer = store.Find(id);
    return customer is null ? Results.NotFound() : Results.Ok(customer);
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
