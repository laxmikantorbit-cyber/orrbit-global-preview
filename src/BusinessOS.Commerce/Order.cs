using BusinessOS.Catalog;

namespace BusinessOS.Commerce;

public sealed class Order
{
    public Order(Guid id, Guid tenantId, Guid organisationId, Guid quoteId,
        Guid? opportunityId, CommercialSnapshot snapshot)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || organisationId == Guid.Empty || quoteId == Guid.Empty)
            throw new ArgumentException("Order, tenant, organisation and quote ids are required.");
        ArgumentNullException.ThrowIfNull(snapshot);
        Id = id;
        TenantId = tenantId;
        OrganisationId = organisationId;
        QuoteId = quoteId;
        OpportunityId = opportunityId;
        Snapshot = snapshot;
        Status = OrderStatus.PendingPayment;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid OrganisationId { get; }
    public Guid QuoteId { get; }
    public Guid? OpportunityId { get; }
    public CommercialSnapshot Snapshot { get; }
    public OrderStatus Status { get; private set; }
    public string? PaymentId { get; private set; }
    public DateTimeOffset? PaidAtUtc { get; private set; }

    public void CapturePayment(string paymentId, DateTimeOffset paidAtUtc)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
            throw new ArgumentException("Payment id is required.", nameof(paymentId));
        if (Status == OrderStatus.Activated)
            throw new InvalidOperationException("Activated order cannot receive another payment.");
        if (Status == OrderStatus.Paid)
        {
            if (string.Equals(PaymentId, paymentId.Trim(), StringComparison.Ordinal))
                return;
            throw new InvalidOperationException("Order is already paid by a different payment.");
        }
        if (Status != OrderStatus.PendingPayment)
            throw new InvalidOperationException("Order cannot be paid in current state.");

        PaymentId = paymentId.Trim();
        PaidAtUtc = paidAtUtc.ToUniversalTime();
        Status = OrderStatus.Paid;
    }

    public SubscriptionEntitlement Activate(Guid subscriptionId)
    {
        if (Status != OrderStatus.Paid || PaidAtUtc is null)
            throw new InvalidOperationException("Captured payment is required before activation.");
        var subscription = SubscriptionEntitlement.FromOrder(subscriptionId, this, PaidAtUtc.Value);
        Status = OrderStatus.Activated;
        return subscription;
    }

    public SubscriptionRenewal ActivateRenewal(Guid renewalId, SubscriptionEntitlement subscription)
    {
        if (Status != OrderStatus.Paid || PaidAtUtc is null)
            throw new InvalidOperationException("Captured payment is required before renewal activation.");
        ArgumentNullException.ThrowIfNull(subscription);

        var renewal = subscription.ApplyRenewal(renewalId, this);
        Status = OrderStatus.Activated;
        return renewal;
    }
}
