using BusinessOS.Api.Commerce;
using BusinessOS.Application;
using BusinessOS.Licensing;

namespace BusinessOS.Api.Tests;

public sealed class PostgresCommerceActivationStoreSmokeTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlanA = Guid.Parse("73333333-3333-3333-3333-333333333331");
    private static readonly Guid PlanVersionA = Guid.Parse("74444444-4444-4444-4444-444444444441");

    [Fact]
    public async Task Postgres_Store_Persists_Initial_Activation_And_Renewal_When_Configured()
    {
        var cs = Environment.GetEnvironmentVariable("BUSINESSOS_COMMERCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
            return;

        using var signer = new LeaseSigner();
        await using var store = new PostgresCommerceActivationStore(
            cs,
            new PaymentSubscriptionActivationService(),
            signer);

        var suffix = Guid.NewGuid().ToString("N");
        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA,
            InitialRequest($"pay_pg_initial_{suffix}"));

        var persisted = await store.FindActivationAsync(TenantA, activation.SubscriptionId);
        var state = await store.FindSubscriptionStateAsync(TenantA, activation.SubscriptionId);
        var otherTenant = await store.FindActivationAsync(TenantB, activation.SubscriptionId);

        Assert.NotNull(persisted);
        Assert.NotNull(state);
        Assert.Null(otherTenant);
        Assert.Equal(activation.LicenseId, state!.LicenseId);
        Assert.Equal(BusinessOS.Commerce.SubscriptionStatus.Active, state.SubscriptionStatus);
        Assert.Equal(new DateOnly(2027, 9, 13), persisted!.ValidUntil);
        Assert.Equal(10, persisted.Entitlements.WebAdminSeats);
        var initialTemplate = await store.FindAutoPayRenewalTemplateAsync(
            TenantA, activation.SubscriptionId);
        Assert.NotNull(initialTemplate);
        Assert.Equal(100m, initialTemplate!.Amount);
        Assert.Equal("INR", initialTemplate.CurrencyCode);
        Assert.Equal(10, initialTemplate.WebAdminSeats);

        var renewal = await store.ActivateRenewalAsync(
            TenantA,
            activation.SubscriptionId,
            RenewalRequest($"pay_pg_renewal_{suffix}"));
        var current = await store.FindActivationAsync(TenantA, activation.SubscriptionId);

        Assert.NotNull(renewal);
        Assert.NotNull(current);
        Assert.Equal(new DateOnly(2028, 9, 13), renewal!.NewValidUntil);
        Assert.Equal(new DateOnly(2028, 9, 13), current!.ValidUntil);
        Assert.Equal(20, current.Entitlements.WebAdminSeats);
        var renewalTemplate = await store.FindAutoPayRenewalTemplateAsync(
            TenantA, activation.SubscriptionId);
        Assert.NotNull(renewalTemplate);
        Assert.Equal(100m, renewalTemplate!.Amount);
        Assert.Equal("INR", renewalTemplate.CurrencyCode);
        Assert.Equal(20, renewalTemplate.WebAdminSeats);

        var cancelled = await store.CancelSubscriptionAtPeriodEndAsync(
            TenantA, activation.SubscriptionId);
        var persistedCancelled = await store.FindSubscriptionStateAsync(
            TenantA, activation.SubscriptionId);

        Assert.NotNull(cancelled);
        Assert.NotNull(persistedCancelled);
        Assert.Equal(BusinessOS.Commerce.SubscriptionStatus.Cancelled,
            cancelled!.SubscriptionStatus);
        Assert.Equal(BusinessOS.Commerce.SubscriptionStatus.Cancelled,
            persistedCancelled!.SubscriptionStatus);
        Assert.Null(await store.CancelSubscriptionAtPeriodEndAsync(
            TenantB, activation.SubscriptionId));
    }

    private static InitialActivationRequest InitialRequest(string paymentId) => new(
        OrgA,
        "ORRBIT-REPAIR",
        PlanA,
        PlanVersionA,
        1,
        100m,
        "INR",
        12,
        1,
        1,
        10,
        5,
        true,
        paymentId,
        new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

    private static RenewalActivationRequest RenewalRequest(string paymentId) => new(
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
        paymentId,
        new DateTimeOffset(2027, 8, 1, 10, 0, 0, TimeSpan.Zero));
}
