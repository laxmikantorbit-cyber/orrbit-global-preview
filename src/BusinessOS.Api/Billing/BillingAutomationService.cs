using BusinessOS.Api.Commerce;
using BusinessOS.Customers;
using Microsoft.Extensions.Options;

namespace BusinessOS.Api.Billing;

public sealed class BillingAutomationService
{
    private readonly ICommerceActivationStore _commerce;
    private readonly IOrganisationRepository _organisations;
    private readonly IBillingStore _billing;
    private readonly BillingOptions _options;
    private readonly IConfiguration _configuration;

    public BillingAutomationService(
        ICommerceActivationStore commerce,
        IOrganisationRepository organisations,
        IBillingStore billing,
        IOptions<BillingOptions> options,
        IConfiguration configuration)
    {
        _commerce = commerce;
        _organisations = organisations;
        _billing = billing;
        _options = options.Value;
        _configuration = configuration;
    }

    public async Task<BillingInvoice?> EnsureForOrderAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _billing.FindInvoiceByOrderAsync(
            tenantId, orderId, cancellationToken);
        if (existing is not null) return existing;

        var source = await _commerce.FindOrderBillingSourceAsync(
            tenantId, orderId, cancellationToken);
        if (source is null) return null;

        var organisation = await _organisations.GetAsync(
            tenantId, source.OrganisationId, cancellationToken);
        if (organisation is null) return null;

        var buyerState = BillingCalculator.StateCodeFromGstin(organisation.Gstin)
            ?? _options.SellerStateCode;
        var draft = BillingCalculator.CreateDraft(
            source,
            organisation,
            _options,
            new BillingInvoiceCreateRequest(buyerState, _options.SellerStateCode),
            _configuration["BusinessOS:DeploymentMode"] ?? "Unknown");
        return await _billing.EnsureInvoiceAsync(
            draft, _options.InvoicePrefix, cancellationToken);
    }
}
