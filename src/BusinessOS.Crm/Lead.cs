namespace BusinessOS.Crm;

public sealed class Lead
{
    private readonly HashSet<string> _tags = new(StringComparer.OrdinalIgnoreCase);

    public Lead(
        Guid id,
        Guid tenantId,
        Guid organisationId,
        string title,
        LeadAttribution attribution,
        string? contactName = null,
        string? mobileNumber = null,
        string? email = null,
        string? productInterest = null,
        string? notes = null,
        LeadPriority priority = LeadPriority.Normal,
        DateTimeOffset? createdAtUtc = null,
        decimal? estimatedValue = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Lead id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (organisationId == Guid.Empty) throw new ArgumentException("Organisation id is required.", nameof(organisationId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Lead title is required.", nameof(title));
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        if (estimatedValue < 0) throw new ArgumentOutOfRangeException(nameof(estimatedValue));
        Id = id;
        TenantId = tenantId;
        OrganisationId = organisationId;
        Title = title.Trim();
        Attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
        ContactName = Clean(contactName);
        MobileNumber = Clean(mobileNumber);
        Email = Clean(email);
        ProductInterest = Clean(productInterest);
        Notes = Clean(notes);
        EstimatedValue = estimatedValue;
        Priority = priority;
        Status = LeadStatus.New;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid OrganisationId { get; private set; }
    public string Title { get; private set; }
    public string? ContactName { get; private set; }
    public string? MobileNumber { get; private set; }
    public string? Email { get; private set; }
    public string? ProductInterest { get; private set; }
    public string? Notes { get; private set; }
    public decimal? EstimatedValue { get; private set; }
    public LeadStatus Status { get; private set; }
    public LeadPriority Priority { get; private set; }
    public LeadAttribution Attribution { get; private set; }
    public string? UnqualifiedReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? LastContactAtUtc { get; private set; }
    public DateTimeOffset? NextFollowUpAtUtc { get; private set; }
    public IReadOnlyCollection<string> Tags => _tags;

    public static Lead Restore(
        Guid id, Guid tenantId, Guid organisationId, string title, LeadAttribution attribution,
        string? contactName, string? mobileNumber, string? email, string? productInterest,
        string? notes, LeadPriority priority, LeadStatus status, string? unqualifiedReason,
        DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc, DateTimeOffset? lastContactAtUtc,
        DateTimeOffset? nextFollowUpAtUtc, IEnumerable<string>? tags = null, decimal? estimatedValue = null)
    {
        var lead = new Lead(id, tenantId, organisationId, title, attribution, contactName,
            mobileNumber, email, productInterest, notes, priority, createdAtUtc, estimatedValue)
        {
            Status = status,
            UnqualifiedReason = Clean(unqualifiedReason),
            UpdatedAtUtc = updatedAtUtc,
            LastContactAtUtc = lastContactAtUtc,
            NextFollowUpAtUtc = nextFollowUpAtUtc
        };
        if (tags is not null)
            foreach (var tag in tags.Where(x => !string.IsNullOrWhiteSpace(x))) lead._tags.Add(tag.Trim());
        return lead;
    }
    public void UpdateProfile(
        string title,
        string? contactName,
        string? mobileNumber,
        string? email,
        string? productInterest,
        string? notes)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Lead title is required.", nameof(title));
        Title = title.Trim();
        ContactName = Clean(contactName);
        MobileNumber = Clean(mobileNumber);
        Email = Clean(email);
        ProductInterest = Clean(productInterest);
        Notes = Clean(notes);
        Touch();
    }
    public void SetEstimatedValue(decimal? estimatedValue)
    {
        EnsureOpen();
        if (estimatedValue < 0) throw new ArgumentOutOfRangeException(nameof(estimatedValue));
        EstimatedValue = estimatedValue;
        Touch();
    }

    public void SetPriority(LeadPriority priority)
    {
        EnsureOpen();
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        Priority = priority;
        Touch();
    }

    public void AssignOwner(Guid? ownerUserId)
    {
        EnsureOpen();
        Attribution = Attribution with { AccountOwnerUserId = ownerUserId };
        Touch();
    }

    public void LinkOrganisation(Guid organisationId)
    {
        EnsureOpen();
        if (organisationId == Guid.Empty)
            throw new ArgumentException("Organisation id is required.", nameof(organisationId));
        OrganisationId = organisationId;
        Touch();
    }

    public void AddTag(string tag)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(tag)) throw new ArgumentException("Tag is required.", nameof(tag));
        _tags.Add(tag.Trim());
        Touch();
    }

    public void RemoveTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        if (_tags.Remove(tag.Trim())) Touch();
    }
    public void MarkContacted(DateTimeOffset? contactedAtUtc = null)
    {
        EnsureOpen();
        Status = LeadStatus.Contacted;
        LastContactAtUtc = contactedAtUtc ?? DateTimeOffset.UtcNow;
        Touch();
    }

    public void Qualify()
    {
        EnsureOpen();
        Status = LeadStatus.Qualified;
        UnqualifiedReason = null;
        Touch();
    }

    public void MarkUnqualified(string reason)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Unqualified reason is required.", nameof(reason));
        Status = LeadStatus.Unqualified;
        UnqualifiedReason = reason.Trim();
        NextFollowUpAtUtc = null;
        Touch();
    }

    public void Convert()
    {
        if (Status != LeadStatus.Qualified)
            throw new InvalidOperationException("Only a qualified lead can be converted.");        Status = LeadStatus.Converted;
        NextFollowUpAtUtc = null;
        Touch();
    }

    public void Reopen(LeadStatus target = LeadStatus.Contacted)
    {
        if (Status is not (LeadStatus.Converted or LeadStatus.Unqualified))
            throw new InvalidOperationException("Only a closed lead can be reopened.");
        if (target is not (LeadStatus.New or LeadStatus.Contacted or LeadStatus.Qualified))
            throw new ArgumentException("Reopen target must be an open status.", nameof(target));
        Status = target;
        UnqualifiedReason = null;
        Touch();
    }

    public void ScheduleNextFollowUp(DateTimeOffset? dueAtUtc)
    {
        EnsureOpen();
        NextFollowUpAtUtc = dueAtUtc;
        Touch();
    }

    public void RecordContact(DateTimeOffset? contactedAtUtc = null)
    {
        EnsureOpen();
        LastContactAtUtc = contactedAtUtc ?? DateTimeOffset.UtcNow;
        if (Status == LeadStatus.New) Status = LeadStatus.Contacted;
        Touch();
    }
    public void UpdateAttribution(LeadAttribution attribution)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(attribution);
        Attribution = attribution;
        Touch();
    }

    private void EnsureOpen()
    {
        if (Status is LeadStatus.Converted or LeadStatus.Unqualified)
            throw new InvalidOperationException("Closed lead cannot be changed.");
    }

    private void Touch() => UpdatedAtUtc = DateTimeOffset.UtcNow;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
