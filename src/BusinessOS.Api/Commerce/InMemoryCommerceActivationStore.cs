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
    private readonly Dictionary<(string Provider, string ProviderSubscriptionId), ProviderSubscriptionBinding> _providerSubscriptions = [];
    private readonly Dictionary<(Guid TenantId, Guid SubscriptionId, string Provider), string> _providerSubscriptionIndex = [];
    private readonly Dictionary<(Guid TenantId, Guid SubscriptionId), CommercialSnapshot> _subscriptionCommercialSnapshots = [];
    private readonly Dictionary<(Guid TenantId, Guid OrderId), RenewalResponse> _renewalsByOrder = [];
    private readonly Dictionary<(Guid TenantId, Guid SubscriptionId), LicenseActivationCodeResponse> _activationCodes = [];
    private readonly Dictionary<string, DesktopActivationRoute> _activationCodeIndex = [];
    private readonly Dictionary<(Guid TenantId, Guid SubscriptionId, string DeviceFingerprint), DeviceMetadata> _deviceMetadata = [];
    private readonly List<DesktopDeviceLifecycleEvent> _deviceEvents = [];
    private readonly Dictionary<(Guid TenantId, Guid OrderId), CommerceBillingSource> _billingOrders = [];

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

    public Task<CommerceBillingSource?> FindOrderBillingSourceAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_billingOrders.TryGetValue((tenantId, orderId), out var source)
                ? source
                : null);
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

    public Task<IReadOnlyList<SubscriptionStateSnapshot>> ListSubscriptionStatesAsync(
        Guid tenantId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var limit = Math.Clamp(take, 1, 200);
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<SubscriptionStateSnapshot>>(
                _activations
                    .Where(x => x.Key.TenantId == tenantId)
                    .Select(x => ToStateSnapshot(tenantId, x.Value))
                    .OrderByDescending(x => x.ValidUntil)
                    .ThenByDescending(x => x.StartsOn)
                    .Take(limit)
                    .ToArray());
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
            AddDeviceEventUnsafe(tenantId, subscriptionId, fingerprint, null,
                "activate", "device_activated", metadata, now);
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
            AddDeviceEventUnsafe(tenantId, subscriptionId, fingerprint, null,
                "validate", "license_valid", refreshed, now);
            return Task.FromResult<DesktopDeviceLicenseResponse?>(ToDesktopResponse(
                tenantId, stored, fingerprint, refreshed, lease, now, true, "license_valid"));
        }
    }

    public Task<IReadOnlyList<DesktopDeviceActivationSnapshot>> ListDesktopDevicesAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_activations.TryGetValue((tenantId, subscriptionId), out var stored))
                return Task.FromResult<IReadOnlyList<DesktopDeviceActivationSnapshot>>([]);
            var devices = stored.License.Activations
                .Select(x =>
                {
                    _deviceMetadata.TryGetValue((tenantId, subscriptionId, x.DeviceFingerprint), out var metadata);
                    return new DesktopDeviceActivationSnapshot(
                        x.Id, x.DeviceFingerprint, metadata?.DeviceName, metadata?.AppVersion,
                        x.Active, x.ActivatedAt, metadata?.LastValidatedAtUtc);
                })
                .OrderByDescending(x => x.Active)
                .ThenByDescending(x => x.ActivatedAtUtc)
                .ToList();
            return Task.FromResult<IReadOnlyList<DesktopDeviceActivationSnapshot>>(devices);
        }
    }

    public Task<IReadOnlyList<DesktopDeviceLifecycleEvent>> ListDesktopDeviceEventsAsync(
        Guid tenantId,
        Guid subscriptionId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var limit = Math.Clamp(take, 1, 200);
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<DesktopDeviceLifecycleEvent>>(
                _deviceEvents
                    .Where(x => x.TenantId == tenantId && x.SubscriptionId == subscriptionId)
                    .OrderByDescending(x => x.OccurredAtUtc)
                    .Take(limit)
                    .ToArray());
        }
    }

    public Task<bool> RevokeDesktopDeviceAsync(
        Guid tenantId,
        Guid subscriptionId,
        string deviceFingerprint,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = NormalizeDeviceFingerprint(deviceFingerprint);
        lock (_gate)
        {
            if (!_activations.TryGetValue((tenantId, subscriptionId), out var stored))
                return Task.FromResult(false);
            var revoked = stored.License.DeactivateDevice(fingerprint);
            if (revoked)
            {
                _deviceMetadata.TryGetValue((tenantId, subscriptionId, fingerprint), out var metadata);
                AddDeviceEventUnsafe(tenantId, subscriptionId, fingerprint, null,
                    "revoke", "device_revoked", metadata ?? DeviceMetadata.Empty,
                    DateTimeOffset.UtcNow);
            }
            return Task.FromResult(revoked);
        }
    }

    public Task<DesktopDeviceLicenseResponse?> ReplaceDesktopDeviceAsync(
        Guid tenantId,
        Guid subscriptionId,
        DesktopDeviceReplaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var oldFingerprint = NormalizeDeviceFingerprint(request.OldDeviceFingerprint);
        var newFingerprint = NormalizeDeviceFingerprint(request.NewDeviceFingerprint);
        if (string.Equals(oldFingerprint, newFingerprint, StringComparison.Ordinal))
            throw new ArgumentException("Old and new device fingerprints must be different.");
        lock (_gate)
        {
            if (!_activations.TryGetValue((tenantId, subscriptionId), out var stored))
                return Task.FromResult<DesktopDeviceLicenseResponse?>(null);
            var now = DateTimeOffset.UtcNow;
            var lease = stored.License.ReplaceDevice(oldFingerprint, newFingerprint, now);
            var metadata = new DeviceMetadata(request.DeviceName?.Trim(), request.AppVersion?.Trim(), now);
            _deviceMetadata[(tenantId, subscriptionId, newFingerprint)] = metadata;
            AddDeviceEventUnsafe(tenantId, subscriptionId, newFingerprint, oldFingerprint,
                "replace", "device_replaced", metadata, now);
            return Task.FromResult<DesktopDeviceLicenseResponse?>(ToDesktopResponse(
                tenantId, stored, newFingerprint, metadata, lease, now, true, "device_replaced"));
        }
    }
    public Task<LicenseActivationCodeResponse?> GetOrCreateDesktopActivationCodeAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_activations.TryGetValue((tenantId, subscriptionId), out var stored))
                return Task.FromResult<LicenseActivationCodeResponse?>(null);
            if (_activationCodes.TryGetValue((tenantId, subscriptionId), out var existing))
                return Task.FromResult<LicenseActivationCodeResponse?>(existing);
            var code = GenerateActivationCodeUnsafe();
            var response = ToActivationCodeResponse(tenantId, stored, code, DateTimeOffset.UtcNow);
            _activationCodes[(tenantId, subscriptionId)] = response;
            _activationCodeIndex[NormalizeActivationCode(code)] = new DesktopActivationRoute(tenantId, subscriptionId);
            return Task.FromResult<LicenseActivationCodeResponse?>(response);
        }
    }

    public Task<DesktopDeviceLicenseResponse?> ActivateDesktopDeviceWithCodeAsync(
        DesktopActivationCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryResolveActivationCode(request.ActivationCode, out var route))
            return Task.FromResult<DesktopDeviceLicenseResponse?>(null);
        return ActivateDesktopDeviceAsync(route.TenantId, route.SubscriptionId,
            new DesktopDeviceActivationRequest(request.DeviceFingerprint, request.DeviceName, request.AppVersion),
            cancellationToken);
    }

    public Task<DesktopDeviceLicenseResponse?> ValidateDesktopDeviceWithCodeAsync(
        DesktopActivationCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryResolveActivationCode(request.ActivationCode, out var route))
            return Task.FromResult<DesktopDeviceLicenseResponse?>(null);
        return ValidateDesktopDeviceAsync(route.TenantId, route.SubscriptionId,
            new DesktopDeviceValidationRequest(request.DeviceFingerprint, request.CurrentLease),
            cancellationToken);
    }

    public Task<ProviderSubscriptionBinding?> FindProviderSubscriptionAsync(
        Guid tenantId,
        Guid subscriptionId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProvider(provider);
        lock (_gate)
        {
            if (!_providerSubscriptionIndex.TryGetValue(
                    (tenantId, subscriptionId, normalized), out var providerSubscriptionId))
                return Task.FromResult<ProviderSubscriptionBinding?>(null);
            return Task.FromResult(_providerSubscriptions.TryGetValue(
                (normalized, providerSubscriptionId), out var binding) ? binding : null);
        }
    }

    public Task<ProviderSubscriptionBinding?> FindProviderSubscriptionRouteAsync(
        string provider,
        string providerSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerSubscriptionId))
            return Task.FromResult<ProviderSubscriptionBinding?>(null);
        lock (_gate)
        {
            return Task.FromResult(_providerSubscriptions.TryGetValue(
                (NormalizeProvider(provider), providerSubscriptionId.Trim()), out var binding)
                ? binding : null);
        }
    }

    public Task<ProviderSubscriptionBinding> RecordProviderSubscriptionAsync(
        ProviderSubscriptionBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var provider = NormalizeProvider(binding.Provider);
        if (string.IsNullOrWhiteSpace(binding.ProviderSubscriptionId) ||
            string.IsNullOrWhiteSpace(binding.ProviderPlanId))
            throw new ArgumentException("Provider subscription and plan ids are required.");
        lock (_gate)
        {
            if (!_activations.ContainsKey((binding.TenantId, binding.SubscriptionId)))
                throw new InvalidOperationException("Subscription was not found for provider binding.");
            var normalized = binding with { Provider = provider };
            _providerSubscriptions[(provider, normalized.ProviderSubscriptionId)] = normalized;
            _providerSubscriptionIndex[(normalized.TenantId, normalized.SubscriptionId, provider)] = normalized.ProviderSubscriptionId;
            return Task.FromResult(normalized);
        }
    }

    public Task<ProviderSubscriptionBinding?> UpdateProviderSubscriptionStateAsync(
        string provider,
        string providerSubscriptionId,
        string status,
        bool autoRenewEnabled,
        bool cancelAtPeriodEnd,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedId = providerSubscriptionId.Trim();
        lock (_gate)
        {
            if (!_providerSubscriptions.TryGetValue((normalizedProvider, normalizedId), out var binding))
                return Task.FromResult<ProviderSubscriptionBinding?>(null);
            var updated = binding with
            {
                Status = status.Trim(),
                AutoRenewEnabled = autoRenewEnabled,
                CancelAtPeriodEnd = cancelAtPeriodEnd,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _providerSubscriptions[(normalizedProvider, normalizedId)] = updated;
            return Task.FromResult<ProviderSubscriptionBinding?>(updated);
        }
    }
    public Task<AutoPayRenewalTemplate?> FindAutoPayRenewalTemplateAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_activations.TryGetValue((tenantId, subscriptionId), out var activation) ||
                !_subscriptionCommercialSnapshots.TryGetValue((tenantId, subscriptionId), out var snapshot))
                return Task.FromResult<AutoPayRenewalTemplate?>(null);
            var termMonths = snapshot.Billing.TermMonths
                ?? throw new InvalidOperationException("AutoPay renewal requires a finite billing term.");
            return Task.FromResult<AutoPayRenewalTemplate?>(new AutoPayRenewalTemplate(
                activation.ProductCode,
                snapshot.PlanVersionId,
                snapshot.PlanVersionNumber,
                snapshot.Billing.Amount,
                snapshot.Billing.CurrencyCode,
                termMonths,
                snapshot.Entitlements.DesktopDeviceLimit,
                snapshot.Entitlements.LocationLimit,
                snapshot.Entitlements.WebAdminSeats,
                snapshot.Entitlements.FieldStaffSeats,
                snapshot.Entitlements.MultiLocationCloud));
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
        StoreCommercialSnapshot(tenantId, activation.Subscription.Id, snapshot);
        StoreBillingSource(ToBillingSource(
            order, activation.Subscription.Id, request.ProductCode, isRenewal: false));
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
        StoreCommercialSnapshot(tenantId, subscriptionId, snapshot);
        StoreBillingSource(ToBillingSource(
            order, subscriptionId, activation.ProductCode, isRenewal: true));
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
            _subscriptionCommercialSnapshots[(tenantId, activation.Subscription.Id)] = pending.Order.Snapshot;
            _billingOrders[(tenantId, orderId)] = ToBillingSource(
                pending.Order, activation.Subscription.Id, pending.ProductCode, isRenewal: false);
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
            _subscriptionCommercialSnapshots[(tenantId, subscriptionId)] = pending.Order.Snapshot;
            _billingOrders[(tenantId, orderId)] = ToBillingSource(
                pending.Order, subscriptionId, activation.ProductCode, isRenewal: true);
            _pendingOrders.Remove((tenantId, orderId));
            return Task.FromResult<RenewalResponse?>(response);
        }
    }

    private void StoreBillingSource(CommerceBillingSource source)
    {
        lock (_gate)
            _billingOrders[(source.TenantId, source.OrderId)] = source;
    }

    private static CommerceBillingSource ToBillingSource(
        Order order,
        Guid subscriptionId,
        string productCode,
        bool isRenewal)
    {
        if (string.IsNullOrWhiteSpace(order.PaymentId) || order.PaidAtUtc is null)
            throw new InvalidOperationException("Paid order is required for billing.");
        return new CommerceBillingSource(
            order.TenantId,
            order.OrganisationId,
            order.Id,
            subscriptionId,
            productCode.Trim(),
            order.Snapshot.Billing.Amount,
            order.Snapshot.Billing.CurrencyCode,
            order.PaymentId,
            order.PaidAtUtc.Value,
            isRenewal);
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

    private void StoreCommercialSnapshot(
        Guid tenantId,
        Guid subscriptionId,
        CommercialSnapshot snapshot)
    {
        lock (_gate)
        {
            _subscriptionCommercialSnapshots[(tenantId, subscriptionId)] = snapshot;
        }
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

    private bool TryResolveActivationCode(string value, out DesktopActivationRoute route)
    {
        var code = NormalizeActivationCode(value);
        lock (_gate)
        {
            return _activationCodeIndex.TryGetValue(code, out route!);
        }
    }

    private string GenerateActivationCodeUnsafe()
    {
        string code;
        do
        {
            var raw = Guid.NewGuid().ToString("N").ToUpperInvariant();
            code = $"ORR-{raw[..4]}-{raw[4..8]}-{raw[8..12]}-{raw[12..16]}";
        } while (_activationCodeIndex.ContainsKey(NormalizeActivationCode(code)));
        return code;
    }

    private static string NormalizeActivationCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Activation code is required.");
        return value.Trim().Replace(" ", "").ToUpperInvariant();
    }

    private static LicenseActivationCodeResponse ToActivationCodeResponse(
        Guid tenantId,
        StoredActivation stored,
        string code,
        DateTimeOffset createdAtUtc) =>
        new(tenantId, stored.Subscription.OrganisationId, stored.Subscription.Id,
            stored.License.LicenseId, stored.ProductCode, code, createdAtUtc);

    private void AddDeviceEventUnsafe(
        Guid tenantId,
        Guid subscriptionId,
        string deviceFingerprint,
        string? previousDeviceFingerprint,
        string action,
        string outcome,
        DeviceMetadata metadata,
        DateTimeOffset occurredAtUtc)
    {
        _deviceEvents.Add(new DesktopDeviceLifecycleEvent(
            Guid.NewGuid(), tenantId, subscriptionId, deviceFingerprint,
            previousDeviceFingerprint, action, outcome, metadata.DeviceName,
            metadata.AppVersion, occurredAtUtc));
    }

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

    private sealed record DesktopActivationRoute(Guid TenantId, Guid SubscriptionId);

    private sealed record DeviceMetadata(
        string? DeviceName,
        string? AppVersion,
        DateTimeOffset LastValidatedAtUtc)
    {
        public static readonly DeviceMetadata Empty = new(null, null, DateTimeOffset.MinValue);
    }
}
