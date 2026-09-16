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
            if (state is null)
                return Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."));

            var entitlement = EntitlementStatusEvaluator.Evaluate(
                state, DateOnly.FromDateTime(DateTime.UtcNow));
            var autoPay = await store.FindProviderSubscriptionAsync(
                tenant.TenantId, subscriptionId, "razorpay", cancellationToken);
            if (autoPay is not null)
                entitlement = entitlement with
                {
                    AutoRenewEnabled = entitlement.AutoRenewEnabled && autoPay.AutoRenewEnabled,
                    AutoPayProviderStatus = autoPay.Status
                };
            return Results.Ok(entitlement);
        });

        group.MapPost("/subscriptions/{subscriptionId:guid}/cancel-at-period-end", async (
            Guid subscriptionId,
            TenantContext tenant,
            ICommerceActivationStore store,
            RazorpayAutoPayService autoPay,
            CancellationToken cancellationToken) =>
        {
            if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden)
                return forbidden;

            var current = await store.FindSubscriptionStateAsync(
                tenant.TenantId, subscriptionId, cancellationToken);
            if (current is null)
                return Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."));

            try
            {
                await autoPay.CancelAsync(
                    tenant.TenantId, subscriptionId, cancellationToken);
                var state = await store.CancelSubscriptionAtPeriodEndAsync(
                    tenant.TenantId, subscriptionId, cancellationToken);
                return Results.Ok(EntitlementStatusEvaluator.Evaluate(
                    state!, DateOnly.FromDateTime(DateTime.UtcNow)));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapGet("/subscriptions/{subscriptionId:guid}/autopay", async (
            Guid subscriptionId,
            TenantContext tenant,
            ICommerceActivationStore store,
            CancellationToken cancellationToken) =>
        {
            if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden)
                return forbidden;
            var state = await store.FindSubscriptionStateAsync(
                tenant.TenantId, subscriptionId, cancellationToken);
            if (state is null)
                return Results.NotFound(new ErrorResponse(
                    "Subscription was not found for this tenant."));
            var binding = await store.FindProviderSubscriptionAsync(
                tenant.TenantId, subscriptionId, "razorpay", cancellationToken);
            return binding is null
                ? Results.NotFound(new ErrorResponse(
                    "AutoPay is not configured for this subscription."))
                : Results.Ok(binding);
        });
        group.MapPost("/subscriptions/{subscriptionId:guid}/autopay/setup", async (
            Guid subscriptionId,
            TenantContext tenant,
            RazorpayAutoPayService autoPay,
            CancellationToken cancellationToken) =>
        {
            if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden)
                return forbidden;
            try
            {
                var setup = await autoPay.SetupAsync(
                    tenant.TenantId, subscriptionId, cancellationToken);
                return setup is null
                    ? Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."))
                    : Results.Ok(setup);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/subscriptions/{subscriptionId:guid}/autopay/authorize", async (
            Guid subscriptionId,
            AutoPayAuthorizationRequest request,
            TenantContext tenant,
            ICommerceActivationStore store,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden)
                return forbidden;

            if (string.IsNullOrWhiteSpace(request.RazorpayPaymentId) ||
                string.IsNullOrWhiteSpace(request.RazorpaySubscriptionId) ||
                string.IsNullOrWhiteSpace(request.RazorpaySignature))
                return Results.BadRequest(new ErrorResponse("Razorpay authorization fields are required."));

            var state = await store.FindSubscriptionStateAsync(
                tenant.TenantId, subscriptionId, cancellationToken);
            if (state is null)
                return Results.NotFound(new ErrorResponse("Subscription was not found for this tenant."));

            var binding = await store.FindProviderSubscriptionAsync(
                tenant.TenantId, subscriptionId, "razorpay", cancellationToken);
            if (binding is null)
                return Results.NotFound(new ErrorResponse("AutoPay is not configured for this subscription."));

            var terminal = binding.CancelAtPeriodEnd || binding.Status.ToLowerInvariant() is
                "cancelled" or "completed" or "expired" or "halted";
            if (terminal)
                return Results.Conflict(new ErrorResponse("AutoPay authorization is not allowed for this provider state."));
            if (!string.Equals(binding.ProviderSubscriptionId, request.RazorpaySubscriptionId.Trim(),
                StringComparison.Ordinal))
                return Results.BadRequest(new ErrorResponse("Razorpay subscription id does not match AutoPay binding."));

            var secret = configuration["Payments:RazorpayKeySecret"];
            if (string.IsNullOrWhiteSpace(secret))
                return Results.Problem("Razorpay key secret is not configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            if (!BusinessOS.Api.Payments.RazorpaySubscriptionAuthorizationVerifier.Verify(
                request.RazorpayPaymentId, request.RazorpaySubscriptionId, request.RazorpaySignature, secret))
                return Results.Unauthorized();

            var updated = binding;
            if (!string.Equals(binding.Status, "active", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(binding.Status, "authenticated", StringComparison.OrdinalIgnoreCase))
                updated = await store.UpdateProviderSubscriptionStateAsync(
                    "razorpay", binding.ProviderSubscriptionId, "authenticated", true, false, cancellationToken)
                    ?? binding;

            return Results.Ok(new AutoPayAuthorizationResponse(
                tenant.TenantId, subscriptionId, updated.ProviderSubscriptionId,
                updated.Status, updated.AutoRenewEnabled, updated.UpdatedAtUtc));
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
