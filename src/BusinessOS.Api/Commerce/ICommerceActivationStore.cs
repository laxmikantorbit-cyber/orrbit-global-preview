using BusinessOS.Payments;

namespace BusinessOS.Api.Commerce;

public interface ICommerceActivationStore
{
    Task<CommerceAdminSnapshot> GetAdminSnapshotAsync(
        Guid tenantId,
        int take,
        CancellationToken cancellationToken = default);

    Task<ActivationResponse?> FindActivationAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionStateSnapshot?> FindSubscriptionStateAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionStateSnapshot?> CancelSubscriptionAtPeriodEndAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<DesktopDeviceLicenseResponse?> ActivateDesktopDeviceAsync(
        Guid tenantId,
        Guid subscriptionId,
        DesktopDeviceActivationRequest request,
        CancellationToken cancellationToken = default);

    Task<DesktopDeviceLicenseResponse?> ValidateDesktopDeviceAsync(
        Guid tenantId,
        Guid subscriptionId,
        DesktopDeviceValidationRequest request,
        CancellationToken cancellationToken = default);

    Task<CheckoutOrderResponse> CreateInitialCheckoutOrderAsync(
        Guid tenantId,
        CreateInitialCheckoutOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<CheckoutOrderResponse?> CreateRenewalCheckoutOrderAsync(
        Guid tenantId,
        Guid subscriptionId,
        CreateRenewalCheckoutOrderRequest request,
        CancellationToken cancellationToken = default);

    Task RecordRazorpayOrderAsync(
        Guid tenantId,
        Guid commerceOrderId,
        string razorpayOrderId,
        string productCode,
        Guid? subscriptionId,
        CancellationToken cancellationToken = default);

    Task<ProviderOrderRoute?> FindProviderOrderRouteAsync(
        string provider,
        string providerOrderId,
        CancellationToken cancellationToken = default);

    Task<ProviderOrderStatus?> FindProviderOrderStatusAsync(
        string provider,
        string providerOrderId,
        CancellationToken cancellationToken = default);

    Task<ActivationResponse> ActivateInitialPurchaseAsync(
        Guid tenantId,
        InitialActivationRequest request,
        CancellationToken cancellationToken = default);

    Task<RenewalResponse?> ActivateRenewalAsync(
        Guid tenantId,
        Guid subscriptionId,
        RenewalActivationRequest request,
        CancellationToken cancellationToken = default);

    Task<ActivationResponse?> ActivateCapturedInitialOrderAsync(
        Guid tenantId,
        PaymentRecord payment,
        string productCode,
        CancellationToken cancellationToken = default);

    Task<RenewalResponse?> ActivateCapturedRenewalOrderAsync(
        Guid tenantId,
        Guid subscriptionId,
        PaymentRecord payment,
        CancellationToken cancellationToken = default);
}
