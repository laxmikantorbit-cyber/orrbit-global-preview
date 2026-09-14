using BusinessOS.Api.Payments;

namespace BusinessOS.Api.Commerce;

public sealed class RazorpayCheckoutService
{
    private readonly ICommerceActivationStore _store;
    private readonly IRazorpayOrderClient _razorpay;
    private readonly IConfiguration _configuration;

    public RazorpayCheckoutService(
        ICommerceActivationStore store,
        IRazorpayOrderClient razorpay,
        IConfiguration configuration)
    {
        _store = store;
        _razorpay = razorpay;
        _configuration = configuration;
    }

    public async Task<RazorpayCheckoutOrderResponse> CreateInitialAsync(
        Guid tenantId,
        CreateInitialCheckoutOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var checkout = await _store.CreateInitialCheckoutOrderAsync(
            tenantId,
            request,
            cancellationToken);
        return await CreateProviderOrderAsync(
            checkout,
            cancellationToken);
    }

    public async Task<RazorpayCheckoutOrderResponse?> CreateRenewalAsync(
        Guid tenantId,
        Guid subscriptionId,
        CreateRenewalCheckoutOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var checkout = await _store.CreateRenewalCheckoutOrderAsync(
            tenantId,
            subscriptionId,
            request,
            cancellationToken);
        return checkout is null
            ? null
            : await CreateProviderOrderAsync(checkout, cancellationToken);
    }
    private async Task<RazorpayCheckoutOrderResponse> CreateProviderOrderAsync(
        CheckoutOrderResponse checkout,
        CancellationToken cancellationToken)
    {
        var receipt = CreateReceipt(checkout.CommerceOrderId);
        var amountMinor = ToMinorUnits(checkout.Amount);
        var provider = await _razorpay.CreateOrderAsync(
            new RazorpayOrderRequest(
                amountMinor,
                checkout.CurrencyCode,
                receipt,
                checkout.RazorpayNotes),
            cancellationToken);

        return new RazorpayCheckoutOrderResponse(
            checkout.TenantId,
            checkout.OrganisationId,
            checkout.QuoteId,
            checkout.CommerceOrderId,
            checkout.PlanId,
            checkout.PlanVersionId,
            checkout.Amount,
            checkout.CurrencyCode,
            checkout.ExpiresAtUtc,
            provider.Id,
            ProviderPublicId(),
            provider.Amount,
            provider.Receipt,
            provider.Status,
            provider.Notes);
    }

    private string ProviderPublicId()
    {
        var value = _configuration["Payments:RazorpayKeyId"];
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("Razorpay key id is not configured.")
            : value;
    }

    private static string CreateReceipt(Guid commerceOrderId) =>
        $"bos_{commerceOrderId:N}";

    private static long ToMinorUnits(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        return checked(decimal.ToInt64(amount * 100m));
    }
}
