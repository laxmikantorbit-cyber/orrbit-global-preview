using BusinessOS.Application;
using BusinessOS.Catalog;
using BusinessOS.Commerce;
using BusinessOS.Licensing;
using BusinessOS.Payments;

namespace BusinessOS.Application.Tests;

public sealed class PaymentSubscriptionActivationServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrganisationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Captured_Payment_Activates_Subscription_And_License_From_Payment_Date()
    {
        using var signer = new LeaseSigner();
        var service = new PaymentSubscriptionActivationService();
        var paidAt = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
        var order = PendingOrder(termMonths: 12, amount: 100m, webSeats: 10);
        var payment = CapturedPayment(order, "pay_initial", paidAt);

        var result = service.ActivateInitialPurchase(
            payment,
            order,
            Guid.NewGuid(),
            "ORRBIT-REPAIR",
            signer);

        Assert.Equal(OrderStatus.Activated, order.Status);
        Assert.Equal(new DateOnly(2026, 9, 14), result.Subscription.StartsOn);
        Assert.Equal(new DateOnly(2027, 9, 13), result.Subscription.ValidUntil);
        Assert.Equal(result.Subscription.StartsOn, result.License.StartsOn);
        Assert.Equal(result.Subscription.ValidUntil!.Value, result.License.ValidUntil);
        Assert.Equal(10, result.License.Entitlements.WebAdminSeats);
    }

    [Fact]
    public void Renewal_Activation_Extends_Subscription_And_Synchronizes_License()
    {
        using var signer = new LeaseSigner();
        var service = new PaymentSubscriptionActivationService();
        var initialOrder = PendingOrder(termMonths: 12, amount: 100m, webSeats: 10);
        var initialPayment = CapturedPayment(
            initialOrder,
            "pay_initial",
            new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

        var initial = service.ActivateInitialPurchase(
            initialPayment,
            initialOrder,
            Guid.NewGuid(),
            "ORRBIT-REPAIR",
            signer);

        var renewalOrder = PendingOrder(
            termMonths: 12,
            amount: 100m,
            webSeats: 20,
            planId: initial.Subscription.PlanId);
        var renewalPayment = CapturedPayment(
            renewalOrder,
            "pay_renewal",
            new DateTimeOffset(2027, 8, 1, 10, 0, 0, TimeSpan.Zero));

        var renewal = service.ActivateRenewal(
            renewalPayment,
            renewalOrder,
            Guid.NewGuid(),
            initial.Subscription,
            initial.License);

        Assert.Equal(OrderStatus.Activated, renewalOrder.Status);
        Assert.Equal(new DateOnly(2028, 9, 13), renewal.Subscription.ValidUntil);
        Assert.Equal(renewal.Subscription.ValidUntil!.Value, renewal.License.ValidUntil);
        Assert.Equal(20, renewal.License.Entitlements.WebAdminSeats);
        Assert.Single(renewal.Subscription.Renewals);
        Assert.Equal(renewal.Renewal.NewValidUntil, renewal.License.ValidUntil);
    }

    [Fact]
    public void Uncaptured_Payment_Cannot_Activate_Commerce()
    {
        var service = new PaymentSubscriptionActivationService();
        var order = PendingOrder(termMonths: 12, amount: 100m, webSeats: 10);
        var payment = new PaymentRecord(
            "pay_pending",
            order.Id.ToString(),
            PaymentStatus.Pending,
            10000,
            "USD");

        using var signer = new LeaseSigner();
        Assert.Throws<InvalidOperationException>(() => service.ActivateInitialPurchase(
            payment,
            order,
            Guid.NewGuid(),
            "ORRBIT-REPAIR",
            signer));
    }

    private static Order PendingOrder(
        int termMonths,
        decimal amount,
        int webSeats,
        Guid? planId = null)
    {
        var snapshot = Snapshot(termMonths, amount, webSeats, planId);
        var quote = new Quote(
            Guid.NewGuid(),
            TenantId,
            OrganisationId,
            snapshot,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7));
        quote.Accept(DateTimeOffset.UtcNow);
        return quote.CreateOrder(Guid.NewGuid());
    }

    private static PaymentRecord CapturedPayment(
        Order order,
        string paymentId,
        DateTimeOffset capturedAtUtc)
        => new(
            paymentId,
            order.Id.ToString(),
            PaymentStatus.Captured,
            checked(decimal.ToInt64(order.Snapshot.Billing.Amount * 100m)),
            order.Snapshot.Billing.CurrencyCode,
            capturedAtUtc);

    private static CommercialSnapshot Snapshot(
        int termMonths,
        decimal amount,
        int webSeats,
        Guid? planId = null)
    {
        var billing = new BillingRule(amount, "USD", BillingCycle.Annual, termMonths);
        var entitlements = new EntitlementProfile(
            1,
            1,
            webSeats,
            5,
            true,
            new HashSet<string> { "WEB" });
        return new CommercialSnapshot(
            planId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            billing,
            entitlements);
    }
}
