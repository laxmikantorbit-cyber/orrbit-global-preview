namespace BusinessOS.Commerce;

public sealed class Quote
{
    public Quote(Guid id, Guid tenantId, Guid organisationId, CommercialSnapshot snapshot,
        DateTimeOffset createdAtUtc, DateTimeOffset validUntilUtc, Guid? opportunityId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Quote id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (organisationId == Guid.Empty) throw new ArgumentException("Organisation id is required.", nameof(organisationId));
        ArgumentNullException.ThrowIfNull(snapshot);
        if (validUntilUtc <= createdAtUtc) throw new ArgumentException("Quote validity must end after creation.");
        Id = id; TenantId = tenantId; OrganisationId = organisationId;
        OpportunityId = opportunityId; Snapshot = snapshot;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        ValidUntilUtc = validUntilUtc.ToUniversalTime();
        Status = QuoteStatus.Draft;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid OrganisationId { get; }
    public Guid? OpportunityId { get; }
    public CommercialSnapshot Snapshot { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset ValidUntilUtc { get; }
    public QuoteStatus Status { get; private set; }

    public void Accept(DateTimeOffset acceptedAtUtc)
    {
        if (Status != QuoteStatus.Draft) throw new InvalidOperationException("Only draft quote can be accepted.");
        if (acceptedAtUtc.ToUniversalTime() > ValidUntilUtc) throw new InvalidOperationException("Expired quote cannot be accepted.");
        Status = QuoteStatus.Accepted;
    }

    public void Cancel()
    {
        if (Status == QuoteStatus.Accepted) throw new InvalidOperationException("Accepted quote cannot be cancelled.");
        Status = QuoteStatus.Cancelled;
    }

    public Order CreateOrder(Guid orderId)
    {
        if (Status != QuoteStatus.Accepted) throw new InvalidOperationException("Quote must be accepted before order creation.");
        return new Order(orderId, TenantId, OrganisationId, Id, OpportunityId, Snapshot);
    }
}
