namespace BusinessOS.Crm;

public enum CrmBusinessModule
{
    Expense = 1,
    Contract = 2,
    Project = 3,
    Ticket = 4
}

public sealed class CrmBusinessRecord
{
    private static readonly IReadOnlyDictionary<CrmBusinessModule, string[]> AllowedStatuses = new Dictionary<CrmBusinessModule, string[]>
    {
        [CrmBusinessModule.Expense] = ["Draft", "Approved", "Paid", "Rejected"],
        [CrmBusinessModule.Contract] = ["Draft", "Active", "Completed", "Expired", "Cancelled"],
        [CrmBusinessModule.Project] = ["Planned", "InProgress", "OnHold", "Completed", "Cancelled"],
        [CrmBusinessModule.Ticket] = ["Open", "InProgress", "Resolved", "Closed", "Cancelled"]
    };

    public CrmBusinessRecord(
        Guid id,
        Guid tenantId,
        CrmBusinessModule module,
        string title,
        Guid? accountId = null,
        decimal? amount = null,
        string? category = null,
        string? priority = null,
        DateOnly? startDate = null,
        DateOnly? dueDate = null,
        Guid? ownerUserId = null,
        string? description = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Business record id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (!Enum.IsDefined(module)) throw new ArgumentOutOfRangeException(nameof(module));
        Id = id;
        TenantId = tenantId;
        Module = module;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        Status = DefaultStatus(module);
        ApplyProfile(title, accountId, amount, category, priority, startDate, dueDate, ownerUserId, description, metadata, false);
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public CrmBusinessModule Module { get; }
    public Guid? AccountId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Status { get; private set; }
    public decimal? Amount { get; private set; }
    public string? Category { get; private set; }
    public string? Priority { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public string? Description { get; private set; }
    public IReadOnlyDictionary<string, string> Metadata { get; private set; } = new Dictionary<string, string>();
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void UpdateProfile(
        string title,
        Guid? accountId,
        decimal? amount,
        string? category,
        string? priority,
        DateOnly? startDate,
        DateOnly? dueDate,
        Guid? ownerUserId,
        string? description,
        IReadOnlyDictionary<string, string>? metadata) =>
        ApplyProfile(title, accountId, amount, category, priority, startDate, dueDate, ownerUserId, description, metadata, true);

    public void ChangeStatus(string status)
    {
        Status = NormalizeStatus(Module, status);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public static CrmBusinessRecord Restore(
        Guid id,
        Guid tenantId,
        CrmBusinessModule module,
        Guid? accountId,
        string title,
        string status,
        decimal? amount,
        string? category,
        string? priority,
        DateOnly? startDate,
        DateOnly? dueDate,
        Guid? ownerUserId,
        string? description,
        IReadOnlyDictionary<string, string>? metadata,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        var record = new CrmBusinessRecord(id, tenantId, module, title, accountId, amount, category, priority, startDate, dueDate, ownerUserId, description, metadata, createdAtUtc);
        record.Status = NormalizeStatus(module, status);
        record.UpdatedAtUtc = updatedAtUtc;
        return record;
    }

    public static string DefaultStatus(CrmBusinessModule module) => module switch
    {
        CrmBusinessModule.Expense => "Draft",
        CrmBusinessModule.Contract => "Draft",
        CrmBusinessModule.Project => "Planned",
        CrmBusinessModule.Ticket => "Open",
        _ => throw new ArgumentOutOfRangeException(nameof(module))
    };

    public static string NormalizeStatus(CrmBusinessModule module, string status)
    {
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));
        var value = status.Trim();
        var allowed = AllowedStatuses[module];
        var match = allowed.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (match is null) throw new ArgumentException($"{module} status is invalid.", nameof(status));
        return match;
    }

    private void ApplyProfile(
        string title,
        Guid? accountId,
        decimal? amount,
        string? category,
        string? priority,
        DateOnly? startDate,
        DateOnly? dueDate,
        Guid? ownerUserId,
        string? description,
        IReadOnlyDictionary<string, string>? metadata,
        bool touch)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));
        if (amount.HasValue && amount.Value < 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        if (startDate.HasValue && dueDate.HasValue && dueDate.Value < startDate.Value)
            throw new ArgumentException("Due date cannot be before start date.", nameof(dueDate));
        Title = title.Trim();
        AccountId = accountId;
        Amount = amount.HasValue ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero) : null;
        Category = Clean(category);
        Priority = Clean(priority);
        StartDate = startDate;
        DueDate = dueDate;
        OwnerUserId = ownerUserId;
        Description = Clean(description);
        Metadata = NormalizeMetadata(metadata);
        if (touch) UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata) =>
        metadata is null
            ? new Dictionary<string, string>()
            : metadata.Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                .ToDictionary(x => x.Key.Trim(), x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
