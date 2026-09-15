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
    private readonly Dictionary<(Guid TenantId, string RazorpayOrderId), Guid> _razorpayOrderIndex = [];
    private readonly Dictionary<(string Provider, string ProviderOrderId), ProviderOrderRoute> _providerRoutes = [];
    private readonly Dictionary<(Guid TenantId, Guid OrderId), RenewalResponse> _renewalsByOrder = [];
    private readonly Dictionary<(Guid TenantId, Guid SubscriptionId, string DeviceFingerprint), DeviceMetadata> _deviceMetadata = [];

    public InMemoryCommerceActivationStore(
        PaymentSubscriptionActivationService activationService,
        LeaseSigner signer)
    {
        _activationService = activationService;
        _signer = signer;
    }

    public Task<CommerceAdminSnapshot> GetAdminSnapshotAsync(
        Guid tenantId,
        int take,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var limit = Math.Clamp(take, 1, 200);
            var pendingOrders = _pendingOrders.Values
                .Where(x => x.Order.TenantId == tenantId)
                .OrderByDescending(x => x.ExpiresAtUtc)
                .Take(limit)
                .Select(x => ToAdminOrderSnapshot(tenantId, x))
                .ToList();
            var activations = _activations
                .Where(x => x.Key.TenantId == tenantId)
                .Select(x => ToResponse(tenantId, x.Value))
                .Take(limit)
                .ToList();
            var renewals = _renewalsByOrder
                .Where(x => x.Key.TenantId == tenantId)
                .Select(x => x.Value)
                .Take(limit)
                .ToList();
            return Task.FromResult(new CommerceAdminSnapshot(
                tenantId, DateTimeOffset.UtcNow,
                pendingOrders, activations, renewals));
        }
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

    public Task<SubscriptionStateSnapshot?> FindSubscriptionStateAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_activations.TryGetValue((tenantId, subscriptionId), out var stored)
                ? ToStateSnapshot(tenantId, stored)
                : null);
        }
    }

    public Task<SubscriptionStateSnapshot?> CancelSubscriptionAtPeriodEndAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_activations.TryGetValue((tenantId, subscriptionId), out var stored))
                return Task.FromResult<SubscriptionStateSnapshot?>(null);
            stored.Subscription.Cancel();
            return Task.FromResult<SubscriptionStateSnapshot?>(ToStateSnapshot(tenantId, stored));
        }
    }

    public Task<DesktopDeviceLicenseResponse?> ActivateDesktopDeviceAsync(
        Guid tenantId,
        Guid subscriptionId,
        DesktopDeviceActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fingerprint = NormalizeDeviceFingerprint(request.DeviceFingerprint);
        lock (_gate)
        {
            if (!_activations.TryGetValue((tenantId, subscriptionId), out var stored))
                return Task.FromResult<DesktopDeviceLicenseResponse?>(null);
            var now = DateTimeOffset.UtcNow;
            var lease = stored.License.Activate(fingerprint, now);
            var metadata = new DeviceMetadata(
                request.DeviceName?.Trim(), request.AppVersion?.Trim(), now);
            _deviceMetadata[(tenantId, subscriptionId, fingerprint)] = metadata;
            return Task.FromResult<DesktopDeviceLicenseResponse?>(ToDesktopResponse(
                tenantId, stored, fingerprint, metadata, lease, now, true, "device_activated"));
        }
    }

    public Task<DesktopDeviceLicenseResponse?> ValidateDesktopDeviceAsync(
        Guid tenantId,
        Guid subscriptionId,
        DesktopDeviceValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fingerprint = NormalizeDeviceFingerprint(request.DeviceFingerprint);
        lock (_gate)
        {
            if (!_activations.TryGetValue((tenantId, subscriptionId), out var stored))
                return Task.FromResult<DesktopDeviceLicenseResponse?>(null);
            var now = DateTimeOffset.UtcNow;
            if (!stored.License.Activations.Any(x => x.Active && x.DeviceFingerprint == fingerprint))
                return Task.FromResult<DesktopDeviceLicenseResponse?>(ToDesktopResponse(
                    tenantId, stored, fingerprint, DeviceMetadata.Empty, null,
                    now, false, "device_not_activated"));
            var lease = stored.License.Activate(fingerprint, now);
            _deviceMetadata.TryGetValue((tenantId, subscriptionId, fingerprint), out var metadata);
            var refreshed = (metadata ?? DeviceMetadata.Empty) with { LastValidatedAtUtc = now };
            _deviceMetadata[(tenantId, subscriptionId, fingerprint)] = refreshed;
            return Task.FromResult<DesktopDeviceLicenseResponse?>(ToDesktopResponse(
                tenantId, stored, fingerprint, refreshed, lease, now, true, "license_valid"));
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
        if (activation.Subscription.Status == BusinessOS.Commerce.SubscriptionStatus.Cancelled)
            throw new InvalidOperationException("Cancelled subscription cannot be renewed from checkout.");
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

    public Task RecordRazorpayOrderAsync(
        Guid tenantId,
        Guid commerceOrderId,
        string razorpayOrderId,
        string productCode,
        Guid? subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || commerceOrderId == Guid.Empty)
            throw new ArgumentException("Tenant and commerce order ids are required.");
        if (string.IsNullOrWhiteSpace(razorpayOrderId))
            throw new ArgumentException("Razorpay order id is required.", nameof(razorpayOrderId));
        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("Product code is required.", nameof(productCode));

        lock (_gate)
        {
            if (!_pendingOrders.TryGetValue((tenantId, commerceOrderId), out var pending))
                throw new InvalidOperationException("Pending checkout order was not found.");
            var trimmedProductCode = productCode.Trim();
            if (!string.Equals(pending.ProductCode, trimmedProductCode, StringComparison.Ordinal))
                throw new InvalidOperationException("Provider route product code does not match checkout order.");
            if (pending.SubscriptionId != subscriptionId)
                throw new InvalidOperationException("Provider route subscription id does not match checkout order.");
            var trimmedRazorpayOrderId = razorpayOrderId.Trim();
            _pendingOrders[(tenantId, commerceOrderId)] = pending with
            {
                RazorpayOrderId = trimmedRazorpayOrderId
            };
            _razorpayOrderIndex[(tenantId, trimmedRazorpayOrderId)] = commerceOrderId;
            _providerRoutes[(NormalizeProvider("razorpay"), trimmedRazorpayOrderId)] = new ProviderOrderRoute(
                "razorpay",
                trimmedRazorpayOrderId,
                tenantId,
                commerceOrderId,
                pending.ProductCode,
                pending.SubscriptionId);
        }
        return Task.CompletedTask;
    }

    public Task<ProviderOrderRoute?> FindProviderOrderRouteAsync(
        string provider,
        string providerOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerOrderId))
            return Task.FromResult<ProviderOrderRoute?>(null);
        lock (_gate)
        {
            return Task.FromResult(TryFindRouteUnsafe(provider, providerOrderId, out var route)
                ? route
                : null);
        }
    }

    public Task<ProviderOrderStatus?> FindProviderOrderStatusAsync(
        string provider,
        string providerOrderId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerOrderId))
            return Task.FromResult<ProviderOrderStatus?>(null);
        lock (_gate)
        {
            if (!TryFindRouteUnsafe(provider, providerOrderId, out var route))
                return Task.FromResult<ProviderOrderStatus?>(null);
            var renewal = route.SubscriptionId is null
                ? null
                : (_renewalsByOrder.TryGetValue((route.TenantId, route.CommerceOrderId), out var foundRenewal)
                    ? foundRenewal
                    : null);
            var activation = route.SubscriptionId is null
                ? _activations.Values
                    .Where(x => x.Subscription.TenantId == route.TenantId)
                    .FirstOrDefault(x => x.Subscription.OrderId == route.CommerceOrderId)
                : null;
            var activationResponse = activation is null ? null : ToResponse(route.TenantId, activation);
            var outcome = activationResponse is not null || renewal is not null
                ? "activated"
                : "verified_pending_activation";
            return Task.FromResult<ProviderOrderStatus?>(new ProviderOrderStatus(
                route, outcome, activationResponse, renewal));
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
        var orderReference = ValidateCapturedOrderReference(payment);
        lock (_gate)
        {
            if (!TryResolveOrderId(tenantId, orderReference, out var orderId))
                return Task.FromResult<ActivationResponse?>(null);
            var normalizedPayment = payment with { OrderId = orderId.ToString() };
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
                normalizedPayment,
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
        var orderReference = ValidateCapturedOrderReference(payment);
        lock (_gate)
        {
            if (!TryResolveOrderId(tenantId, orderReference, out var orderId))
                return Task.FromResult<RenewalResponse?>(null);
            var normalizedPayment = payment with { OrderId = orderId.ToString() };
            if (_renewalsByOrder.TryGetValue((tenantId, orderId), out var existing))
                return Task.FromResult<RenewalResponse?>(existing);
            if (!_activations.TryGetValue((tenantId, subscriptionId), out var activation))
                return Task.FromResult<RenewalResponse?>(null);
            if (!_pendingOrders.TryGetValue((tenantId, orderId), out var pending))
                return Task.FromResult<RenewalResponse?>(null);
            if (pending.SubscriptionId != subscriptionId)
                throw new InvalidOperationException("Pending order does not match subscription.");

            var result = _activationService.ActivateRenewal(
                normalizedPayment,
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

    private CommerceAdminOrderSnapshot ToAdminOrderSnapshot(
        Guid tenantId,
        PendingCheckoutOrder pending)
    {
        var providerRoute = _providerRoutes.Values.FirstOrDefault(x =>
            x.TenantId == tenantId && x.CommerceOrderId == pending.Order.Id);
        var providerOrderId = providerRoute?.ProviderOrderId ?? pending.RazorpayOrderId;
        return new CommerceAdminOrderSnapshot(
            tenantId,
            pending.Order.OrganisationId,
            pending.Order.QuoteId,
            pending.Order.Id,
            pending.Order.Snapshot.PlanId,
            pending.Order.Snapshot.PlanVersionId,
            pending.Order.Snapshot.Billing.Amount,
            pending.Order.Snapshot.Billing.CurrencyCode,
            pending.Order.Status.ToString(),
            pending.Order.PaymentId,
            pending.Order.PaidAtUtc,
            pending.RazorpayOrderId,
            providerRoute?.Provider,
            providerOrderId,
            pending.ProductCode,
            pending.SubscriptionId,
            pending.ExpiresAtUtc.AddDays(-7),
            pending.ExpiresAtUtc);
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
            pending.ProductCode,
            pending.SubscriptionId,
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

    private static SubscriptionStateSnapshot ToStateSnapshot(
        Guid tenantId,
        StoredActivation stored)
    {
        var validUntil = stored.Subscription.ValidUntil
            ?? throw new InvalidOperationException("Activated subscription must have a finite term.");
        return new SubscriptionStateSnapshot(
            tenantId,
            stored.Subscription.OrganisationId,
            stored.Subscription.Id,
            stored.License.LicenseId,
            stored.ProductCode,
            stored.Subscription.PlanId,
            stored.Subscription.PlanVersionId,
            stored.Subscription.StartsOn,
            validUntil,
            stored.Subscription.Entitlements,
            stored.Subscription.Status);
    }

    private DesktopDeviceLicenseResponse ToDesktopResponse(
        Guid tenantId,
        StoredActivation stored,
        string deviceFingerprint,
        DeviceMetadata metadata,
        SignedLicenseLease? lease,
        DateTimeOffset now,
        bool allowed,
        string reason)
    {
        var state = ToStateSnapshot(tenantId, stored);
        var entitlement = EntitlementStatusEvaluator.Evaluate(
            state,
            DateOnly.FromDateTime(now.UtcDateTime));
        var activeDevices = stored.License.Activations.Count(x => x.Active);
        var leasePayload = lease is null
            ? null
            : LeaseSigner.Verify(lease, stored.License.ExportPublicKey(), deviceFingerprint, now);
        return new DesktopDeviceLicenseResponse(
            state.TenantId,
            state.OrganisationId,
            state.SubscriptionId,
            state.LicenseId,
            state.ProductCode,
            deviceFingerprint,
            metadata.DeviceName,
            entitlement.Status,
            entitlement.RenewalStatus,
            allowed,
            reason,
            activeDevices,
            state.Entitlements.DesktopSystems,
            state.StartsOn,
            state.ValidUntil,
            leasePayload?.LeaseValidUntil,
            lease,
            Convert.ToBase64String(stored.License.ExportPublicKey()),
            state.Entitlements);
    }

    private static string ValidateCapturedOrderReference(PaymentRecord payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (payment.Status != PaymentStatus.Captured || payment.CapturedAtUtc is null)
            throw new InvalidOperationException("Only captured payment can activate commerce order.");
        if (string.IsNullOrWhiteSpace(payment.OrderId))
            throw new InvalidOperationException("Payment order id is required.");
        return payment.OrderId.Trim();
    }

    private bool TryResolveOrderId(
        Guid tenantId,
        string orderReference,
        out Guid orderId)
    {
        if (Guid.TryParse(orderReference, out orderId) && orderId != Guid.Empty)
            return true;
        if (_razorpayOrderIndex.TryGetValue((tenantId, orderReference), out orderId))
            return true;
        orderId = Guid.Empty;
        return false;
    }

    private bool TryFindRouteUnsafe(
        string provider,
        string providerOrderId,
        out ProviderOrderRoute route) =>
        _providerRoutes.TryGetValue(
            (NormalizeProvider(provider), providerOrderId.Trim()),
            out route!);

    private static string NormalizeProvider(string provider) =>
        provider.Trim().ToLowerInvariant();

    private static string NormalizeDeviceFingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Device fingerprint is required.");
        var normalized = value.Trim();
        if (normalized.Length > 256)
            throw new ArgumentException("Device fingerprint must be 256 characters or fewer.");
        return normalized;
    }

    private sealed record PendingCheckoutOrder(
        Order Order,
        string ProductCode,
        Guid? SubscriptionId,
        DateTimeOffset ExpiresAtUtc,
        string? RazorpayOrderId = null);

    private sealed record StoredActivation(
        SubscriptionEntitlement Subscription,
        LicenseEngine License,
        string ProductCode);

    private sealed record DeviceMetadata(
        string? DeviceName,
        string? AppVersion,
        DateTimeOffset LastValidatedAtUtc)
    {
        public static readonly DeviceMetadata Empty = new(null, null, DateTimeOffset.MinValue);
    }
}
