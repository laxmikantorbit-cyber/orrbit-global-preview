namespace BusinessOS.Crm;

public sealed class Lead
{
    public Lead(
        Guid id,
        Guid tenantId,
        Guid organisationId,
        string title,
        LeadAttribution attribution)
    {
        if (id == Guid.Empty) throw new ArgumentException("Lead id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (organisationId == Guid.Empty) throw new ArgumentException("Organisation id is required.", nameof(organisationId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Lead title is required.", nameof(title));

        Id = id;
        TenantId = tenantId;
        OrganisationId = organisationId;
        Title = title.Trim();
        Attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
        Status = LeadStatus.New;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid OrganisationId { get; }
    public string Title { get; private set; }
    public LeadStatus Status { get; private set; }
    public LeadAttribution Attribution { get; private set; }
    public string? UnqualifiedReason { get; private set; }

    public void MarkContacted()
    {
        EnsureOpen();
        Status = LeadStatus.Contacted;
    }

    public void Qualify()
    {
        EnsureOpen();
        Status = LeadStatus.Qualified;
        UnqualifiedReason = null;
    }

    public void MarkUnqualified(string reason)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Unqualified reason is required.", nameof(reason));
        Status = LeadStatus.Unqualified;
        UnqualifiedReason = reason.Trim();
    }

    public void Convert()
    {
        if (Status != LeadStatus.Qualified)
            throw new InvalidOperationException("Only a qualified lead can be converted.");
        Status = LeadStatus.Converted;
    }

    public void UpdateAttribution(LeadAttribution attribution)
    {
        ArgumentNullException.ThrowIfNull(attribution);
        Attribution = attribution;
    }

    private void EnsureOpen()
    {
        if (Status is LeadStatus.Converted or LeadStatus.Unqualified)
            throw new InvalidOperationException("Closed lead cannot change status.");
    }
}
