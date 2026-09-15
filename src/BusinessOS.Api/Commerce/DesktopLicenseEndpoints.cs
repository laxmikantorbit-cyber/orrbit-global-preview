using BusinessOS.Api.Tenancy;

namespace BusinessOS.Api.Commerce;

public static class DesktopLicenseEndpoints
{
    public static IEndpointRouteBuilder MapDesktopLicenseEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/desktop/licenses");

        group.MapPost("/{subscriptionId:guid}/activate", async (
            Guid subscriptionId,
            DesktopDeviceActivationRequest request,
            TenantContext tenant,
            ICommerceActivationStore store,
            CancellationToken cancellationToken) => await ExecuteNullableAsync(() =>
                store.ActivateDesktopDeviceAsync(
                    tenant.TenantId,
                    subscriptionId,
                    request,
                    cancellationToken),
                "Subscription was not found for this tenant."));

        group.MapPost("/{subscriptionId:guid}/validate", async (
            Guid subscriptionId,
            DesktopDeviceValidationRequest request,
            TenantContext tenant,
            ICommerceActivationStore store,
            CancellationToken cancellationToken) => await ExecuteNullableAsync(() =>
                store.ValidateDesktopDeviceAsync(
                    tenant.TenantId,
                    subscriptionId,
                    request,
                    cancellationToken),
                "Subscription was not found for this tenant."));

        return app;
    }

    private static async Task<IResult> ExecuteNullableAsync<T>(
        Func<Task<T?>> action,
        string notFoundMessage)
        where T : class
    {
        try
        {
            var result = await action();
            return result is null
                ? Results.NotFound(new ErrorResponse(notFoundMessage))
                : Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }
}
