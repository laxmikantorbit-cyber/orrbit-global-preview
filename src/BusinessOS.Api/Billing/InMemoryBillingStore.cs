namespace BusinessOS.Api.Billing;

public sealed class InMemoryBillingStore : IBillingStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid TenantId, Guid InvoiceId), BillingInvoice> _invoices = [];
    private readonly Dictionary<(Guid TenantId, Guid OrderId), Guid> _orderIndex = [];
    private readonly Dictionary<(Guid TenantId, string FinancialYear), int> _sequences = [];

    public Task<BillingInvoice> EnsureInvoiceAsync(
        BillingInvoiceDraft draft,
        string invoicePrefix,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_orderIndex.TryGetValue((draft.TenantId, draft.OrderId), out var existingId))
                return Task.FromResult(_invoices[(draft.TenantId, existingId)]);

            var key = (draft.TenantId, draft.FinancialYear);
            var next = _sequences.GetValueOrDefault(key) + 1;
            _sequences[key] = next;
            var number = FormatInvoiceNumber(invoicePrefix, draft.FinancialYear, next);
            var invoice = ToInvoice(draft, Guid.NewGuid(), number);
            _invoices[(draft.TenantId, invoice.Id)] = invoice;
            _orderIndex[(draft.TenantId, draft.OrderId)] = invoice.Id;
            return Task.FromResult(invoice);
        }
    }

    public Task<BillingInvoice?> FindInvoiceAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_invoices.GetValueOrDefault((tenantId, invoiceId)));
    }

    public Task<BillingInvoice?> FindInvoiceByOrderAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_orderIndex.TryGetValue((tenantId, orderId), out var id)
                ? _invoices[(tenantId, id)]
                : null);
        }
    }

    public Task<IReadOnlyList<BillingInvoice>> ListInvoicesAsync(
        Guid tenantId,
        Guid? organisationId,
        Guid? subscriptionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var rows = _invoices.Values
                .Where(x => x.TenantId == tenantId)
                .Where(x => organisationId is null || x.OrganisationId == organisationId)
                .Where(x => subscriptionId is null || x.SubscriptionId == subscriptionId)
                .OrderByDescending(x => x.IssuedAtUtc)
                .ThenByDescending(x => x.InvoiceNumber)
                .Take(Math.Clamp(take, 1, 200))
                .ToArray();
            return Task.FromResult<IReadOnlyList<BillingInvoice>>(rows);
        }
    }

    internal static string FormatInvoiceNumber(
        string prefix, string financialYear, int sequence)
    {
        var safePrefix = string.IsNullOrWhiteSpace(prefix) ? "INV" : prefix.Trim().ToUpperInvariant();
        return $"{safePrefix}-{financialYear}-{sequence:000000}";
    }

    internal static BillingInvoice ToInvoice(
        BillingInvoiceDraft x, Guid id, string invoiceNumber) =>
        new(id, x.TenantId, x.OrganisationId, x.OrderId, x.SubscriptionId,
            invoiceNumber, x.InvoiceType, x.IssuedAtUtc, x.ProductCode, x.Description,
            x.CurrencyCode, x.Seller, x.Buyer, x.Tax, x.PaymentId, x.PaidAtUtc,
            x.FinancialYear, x.DocumentMode, x.TaxDocumentValid);
}
