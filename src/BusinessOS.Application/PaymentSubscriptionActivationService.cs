using BusinessOS.Commerce;
using BusinessOS.Licensing;
using BusinessOS.Payments;

namespace BusinessOS.Application;

public sealed record InitialActivationResult(
    SubscriptionEntitlement Subscription,
    LicenseEngine License);

public sealed record RenewalActivationResult(
    SubscriptionRenewal Renewal,
    SubscriptionEntitlement Subscription,
    LicenseEngine License);

public sealed class PaymentSubscriptionActivationService
{
    public InitialActivationResult ActivateInitialPurchase(
        PaymentRecord payment,
        Order order,
        Guid subscriptionId,
        string productCode,
        LeaseSigner signer)
    {
        ValidateCapturedPayment(payment, order);
        order.CapturePayment(payment.PaymentId, payment.CapturedAtUtc!.Value);
        var subscription = order.Activate(subscriptionId);
        var validUntil = subscription.ValidUntil
            ?? throw new InvalidOperationException("License-backed subscriptions require a finite paid term.");

        var license = LicenseEngine.FromVerifiedSubscription(
            productCode,
            subscription.StartsOn,
            validUntil,
            subscription.Entitlements,
            signer);

        return new InitialActivationResult(subscription, license);
    }

    public RenewalActivationResult ActivateRenewal(
        PaymentRecord payment,
        Order renewalOrder,
        Guid renewalId,
        SubscriptionEntitlement subscription,
        LicenseEngine license)
    {
        ValidateCapturedPayment(payment, renewalOrder);
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(license);

        renewalOrder.CapturePayment(payment.PaymentId, payment.CapturedAtUtc!.Value);
        var renewal = renewalOrder.ActivateRenewal(renewalId, subscription);
        var validUntil = subscription.ValidUntil
            ?? throw new InvalidOperationException("Renewed license-backed subscriptions require a finite paid term.");

        license.SynchronizeSubscription(
            subscription.StartsOn,
            validUntil,
            subscription.Entitlements);

        return new RenewalActivationResult(renewal, subscription, license);
    }

    private static void ValidateCapturedPayment(PaymentRecord payment, Order order)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(order);
        if (payment.Status != PaymentStatus.Captured || payment.CapturedAtUtc is null)
            throw new InvalidOperationException("Only a captured payment can activate commerce.");
        if (!Guid.TryParse(payment.OrderId, out var paymentOrderId) || paymentOrderId != order.Id)
            throw new InvalidOperationException("Payment order identity does not match the commerce order.");
        if (!string.Equals(payment.Currency, order.Snapshot.Billing.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Payment currency does not match the commerce order.");

        var expectedMinor = checked(decimal.ToInt64(order.Snapshot.Billing.Amount * 100m));
        if (payment.AmountPaise != expectedMinor)
            throw new InvalidOperationException("Payment amount does not match the commerce order.");
    }
}
