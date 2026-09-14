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
    private readonly Dictionary<(Guid TenantId, Guid OrderId), PendingCheckoutOrder> _pendingOrders = [];
    private readonly Dictionary<(Guid TenantId, Guid OrderId), RenewalResponse> _renewalsByOrder = [];

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

    public Task<CheckoutOrderResponse> CreateInitialCheckoutOrderAsync(
        Guid tenantId,
        CreateInitialCheckoutOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        CommerceActivationFactory.ValidateTenantAndOrganisation(tenantId, request.OrganisationId);
        var createdAtUtc = DateTimeOffset.UtcNow;
        var order = CreatePendingOrder(
            tenantId,
            request.OrganisationId,
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
            request.MultiLocationCloud,
            createdAtUtc);

        var pending = new PendingCheckoutOrder(order, request.ProductCode.Trim(), null, createdAtUtc.AddDays(7));
        lock (_gate)
        {
            _pendingOrders[(tenantId, order.Id)] = pending;
        }

        return Task.FromResult(ToCheckoutResponse(tenantId, pending));
    }

    public Task<CheckoutOrderResponse?> CreateRenewalCheckoutOrderAsync(
        Guid tenantId,
        Guid subscriptionId,
        CreateRenewalCheckoutOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        StoredActivation? stored;
        lock (_gate)
        {
            if (!_activations.TryGetValue((tenantId, subscriptionId), out stored))
                return Task.FromResult<CheckoutOrderResponse?>(null);
        }

        var activation = stored
            ?? throw new InvalidOperationException("Subscription disappeared during checkout creation.");
        var createdAtUtc = DateTimeOffset.UtcNow;
        var order = CreatePendingOrder(
            tenantId,
            activation.Subscription.OrganisationId,
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
            request.MultiLocationCloud,
            createdAtUtc);
        var pending = new PendingCheckoutOrder(
            order,
            activation.ProductCode,
            subscriptionId,
            createdAtUtc.AddDays(7));
        lock (_gate)
        {
            _pendingOrders[(tenantId, order.Id)] = pending;
        }

        return Task.FromResult<CheckoutOrderResponse?>(ToCheckoutResponse(tenantId, pending));
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

        StoreActivation(tenantId, activation, request.ProductCode);
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
            activation.Subscription.OrganisationId,
            snapshot);
        var payment = CommerceActivationFactory.CapturedPayment(
            order,
            request.PaymentId,
            request.CapturedAtUtc);
        var result = _activationService.ActivateRenewal(
            payment,
            order,
            Guid.NewGuid(),
            activation.Subscription,
            activation.License);

        var response = ToRenewalResponse(tenantId, result);
        return Task.FromResult<RenewalResponse?>(response);
    }

    public Task<ActivationResponse?> ActivateCapturedInitialOrderAsync(
        Guid tenantId,
        PaymentRecord payment,
        string productCode,
        CancellationToken cancellationToken = default)
    {
        var orderId = ValidateCapturedOrderId(payment);
        lock (_gate)
        {
            var existing = _activations.Values.FirstOrDefault(x => x.Subscription.OrderId == orderId);
            if (existing is not null)
                return Task.FromResult<ActivationResponse?>(ToResponse(tenantId, existing));
            if (!_pendingOrders.TryGetValue((tenantId, orderId), out var pending))
                return Task.FromResult<ActivationResponse?>(null);
            if (pending.SubscriptionId is not null)
                throw new InvalidOperationException("Pending order is for subscription renewal.");
            if (!string.Equals(pending.ProductCode, productCode, StringComparison.Ordinal))
                throw new InvalidOperationException("Webhook product code does not match checkout order.");

            var activation = _activationService.ActivateInitialPurchase(
                payment,
                pending.Order,
                Guid.NewGuid(),
                pending.ProductCode,
                _signer);
            StoreActivationUnsafe(tenantId, activation, pending.ProductCode);
            _pendingOrders.Remove((tenantId, orderId));
            return Task.FromResult<ActivationResponse?>(ToResponse(tenantId, activation, pending.ProductCode));
        }
    }

    public Task<RenewalResponse?> ActivateCapturedRenewalOrderAsync(
        Guid tenantId,
        Guid subscriptionId,
        PaymentRecord payment,
        CancellationToken cancellationToken = default)
    {
        var orderId = ValidateCapturedOrderId(payment);
        lock (_gate)
        {
            if (_renewalsByOrder.TryGetValue((tenantId, orderId), out var existing))
                return Task.FromResult<RenewalResponse?>(existing);
            if (!_activations.TryGetValue((tenantId, subscriptionId), out var activation))
                return Task.FromResult<RenewalResponse?>(null);
            if (!_pendingOrders.TryGetValue((tenantId, orderId), out var pending))
                return Task.FromResult<RenewalResponse?>(null);
            if (pending.SubscriptionId != subscriptionId)
                throw new InvalidOperationException("Pending order does not match subscription.");

            var result = _activationService.ActivateRenewal(
                payment,
                pending.Order,
                Guid.NewGuid(),
                activation.Subscription,
                activation.License);
            var response = ToRenewalResponse(tenantId, result);
            _renewalsByOrder[(tenantId, orderId)] = response;
            _pendingOrders.Remove((tenantId, orderId));
            return Task.FromResult<RenewalResponse?>(response);
        }
    }

    private static Order CreatePendingOrder(
        Guid tenantId,
        Guid organisationId,
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
        bool multiLocationCloud,
        DateTimeOffset createdAtUtc)
    {
        var snapshot = CommerceActivationFactory.Snapshot(
            planId,
            planVersionId,
            planVersionNumber,
            amount,
            currencyCode,
            termMonths,
            desktopDeviceLimit,
            locationLimit,
            webAdminSeats,
            fieldStaffSeats,
            multiLocationCloud);
        return CommerceActivationFactory.AcceptedOrder(
            tenantId,
            organisationId,
            snapshot,
            Guid.NewGuid(),
            Guid.NewGuid(),
            createdAtUtc);
    }

    private void StoreActivation(
        Guid tenantId,
        InitialActivationResult activation,
        string productCode)
    {
        lock (_gate)
        {
            StoreActivationUnsafe(tenantId, activation, productCode);
        }
    }

    private void StoreActivationUnsafe(
        Guid tenantId,
        InitialActivationResult activation,
        string productCode)
    {
        _activations[(tenantId, activation.Subscription.Id)] = new StoredActivation(
            activation.Subscription,
            activation.License,
            productCode.Trim());
    }

    private static CheckoutOrderResponse ToCheckoutResponse(
        Guid tenantId,
        PendingCheckoutOrder pending)
    {
        var notes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenantId"] = tenantId.ToString(),
            ["commerceOrderId"] = pending.Order.Id.ToString(),
            ["internalOrderId"] = pending.Order.Id.ToString(),
            ["productCode"] = pending.ProductCode
        };
        if (pending.SubscriptionId is Guid subscriptionId)
            notes["subscriptionId"] = subscriptionId.ToString();

        return new CheckoutOrderResponse(
            tenantId,
            pending.Order.OrganisationId,
            pending.Order.QuoteId,
            pending.Order.Id,
            pending.Order.Snapshot.PlanId,
            pending.Order.Snapshot.PlanVersionId,
            pending.Order.Snapshot.Billing.Amount,
            pending.Order.Snapshot.Billing.CurrencyCode,
            pending.ExpiresAtUtc,
            notes);
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

    private static RenewalResponse ToRenewalResponse(
        Guid tenantId,
        RenewalActivationResult result)
        => new(
            tenantId,
            result.Subscription.Id,
            result.Renewal.Id,
            result.Renewal.OrderId,
            result.Renewal.PlanVersionId,
            result.Renewal.PreviousValidUntil ?? result.Subscription.StartsOn,
            result.Renewal.NewValidUntil,
            result.Subscription.Entitlements);

    private static Guid ValidateCapturedOrderId(PaymentRecord payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (payment.Status != PaymentStatus.Captured || payment.CapturedAtUtc is null)
            throw new InvalidOperationException("Only captured payment can activate commerce order.");
        if (!Guid.TryParse(payment.OrderId, out var orderId) || orderId == Guid.Empty)
            throw new InvalidOperationException("Payment order id must be the internal commerce order id.");
        return orderId;
    }

    private sealed record PendingCheckoutOrder(
        Order Order,
        string ProductCode,
        Guid? SubscriptionId,
        DateTimeOffset ExpiresAtUtc);

    private sealed record StoredActivation(
        SubscriptionEntitlement Subscription,
        LicenseEngine License,
        string ProductCode);
}
