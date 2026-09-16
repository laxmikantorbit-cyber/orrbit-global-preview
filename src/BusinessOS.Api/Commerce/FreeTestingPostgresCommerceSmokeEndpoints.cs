using BusinessOS.Api.Payments;
using BusinessOS.Payments;

namespace BusinessOS.Api.Commerce;

public static class FreeTestingPostgresCommerceSmokeEndpoints
{
    private static readonly Guid TenantA =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrganisationA =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlanA =
        Guid.Parse("73333333-3333-3333-3333-333333333331");
    private static readonly Guid PlanVersionA =
        Guid.Parse("74444444-4444-4444-4444-444444444441");

    public static IEndpointRouteBuilder MapFreeTestingPostgresCommerceSmokeEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/testing/postgres/commerce-smoke", ExecuteAsync);
        return app;
    }
    private static async Task<IResult> ExecuteAsync(
        IConfiguration configuration,
        IHostEnvironment environment,
        ICommerceActivationStore store,
        IPaymentEventStore paymentStore,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(configuration, environment))
            return Results.NotFound(new ErrorResponse(
                "Postgres commerce smoke is not enabled."));

        var suffix = Guid.NewGuid().ToString("N");
        var capturedAtUtc = DateTimeOffset.UtcNow;
        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA,
            new InitialActivationRequest(
                OrganisationA, "ORRBIT-REPAIR", PlanA, PlanVersionA, 1,
                100m, "INR", 12, 1, 1, 10, 5, true,
                $"pay_pg_smoke_initial_{suffix}", capturedAtUtc),
            cancellationToken);

        var persisted = await store.FindActivationAsync(
            TenantA, activation.SubscriptionId, cancellationToken);
        if (persisted is null)
            return Results.Problem("Persisted activation was not found.");
        var providerBinding = new ProviderSubscriptionBinding(
            TenantA,
            activation.SubscriptionId,
            "razorpay",
            $"sub_pg_smoke_{suffix}",
            "plan_pg_smoke",
            DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            12,
            "created",
            true,
            false,
            null,
            DateTimeOffset.UtcNow);
        await store.RecordProviderSubscriptionAsync(
            providerBinding, cancellationToken);
        var providerRoute = await store.FindProviderSubscriptionRouteAsync(
            "razorpay",
            providerBinding.ProviderSubscriptionId,
            cancellationToken);
        if (providerRoute is null)
            return Results.Problem("Persisted AutoPay provider route was not found.");

        var providerOrderId = $"order_pg_smoke_{suffix}";
        var paymentMessage = new PaymentWebhookMessage(
            $"evt_pg_smoke_{suffix}",
            $"pay_pg_smoke_{suffix}",
            providerOrderId,
            PaymentStatus.Captured,
            10000,
            "INR",
            DateTimeOffset.UtcNow);
        var paymentInitial = await paymentStore.ProcessAsync(
            "razorpay", paymentMessage, cancellationToken);
        var paymentReplay = await paymentStore.ProcessAsync(
            "razorpay", paymentMessage, cancellationToken);
        var paymentRows = await paymentStore.ListPaymentsForProviderOrdersAsync(
            "razorpay", [providerOrderId], 5, cancellationToken);
        if (paymentInitial.Duplicate || !paymentReplay.Duplicate || paymentRows.Count != 1)
            return Results.Problem("Payment event persistence/idempotency verification failed.");

        var renewal = await store.ActivateRenewalAsync(
            TenantA,
            activation.SubscriptionId,
            new RenewalActivationRequest(
                PlanVersionA,
                2,
                100m,
                "INR",
                12,
                1,
                1,
                20,
                5,
                true,
                $"pay_pg_smoke_renewal_{suffix}",
                DateTimeOffset.UtcNow),
            cancellationToken);
        if (renewal is null)
            return Results.Problem("Persisted renewal was not created.");

        var current = await store.FindActivationAsync(
            TenantA, activation.SubscriptionId, cancellationToken);
        if (current is null)
            return Results.Problem("Persisted renewal readback was not found.");

        return Results.Ok(new PostgresCommerceSmokeResponse(
            activation.SubscriptionId,
            persisted.ValidUntil,
            current.ValidUntil,
            providerRoute.ProviderSubscriptionId,
            true,
            true,
            paymentReplay.Duplicate,
            paymentRows.Count));
    }
    private static bool IsEnabled(
        IConfiguration configuration,
        IHostEnvironment environment) =>
        !environment.IsProduction() &&
        string.Equals(
            configuration["BusinessOS:DeploymentMode"],
            "FreeTesting",
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            configuration["BusinessOS:StorageMode"],
            "Postgres",
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            configuration["BusinessOS:Testing:EnablePostgresSmoke"],
            "true",
            StringComparison.OrdinalIgnoreCase);
}

public sealed record PostgresCommerceSmokeResponse(
    Guid SubscriptionId,
    DateOnly InitialValidUntil,
    DateOnly RenewedValidUntil,
    string ProviderSubscriptionId,
    bool Persisted,
    bool PaymentPersisted,
    bool PaymentDuplicateReplay,
    int PaymentRows);
