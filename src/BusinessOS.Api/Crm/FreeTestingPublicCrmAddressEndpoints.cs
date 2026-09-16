using BusinessOS.Api.Commerce;
using BusinessOS.Customers;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmAddressEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmAddressEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/accounts/{accountId:guid}/addresses", async (
            Guid accountId,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmAccountStore accounts,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var account = await accounts.GetAsync(DemoTenantId, accountId, cancellationToken);
            if (account is null) return Results.NotFound(new ErrorResponse("Account not found."));
            return Results.Ok(new { addresses = account.Addresses.Select(ToResponse).ToArray() });
        });

        group.MapPost("/accounts/{accountId:guid}/addresses", async (
            Guid accountId,
            SaveCrmAddressRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var account = await accounts.GetAsync(DemoTenantId, accountId, cancellationToken);
            if (account is null) return Results.NotFound(new ErrorResponse("Account not found."));
            try
            {
                var address = new OrganisationAddress(
                    Guid.NewGuid(), request.Line1, request.Line2, request.City,
                    request.State, request.PostalCode, request.CountryCode, request.IsPrimary);
                account.AddAddress(address);
                await accounts.SaveAsync(account, cancellationToken);
                await AuditAsync(management, context, "AddressCreated", address.Id,
                    $"Account={account.Name}; {request.City}, {request.State}; primary={request.IsPrimary}", cancellationToken);
                return Results.Ok(ToResponse(address));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/accounts/{accountId:guid}/addresses/{addressId:guid}/update", async (
            Guid accountId,
            Guid addressId,
            SaveCrmAddressRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var account = await accounts.GetAsync(DemoTenantId, accountId, cancellationToken);
            if (account is null) return Results.NotFound(new ErrorResponse("Account not found."));
            if (account.Addresses.All(x => x.Id != addressId)) return Results.NotFound(new ErrorResponse("Address not found."));
            try
            {
                account.UpdateAddress(addressId, request.Line1, request.Line2, request.City,
                    request.State, request.PostalCode, request.CountryCode, request.IsPrimary);
                await accounts.SaveAsync(account, cancellationToken);
                var address = account.Addresses.Single(x => x.Id == addressId);
                await AuditAsync(management, context, "AddressUpdated", addressId,
                    $"Account={account.Name}; {address.City}, {address.State}; primary={address.IsPrimary}", cancellationToken);
                return Results.Ok(ToResponse(address));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/accounts/{accountId:guid}/addresses/{addressId:guid}/deactivate", async (
            Guid accountId,
            Guid addressId,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var account = await accounts.GetAsync(DemoTenantId, accountId, cancellationToken);
            if (account is null) return Results.NotFound(new ErrorResponse("Account not found."));
            var address = account.Addresses.FirstOrDefault(x => x.Id == addressId);
            if (address is null) return Results.NotFound(new ErrorResponse("Address not found."));
            try
            {
                account.RemoveAddress(addressId);
                await accounts.SaveAsync(account, cancellationToken);
                await AuditAsync(management, context, "AddressDeactivated", addressId,
                    $"Account={account.Name}; removed {address.City}, {address.State}", cancellationToken);
                return Results.Ok(new { removed = true, addressId, addresses = account.Addresses.Select(ToResponse).ToArray() });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        return app;
    }

    private static CrmAddressResponse ToResponse(OrganisationAddress x) => new(
        x.Id, x.Line1, x.Line2, x.City, x.State, x.PostalCode, x.CountryCode, x.IsPrimary);

    private static async Task AuditAsync(
        ICrmManagementStore management,
        HttpContext context,
        string action,
        Guid addressId,
        string detail,
        CancellationToken cancellationToken)
    {
        var member = CrmFreeTestingAccessMiddleware.Current(context);
        await management.AddAuditAsync(new CrmAuditEntry(
            Guid.NewGuid(), DemoTenantId, member.Id, action, "Address", addressId.ToString(), detail, DateTimeOffset.UtcNow), cancellationToken);
    }

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
}

public sealed record SaveCrmAddressRequest(
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string CountryCode,
    bool IsPrimary);

public sealed record CrmAddressResponse(
    Guid Id,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string CountryCode,
    bool IsPrimary);
