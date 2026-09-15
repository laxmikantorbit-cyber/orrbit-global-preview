using BusinessOS.Api.Tenancy;

namespace BusinessOS.Api.Commerce;

public static class CommerceEndpoints
{
    public static IEndpointRouteBuilder MapCommerceActivationEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/commerce");

        group.MapGet("/subscriptions/{subscriptionId:guid}", async (
            Guid subscriptionId,
            TenantContext tenant,
            ICommerceActivationStore store,
            CancellationToken cancellationToken) =>
        {
            var activation = await store.FindActivationAsync(
                tenant.TenantId,
                subscriptionId,
                cancellationToken);
            return activation is null
                ? Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."))
                : Results.Ok(activation);
        });

        group.MapGet("/subscriptions/{subscriptionId:guid}/entitlement", async (
            Guid subscriptionId,
            TenantContext tenant,
            ICommerceActivationStore store,
            CancellationToken cancellationToken) =>
        {
            var state = await store.FindSubscriptionStateAsync(
                tenant.TenantId, subscriptionId, cancellationToken);
            return state is null
                ? Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."))
                : Results.Ok(EntitlementStatusEvaluator.Evaluate(
                    state, DateOnly.FromDateTime(DateTime.UtcNow)));
        });

        group.MapPost("/subscriptions/{subscriptionId:guid}/cancel-at-period-end", async (
            Guid subscriptionId,
            TenantContext tenant,
            ICommerceActivationStore store,
            CancellationToken cancellationToken) =>
        {
            var state = await store.CancelSubscriptionAtPeriodEndAsync(
                tenant.TenantId, subscriptionId, cancellationToken);
            return state is null
                ? Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."))
                : Results.Ok(EntitlementStatusEvaluator.Evaluate(
                    state, DateOnly.FromDateTime(DateTime.UtcNow)));
        });

        group.MapPost("/checkout/initial", async (
            CreateInitialCheckoutOrderRequest request,
            TenantContext tenant,
            RazorpayCheckoutService checkoutService,
            CancellationToken cancellationToken) => await ExecuteAsync(() =>
                checkoutService.CreateInitialAsync(
                    tenant.TenantId,
                    request,
                    cancellationToken)));

        group.MapPost("/subscriptions/{subscriptionId:guid}/checkout/renewal", async (
            Guid subscriptionId,
            CreateRenewalCheckoutOrderRequest request,
            TenantContext tenant,
            RazorpayCheckoutService checkoutService,
            CancellationToken cancellationToken) =>
        {
            var checkout = await ExecuteNullableAsync(() =>
                checkoutService.CreateRenewalAsync(
                    tenant.TenantId,
                    subscriptionId,
                    request,
                    cancellationToken));

            return checkout is null
                ? Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."))
                : Results.Ok(checkout);
        });

        group.MapPost("/activations/initial", async (
            InitialActivationRequest request,
            TenantContext tenant,
            ICommerceActivationStore store,
            CancellationToken cancellationToken) => await ExecuteAsync(() =>
                store.ActivateInitialPurchaseAsync(
                    tenant.TenantId,
                    request,
                    cancellationToken)));

        group.MapPost("/subscriptions/{subscriptionId:guid}/renewals", async (
            Guid subscriptionId,
            RenewalActivationRequest request,
            TenantContext tenant,
            ICommerceActivationStore store,
            CancellationToken cancellationToken) =>
        {
            var renewal = await ExecuteNullableAsync(() =>
                store.ActivateRenewalAsync(
                    tenant.TenantId,
                    subscriptionId,
                    request,
                    cancellationToken));

            return renewal is null
                ? Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."))
                : Results.Ok(renewal);
        });

        return app;
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
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

    private static async Task<T?> ExecuteNullableAsync<T>(Func<Task<T?>> action)
        where T : class
    {
        try
        {
            return await action();
        }
        catch (ArgumentException ex)
        {
            throw new BadHttpRequestException(ex.Message, StatusCodes.Status400BadRequest);
        }
        catch (InvalidOperationException ex)
        {
            throw new BadHttpRequestException(ex.Message, StatusCodes.Status400BadRequest);
        }
    }
}
