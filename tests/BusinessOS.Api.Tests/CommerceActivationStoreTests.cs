using BusinessOS.Api.Commerce;
using BusinessOS.Application;
using BusinessOS.Licensing;

namespace BusinessOS.Api.Tests;

public sealed class CommerceActivationStoreTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Initial_Activation_Is_Retrievable_Only_For_Same_Tenant()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);

        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA,
            InitialRequest("pay_initial"));

        Assert.NotNull(await store.FindActivationAsync(TenantA, activation.SubscriptionId));
        Assert.Null(await store.FindActivationAsync(TenantB, activation.SubscriptionId));
        Assert.Equal(new DateOnly(2027, 9, 13), activation.ValidUntil);
        Assert.Equal(10, activation.Entitlements.WebAdminSeats);
    }

    [Fact]
    public async Task Renewal_Extends_Subscription_And_Updates_Entitlements()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);
        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA,
            InitialRequest("pay_initial"));

        var renewal = await store.ActivateRenewalAsync(
            TenantA,
            activation.SubscriptionId,
            RenewalRequest("pay_renewal"));

        Assert.NotNull(renewal);
        Assert.Equal(new DateOnly(2027, 9, 13), renewal!.PreviousValidUntil);
        Assert.Equal(new DateOnly(2028, 9, 13), renewal.NewValidUntil);
        Assert.Equal(20, renewal.Entitlements.WebAdminSeats);

        var current = await store.FindActivationAsync(TenantA, activation.SubscriptionId);
        Assert.NotNull(current);
        Assert.Equal(new DateOnly(2028, 9, 13), current!.ValidUntil);
        Assert.Equal(20, current.Entitlements.WebAdminSeats);
    }

    [Fact]
    public async Task Renewal_For_Other_Tenant_Is_Not_Found()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(),
            signer);
        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA,
            InitialRequest("pay_initial"));

        var renewal = await store.ActivateRenewalAsync(
            TenantB,
            activation.SubscriptionId,
            RenewalRequest("pay_renewal"));

        Assert.Null(renewal);
    }

    private static InitialActivationRequest InitialRequest(string paymentId) => new(
        OrgA,
        "ORRBIT-REPAIR",
        Guid.NewGuid(),
        Guid.NewGuid(),
        1,
        100m,
        "USD",
        12,
        1,
        1,
        10,
        5,
        true,
        paymentId,
        new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

    private static RenewalActivationRequest RenewalRequest(string paymentId) => new(
        Guid.NewGuid(),
        2,
        100m,
        "USD",
        12,
        1,
        1,
        20,
        5,
        true,
        paymentId,
        new DateTimeOffset(2027, 8, 1, 10, 0, 0, TimeSpan.Zero));
}
