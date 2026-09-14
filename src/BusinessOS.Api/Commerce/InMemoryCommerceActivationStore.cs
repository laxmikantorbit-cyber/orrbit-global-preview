using BusinessOS.Application;
using BusinessOS.Commerce;
using BusinessOS.Licensing;
using BusinessOS.Payments;

namespace BusinessOS.Api.Commerce;

public sealed class InMemoryCommerceActivationStore : ICommerceActivationStore
{
    private readonly PaymentSubscriptionActivationService _activationService;
    private readonly LeaseSigner _signer;
    private readonly object _gate = new();
    private readonly Dictionary<(Guid TenantId, Guid SubscriptionId), StoredActivation> _activations = [];

    public InMemoryCommerceActivationStore(
        PaymentSubscriptionActivationService activationService,
        LeaseSigner signer)
    {
        _activationService = activationService;
        _signer = signer;
    }

    public Task<ActivationResponse?> FindActivationAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_activations.TryGetValue((tenantId, subscriptionId), out var stored)
                ? ToResponse(tenantId, stored)
                : null);
        }
    }

    public Task<ActivationResponse> ActivateInitialPurchaseAsync(
        Guid tenantId,
        InitialActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        CommerceActivationFactory.ValidateTenantAndOrganisation(
            tenantId,
            request.OrganisationId);
        var snapshot = CommerceActivationFactory.Snapshot(
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
        var order = CommerceActivationFactory.AcceptedOrder(
            tenantId,
            request.OrganisationId,
            snapshot);
        var payment = CommerceActivationFactory.CapturedPayment(
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

        return Task.FromResult(ToResponse(tenantId, activation, request.ProductCode));
    }

    public Task<RenewalResponse?> ActivateRenewalAsync(
        Guid tenantId,
        Guid subscriptionId,
        RenewalActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        StoredActivation? stored;
        lock (_gate)
        {
            if (!_activations.TryGetValue((tenantId, subscriptionId), out stored))
                return Task.FromResult<RenewalResponse?>(null);
        }

        var activation = stored
            ?? throw new InvalidOperationException("Subscription disappeared during renewal activation.");
        var snapshot = CommerceActivationFactory.Snapshot(
            activation.Subscription.PlanId,
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
        var order = CommerceActivationFactory.AcceptedOrder(
            tenantId,
            stored.Subscription.OrganisationId,
            snapshot);
        var payment = CommerceActivationFactory.CapturedPayment(
            order,
            request.PaymentId,
            request.CapturedAtUtc);

        var result = _activationService.ActivateRenewal(
            payment,
            order,
            Guid.NewGuid(),
            stored.Subscription,
            stored.License);

        var response = new RenewalResponse(
            tenantId,
            result.Subscription.Id,
            result.Renewal.Id,
            result.Renewal.OrderId,
            result.Renewal.PlanVersionId,
            result.Renewal.PreviousValidUntil ?? result.Subscription.StartsOn,
            result.Renewal.NewValidUntil,
            result.Subscription.Entitlements);
        return Task.FromResult<RenewalResponse?>(response);
    }

    public Task<ActivationResponse?> ActivateCapturedInitialOrderAsync(
        Guid tenantId,
        PaymentRecord payment,
        string productCode,
        CancellationToken cancellationToken = default)
        => Task.FromResult<ActivationResponse?>(null);

    public Task<RenewalResponse?> ActivateCapturedRenewalOrderAsync(
        Guid tenantId,
        Guid subscriptionId,
        PaymentRecord payment,
        CancellationToken cancellationToken = default)
        => Task.FromResult<RenewalResponse?>(null);

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
