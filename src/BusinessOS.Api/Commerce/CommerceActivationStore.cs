using BusinessOS.Application;
using BusinessOS.Catalog;
using BusinessOS.Commerce;
using BusinessOS.Licensing;
using BusinessOS.Payments;

namespace BusinessOS.Api.Commerce;

public sealed class CommerceActivationStore
{
    private readonly PaymentSubscriptionActivationService _activationService;
    private readonly LeaseSigner _signer;
    private readonly object _gate = new();
    private readonly Dictionary<(Guid TenantId, Guid SubscriptionId), StoredActivation> _activations = [];

    public CommerceActivationStore(
        PaymentSubscriptionActivationService activationService,
        LeaseSigner signer)
    {
        _activationService = activationService;
        _signer = signer;
    }

    public ActivationResponse? FindActivation(Guid tenantId, Guid subscriptionId)
    {
        lock (_gate)
        {
            return _activations.TryGetValue((tenantId, subscriptionId), out var stored)
                ? ToResponse(tenantId, stored)
                : null;
        }
    }

    public ActivationResponse ActivateInitialPurchase(
        Guid tenantId,
        InitialActivationRequest request)
    {
        ValidateTenantAndOrganisation(tenantId, request.OrganisationId);
        var snapshot = Snapshot(
            request.PlanId ?? Guid.NewGuid(),
            request.PlanVersionId ?? Guid.NewGuid(),
            request.PlanVersionNumber,
            request.Amount,
            request.CurrencyCode,
            request.TermMonths,
            request.DesktopDeviceLimit,
            request.LocationLimit,
            request.WebAdminSeats,
            request.FieldStaffSeats,
            request.MultiLocationCloud);

        var order = AcceptedOrder(
            tenantId,
            request.OrganisationId,
            snapshot);
        var payment = CapturedPayment(
            order,
            request.PaymentId,
            request.CapturedAtUtc);

        var activation = _activationService.ActivateInitialPurchase(
            payment,
            order,
            Guid.NewGuid(),
            request.ProductCode,
            _signer);

        lock (_gate)
        {
            _activations[(tenantId, activation.Subscription.Id)] = new StoredActivation(
                activation.Subscription,
                activation.License,
                request.ProductCode.Trim());
        }

        return ToResponse(tenantId, activation, request.ProductCode);
    }

    public RenewalResponse? ActivateRenewal(
        Guid tenantId,
        Guid subscriptionId,
        RenewalActivationRequest request)
    {
        StoredActivation stored;
        lock (_gate)
        {
            if (!_activations.TryGetValue((tenantId, subscriptionId), out stored!))
                return null;
        }

        var snapshot = Snapshot(
            stored.Subscription.PlanId,
            request.PlanVersionId ?? Guid.NewGuid(),
            request.PlanVersionNumber,
            request.Amount,
            request.CurrencyCode,
            request.TermMonths,
            request.DesktopDeviceLimit,
            request.LocationLimit,
            request.WebAdminSeats,
            request.FieldStaffSeats,
            request.MultiLocationCloud);
        var order = AcceptedOrder(
            tenantId,
            stored.Subscription.OrganisationId,
            snapshot);
        var payment = CapturedPayment(
            order,
            request.PaymentId,
            request.CapturedAtUtc);

        var result = _activationService.ActivateRenewal(
            payment,
            order,
            Guid.NewGuid(),
            stored.Subscription,
            stored.License);

        return new RenewalResponse(
            tenantId,
            result.Subscription.Id,
            result.Renewal.Id,
            result.Renewal.OrderId,
            result.Renewal.PlanVersionId,
            result.Renewal.PreviousValidUntil ?? result.Subscription.StartsOn,
            result.Renewal.NewValidUntil,
            result.Subscription.Entitlements);
    }

    private static void ValidateTenantAndOrganisation(Guid tenantId, Guid organisationId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.");
        if (organisationId == Guid.Empty)
            throw new ArgumentException("Organisation id is required.");
    }

    private static Order AcceptedOrder(
        Guid tenantId,
        Guid organisationId,
        CommercialSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        var quote = new Quote(
            Guid.NewGuid(),
            tenantId,
            organisationId,
            snapshot,
            now,
            now.AddDays(7));
        quote.Accept(now);
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
        if (planVersionNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(planVersionNumber));
        if (termMonths < 1)
            throw new ArgumentOutOfRangeException(nameof(termMonths));
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
    private static ActivationResponse ToResponse(
        Guid tenantId,
        InitialActivationResult activation,
        string productCode)
    {
        var stored = new StoredActivation(
            activation.Subscription,
            activation.License,
            productCode.Trim());
        return ToResponse(tenantId, stored);
    }

    private static ActivationResponse ToResponse(
        Guid tenantId,
        StoredActivation stored)
    {
        var validUntil = stored.Subscription.ValidUntil
            ?? throw new InvalidOperationException("Activated subscription must have a finite term.");
        return new ActivationResponse(
            tenantId,
            stored.Subscription.OrganisationId,
            stored.Subscription.OrderId,
            stored.Subscription.Id,
            stored.License.LicenseId,
            stored.ProductCode,
            stored.Subscription.StartsOn,
            validUntil,
            stored.Subscription.Entitlements);
    }

    private sealed record StoredActivation(
        SubscriptionEntitlement Subscription,
        LicenseEngine License,
        string ProductCode);
}
