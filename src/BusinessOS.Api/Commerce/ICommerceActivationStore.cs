using BusinessOS.Payments;

namespace BusinessOS.Api.Commerce;

public interface ICommerceActivationStore
{
    Task<ActivationResponse?> FindActivationAsync(
        Guid tenantId,
        Guid subscriptionId,
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
