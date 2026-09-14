using BusinessOS.Catalog;
using BusinessOS.Licensing;

namespace BusinessOS.Commerce;

public sealed class SubscriptionEntitlement
{
    private readonly List<SubscriptionRenewal> _renewals = new();

    private SubscriptionEntitlement(Guid id, Order order, DateOnly startsOn, DateOnly? validUntil)
        : this(
            id,
            order.TenantId,
            order.OrganisationId,
            order.Id,
            order.Snapshot.PlanId,
            order.Snapshot.PlanVersionId,
            startsOn,
            validUntil,
            order.Snapshot.ToLicensingSnapshot(),
            SubscriptionStatus.Active)
    {
    }

    private SubscriptionEntitlement(
        Guid id,
        Guid tenantId,
        Guid organisationId,
        Guid orderId,
        Guid planId,
        Guid planVersionId,
        DateOnly startsOn,
        DateOnly? validUntil,
        EntitlementSnapshot entitlements,
        SubscriptionStatus status)
    {
        Id = id;
        TenantId = tenantId;
        OrganisationId = organisationId;
        OrderId = orderId;
        PlanId = planId;
        PlanVersionId = planVersionId;
        StartsOn = startsOn;
        ValidUntil = validUntil;
        Entitlements = entitlements;
        Status = status;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid OrganisationId { get; }
    public Guid OrderId { get; }
    public Guid PlanId { get; }
    public Guid PlanVersionId { get; private set; }
    public DateOnly StartsOn { get; }
    public DateOnly? ValidUntil { get; private set; }
    public EntitlementSnapshot Entitlements { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public IReadOnlyList<SubscriptionRenewal> Renewals => _renewals;

    public static SubscriptionEntitlement FromOrder(Guid id, Order order, DateTimeOffset paidAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Subscription id is required.", nameof(id));
        ArgumentNullException.ThrowIfNull(order);

        var startsOn = DateOnly.FromDateTime(paidAtUtc.UtcDateTime);
        var months = ResolveTermMonths(order.Snapshot.Billing);
        DateOnly? validUntil = months is null
            ? null
            : startsOn.AddMonths(months.Value).AddDays(-1);

        return new SubscriptionEntitlement(id, order, startsOn, validUntil);
    }

    public static SubscriptionEntitlement Rehydrate(
        Guid id,
        Guid tenantId,
        Guid organisationId,
        Guid orderId,
        Guid planId,
        Guid planVersionId,
        DateOnly startsOn,
        DateOnly? validUntil,
        EntitlementSnapshot entitlements,
        SubscriptionStatus status)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || organisationId == Guid.Empty ||
            orderId == Guid.Empty || planId == Guid.Empty || planVersionId == Guid.Empty)
            throw new ArgumentException("Persisted subscription identity is incomplete.");
        ArgumentNullException.ThrowIfNull(entitlements);
        return new SubscriptionEntitlement(
            id, tenantId, organisationId, orderId, planId, planVersionId,
            startsOn, validUntil, entitlements, status);
    }

    internal SubscriptionRenewal ApplyRenewal(Guid renewalId, Order order)
    {
        if (renewalId == Guid.Empty)
            throw new ArgumentException("Renewal id is required.", nameof(renewalId));
        ArgumentNullException.ThrowIfNull(order);
        if (Status != SubscriptionStatus.Active)
            throw new InvalidOperationException("Only an active subscription can be renewed.");
        if (order.Status != OrderStatus.Paid || order.PaidAtUtc is null)
            throw new InvalidOperationException("Captured payment is required before renewal.");
        if (order.TenantId != TenantId || order.OrganisationId != OrganisationId)
            throw new InvalidOperationException("Renewal order must belong to the same tenant and organisation.");
        if (order.Snapshot.PlanId != PlanId)
            throw new InvalidOperationException("Renewal order must belong to the same commercial plan.");

        var months = ResolveTermMonths(order.Snapshot.Billing)
            ?? throw new InvalidOperationException("A renewable term is required.");
        var paidOn = DateOnly.FromDateTime(order.PaidAtUtc.Value.UtcDateTime);
        var periodStart = ValidUntil is not null && ValidUntil.Value >= paidOn
            ? ValidUntil.Value.AddDays(1)
            : paidOn;
        var previousValidUntil = ValidUntil;
        var newValidUntil = periodStart.AddMonths(months).AddDays(-1);

        ValidUntil = newValidUntil;
        PlanVersionId = order.Snapshot.PlanVersionId;
        Entitlements = order.Snapshot.ToLicensingSnapshot();

        var renewal = new SubscriptionRenewal(
            renewalId, Id, order.Id, order.PaidAtUtc.Value,
            previousValidUntil, newValidUntil, months, PlanVersionId);
        _renewals.Add(renewal);
        return renewal;
    }
    public void Cancel() => Status = SubscriptionStatus.Cancelled;

    private static int? ResolveTermMonths(BillingRule billing) =>
        billing.TermMonths ?? billing.Cycle switch
        {
            BillingCycle.Monthly => 1,
            BillingCycle.Annual => 12,
            _ => (int?)null
        };
}
