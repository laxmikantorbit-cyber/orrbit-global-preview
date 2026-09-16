namespace BusinessOS.Crm;

public interface ICrmWorkRepository
{
    Task AddActivityAsync(LeadActivity activity, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeadActivity>> ListActivitiesAsync(Guid tenantId, Guid leadId, CancellationToken cancellationToken = default);
    Task AddFollowUpAsync(LeadFollowUp followUp, CancellationToken cancellationToken = default);
    Task<LeadFollowUp?> GetFollowUpAsync(Guid tenantId, Guid followUpId, CancellationToken cancellationToken = default);
    Task SaveFollowUpAsync(LeadFollowUp followUp, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeadFollowUp>> ListFollowUpsAsync(Guid tenantId, Guid? leadId = null, CancellationToken cancellationToken = default);
    Task AddTaskAsync(CrmTask task, CancellationToken cancellationToken = default);
    Task<CrmTask?> GetTaskAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken = default);
    Task SaveTaskAsync(CrmTask task, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmTask>> ListTasksAsync(Guid tenantId, Guid? leadId = null, CancellationToken cancellationToken = default);
}

public sealed class InMemoryCrmWorkRepository : ICrmWorkRepository
{
    private readonly Dictionary<Guid, LeadActivity> _activities = [];
    private readonly Dictionary<Guid, LeadFollowUp> _followUps = [];
    private readonly Dictionary<Guid, CrmTask> _tasks = [];
    private readonly object _gate = new();

    public Task AddActivityAsync(LeadActivity activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) AddUnique(_activities, activity.Id, activity);
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<LeadActivity>> ListActivitiesAsync(Guid tenantId, Guid leadId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<LeadActivity>>(_activities.Values
                .Where(x => x.TenantId == tenantId && x.LeadId == leadId)
                .OrderByDescending(x => x.OccurredAtUtc)
                .ToArray());
    }

    public Task AddFollowUpAsync(LeadFollowUp followUp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(followUp);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) AddUnique(_followUps, followUp.Id, followUp);
        return Task.CompletedTask;
    }

    public Task<LeadFollowUp?> GetFollowUpAsync(Guid tenantId, Guid followUpId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _followUps.GetValueOrDefault(followUpId);
            return Task.FromResult(item?.TenantId == tenantId ? item : null);
        }
    }
    public Task SaveFollowUpAsync(LeadFollowUp followUp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(followUp);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_followUps.ContainsKey(followUp.Id)) throw new InvalidOperationException("Follow-up does not exist.");
            _followUps[followUp.Id] = followUp;
        }
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<LeadFollowUp>> ListFollowUpsAsync(Guid tenantId, Guid? leadId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<LeadFollowUp>>(_followUps.Values
                .Where(x => x.TenantId == tenantId && (!leadId.HasValue || x.LeadId == leadId.Value))
                .OrderBy(x => x.Status)
                .ThenBy(x => x.DueAtUtc)
                .ToArray());
    }

    public Task AddTaskAsync(CrmTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) AddUnique(_tasks, task.Id, task);
        return Task.CompletedTask;
    }

    public Task<CrmTask?> GetTaskAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _tasks.GetValueOrDefault(taskId);
            return Task.FromResult(item?.TenantId == tenantId ? item : null);
        }
    }
    public Task SaveTaskAsync(CrmTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_tasks.ContainsKey(task.Id)) throw new InvalidOperationException("Task does not exist.");
            _tasks[task.Id] = task;
        }
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<CrmTask>> ListTasksAsync(Guid tenantId, Guid? leadId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<CrmTask>>(_tasks.Values
                .Where(x => x.TenantId == tenantId && (!leadId.HasValue || x.LeadId == leadId.Value))
                .OrderBy(x => x.Status)
                .ThenBy(x => x.DueAtUtc ?? DateTimeOffset.MaxValue)
                .ToArray());
    }

    private static void AddUnique<T>(Dictionary<Guid, T> store, Guid id, T value)
    {
        if (store.ContainsKey(id)) throw new InvalidOperationException("CRM item id already exists.");
        store.Add(id, value);
    }
}
