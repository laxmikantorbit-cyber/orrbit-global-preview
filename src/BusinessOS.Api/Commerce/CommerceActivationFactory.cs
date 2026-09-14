using BusinessOS.Catalog;
using BusinessOS.Commerce;
using BusinessOS.Payments;

namespace BusinessOS.Api.Commerce;

internal static class CommerceActivationFactory
{
    public static void ValidateTenantAndOrganisation(
        Guid tenantId,
        Guid organisationId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.");
        if (organisationId == Guid.Empty)
            throw new ArgumentException("Organisation id is required.");
    }

    public static Order AcceptedOrder(
        Guid tenantId,
        Guid organisationId,
        CommercialSnapshot snapshot,
        Guid quoteId,
        Guid orderId,
        DateTimeOffset createdAtUtc)
    {
        var quote = new Quote(
            quoteId,
            tenantId,
            organisationId,
            snapshot,
            createdAtUtc,
            createdAtUtc.AddDays(7));
        quote.Accept(createdAtUtc);
        return quote.CreateOrder(orderId);
    }

    public static Order AcceptedOrder(
        Guid tenantId,
        Guid organisationId,
        CommercialSnapshot snapshot)
        => AcceptedOrder(
            tenantId,
            organisationId,
            snapshot,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

    public static PaymentRecord CapturedPayment(
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

    public static CommercialSnapshot Snapshot(
        Guid planId,
        Guid planVersionId,
        int planVersionNumber,
        decimal amount,
        string currencyCode,
        int termMonths,
        int desktopDeviceLimit,
        int locationLimit,
        int webAdminSeats,
        int fieldStaffSeats,
        bool multiLocationCloud)
    {
        if (planId == Guid.Empty)
            throw new ArgumentException("Plan id is required.");
        if (planVersionId == Guid.Empty)
            throw new ArgumentException("Plan version id is required.");
        if (planVersionNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(planVersionNumber));
        if (termMonths < 1)
            throw new ArgumentOutOfRangeException(nameof(termMonths));
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("Currency code is required.");

        var billing = new BillingRule(
            amount,
            currencyCode.Trim().ToUpperInvariant(),
            BillingCycle.Annual,
            termMonths);
        var entitlements = new EntitlementProfile(
            desktopDeviceLimit,
            locationLimit,
            webAdminSeats,
            fieldStaffSeats,
            multiLocationCloud,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        return new CommercialSnapshot(
            planId,
            planVersionId,
            planVersionNumber,
            billing,
            entitlements);
    }
}
