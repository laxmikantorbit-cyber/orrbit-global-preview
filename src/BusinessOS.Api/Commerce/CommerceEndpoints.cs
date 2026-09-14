using BusinessOS.Api.Tenancy;

namespace BusinessOS.Api.Commerce;

public static class CommerceEndpoints
{
    public static IEndpointRouteBuilder MapCommerceActivationEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/commerce");

        group.MapGet("/subscriptions/{subscriptionId:guid}", (
            Guid subscriptionId,
            TenantContext tenant,
            CommerceActivationStore store) =>
        {
            var activation = store.FindActivation(tenant.TenantId, subscriptionId);
            return activation is null
                ? Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."))
                : Results.Ok(activation);
        });

        group.MapPost("/activations/initial", (
            InitialActivationRequest request,
            TenantContext tenant,
            CommerceActivationStore store) => Execute(() =>
                Results.Ok(store.ActivateInitialPurchase(
                    tenant.TenantId,
                    request))));

        group.MapPost("/subscriptions/{subscriptionId:guid}/renewals", (
            Guid subscriptionId,
            RenewalActivationRequest request,
            TenantContext tenant,
            CommerceActivationStore store) => Execute(() =>
        {
            var renewal = store.ActivateRenewal(
                tenant.TenantId,
                subscriptionId,
                request);

            return renewal is null
                ? Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."))
                : Results.Ok(renewal);
        }));

        return app;
    }

    private static IResult Execute(Func<IResult> action)
    {
        try
        {
            return action();
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
