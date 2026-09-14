using BusinessOS.Catalog;
using BusinessOS.Licensing;

namespace BusinessOS.Commerce;

public sealed class SubscriptionEntitlement
{
    private SubscriptionEntitlement(Guid id, Order order, DateOnly startsOn, DateOnly? validUntil)
    {
        Id = id; TenantId = order.TenantId; OrganisationId = order.OrganisationId;
        OrderId = order.Id; PlanId = order.Snapshot.PlanId;
        PlanVersionId = order.Snapshot.PlanVersionId;
        StartsOn = startsOn; ValidUntil = validUntil;
        Entitlements = order.Snapshot.ToLicensingSnapshot();
        Status = SubscriptionStatus.Active;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid OrganisationId { get; }
    public Guid OrderId { get; }
    public Guid PlanId { get; }
    public Guid PlanVersionId { get; }
    public DateOnly StartsOn { get; }
    public DateOnly? ValidUntil { get; }
    public EntitlementSnapshot Entitlements { get; }
    public SubscriptionStatus Status { get; private set; }

    public static SubscriptionEntitlement FromOrder(Guid id, Order order, DateTimeOffset paidAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Subscription id is required.", nameof(id));
        ArgumentNullException.ThrowIfNull(order);
        var startsOn = DateOnly.FromDateTime(paidAtUtc.UtcDateTime);
        var months = order.Snapshot.Billing.TermMonths ?? order.Snapshot.Billing.Cycle switch
        {
            BillingCycle.Monthly => 1,
            BillingCycle.Annual => 12,
            _ => (int?)null
        };
        DateOnly? validUntil = months is null ? null : startsOn.AddMonths(months.Value).AddDays(-1);
        return new SubscriptionEntitlement(id, order, startsOn, validUntil);
    }

    public void Cancel() => Status = SubscriptionStatus.Cancelled;
}
