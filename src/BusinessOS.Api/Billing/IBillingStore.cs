namespace BusinessOS.Api.Billing;

public interface IBillingStore
{
    Task<BillingInvoice> EnsureInvoiceAsync(
        BillingInvoiceDraft draft,
        string invoicePrefix,
        CancellationToken cancellationToken = default);

    Task<BillingInvoice?> FindInvoiceAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken = default);

    Task<BillingInvoice?> FindInvoiceByOrderAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingInvoice>> ListInvoicesAsync(
        Guid tenantId,
        Guid? organisationId,
        Guid? subscriptionId,
        int take,
        CancellationToken cancellationToken = default);
}
