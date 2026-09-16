namespace BusinessOS.Crm;

public sealed class LeadActivity
{
    public LeadActivity(
        Guid id,
        Guid tenantId,
        Guid leadId,
        CrmActivityType type,
        string summary,
        string? details = null,
        Guid? actorUserId = null,
        DateTimeOffset? occurredAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Activity id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (leadId == Guid.Empty) throw new ArgumentException("Lead id is required.", nameof(leadId));
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (string.IsNullOrWhiteSpace(summary)) throw new ArgumentException("Activity summary is required.", nameof(summary));
        Id = id;
        TenantId = tenantId;
        LeadId = leadId;
        Type = type;
        Summary = summary.Trim();
        Details = Clean(details);
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc ?? DateTimeOffset.UtcNow;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid LeadId { get; }
    public CrmActivityType Type { get; }
    public string Summary { get; }
    public string? Details { get; }
    public Guid? ActorUserId { get; }
    public DateTimeOffset OccurredAtUtc { get; }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class LeadFollowUp
{
    public LeadFollowUp(
        Guid id,
        Guid tenantId,
        Guid leadId,
        DateTimeOffset dueAtUtc,
        FollowUpChannel channel,
        string purpose,
        Guid? ownerUserId = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Follow-up id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (leadId == Guid.Empty) throw new ArgumentException("Lead id is required.", nameof(leadId));
        if (!Enum.IsDefined(channel)) throw new ArgumentOutOfRangeException(nameof(channel));
        if (string.IsNullOrWhiteSpace(purpose)) throw new ArgumentException("Follow-up purpose is required.", nameof(purpose));
        Id = id;
        TenantId = tenantId;
        LeadId = leadId;
        DueAtUtc = dueAtUtc;
        Channel = channel;
        Purpose = purpose.Trim();
        OwnerUserId = ownerUserId;
        Status = CrmWorkStatus.Open;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid LeadId { get; private set; }
    public DateTimeOffset DueAtUtc { get; private set; }
    public FollowUpChannel Channel { get; private set; }
    public string Purpose { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public CrmWorkStatus Status { get; private set; }
    public string? Outcome { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public void Reschedule(DateTimeOffset dueAtUtc, FollowUpChannel channel, string purpose, Guid? ownerUserId)
    {
        EnsureOpen();
        if (!Enum.IsDefined(channel)) throw new ArgumentOutOfRangeException(nameof(channel));
        if (string.IsNullOrWhiteSpace(purpose)) throw new ArgumentException("Follow-up purpose is required.", nameof(purpose));
        DueAtUtc = dueAtUtc;
        Channel = channel;
        Purpose = purpose.Trim();
        OwnerUserId = ownerUserId;
    }

    public void MoveToLead(Guid leadId)
    {
        EnsureOpen();
        if (leadId == Guid.Empty) throw new ArgumentException("Lead id is required.", nameof(leadId));
        LeadId = leadId;
    }

    public void Complete(string? outcome = null, DateTimeOffset? completedAtUtc = null)
    {
        EnsureOpen();
        Status = CrmWorkStatus.Completed;
        Outcome = Clean(outcome);
        CompletedAtUtc = completedAtUtc ?? DateTimeOffset.UtcNow;
    }

    public void Cancel(string? reason = null)
    {
        EnsureOpen();
        Status = CrmWorkStatus.Cancelled;
        Outcome = Clean(reason);
    }

    private void EnsureOpen()
    {
        if (Status != CrmWorkStatus.Open) throw new InvalidOperationException("Follow-up is already closed.");
    }
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class CrmTask
{
    public CrmTask(
        Guid id,
        Guid tenantId,
        string title,
        DateTimeOffset? dueAtUtc = null,
        Guid? leadId = null,
        string? details = null,
        LeadPriority priority = LeadPriority.Normal,
        Guid? assigneeUserId = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Task id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Task title is required.", nameof(title));
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        Id = id;
        TenantId = tenantId;
        LeadId = leadId;
        Title = title.Trim();
        Details = Clean(details);
        DueAtUtc = dueAtUtc;
        Priority = priority;
        AssigneeUserId = assigneeUserId;
        Status = CrmWorkStatus.Open;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid? LeadId { get; private set; }
    public string Title { get; private set; }
    public string? Details { get; private set; }
    public DateTimeOffset? DueAtUtc { get; private set; }
    public LeadPriority Priority { get; private set; }
    public Guid? AssigneeUserId { get; private set; }
    public CrmWorkStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public void Complete(DateTimeOffset? completedAtUtc = null)
    {
        EnsureOpen();
        Status = CrmWorkStatus.Completed;
        CompletedAtUtc = completedAtUtc ?? DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        EnsureOpen();
        Status = CrmWorkStatus.Cancelled;
    }

    public void Update(string title, string? details, DateTimeOffset? dueAtUtc, LeadPriority priority, Guid? assigneeUserId)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Task title is required.", nameof(title));
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        Title = title.Trim();
        Details = Clean(details);
        DueAtUtc = dueAtUtc;
        Priority = priority;
        AssigneeUserId = assigneeUserId;
    }

    public void MoveToLead(Guid? leadId)
    {
        EnsureOpen();
        if (leadId.HasValue && leadId.Value == Guid.Empty) throw new ArgumentException("Lead id is invalid.", nameof(leadId));
        LeadId = leadId;
    }

    private void EnsureOpen()
    {
        if (Status != CrmWorkStatus.Open) throw new InvalidOperationException("Task is already closed.");
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
