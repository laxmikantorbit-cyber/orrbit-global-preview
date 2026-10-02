using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public sealed class CrmTimesheetEntry
{
    public CrmTimesheetEntry(
        Guid id,
        Guid tenantId,
        Guid userId,
        DateOnly workDate,
        int minutes,
        string activity,
        bool billable,
        Guid? projectId = null,
        Guid? taskId = null,
        Guid? accountId = null,
        string? notes = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Timesheet id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (userId == Guid.Empty) throw new ArgumentException("User is required.", nameof(userId));
        Validate(minutes, activity);
        Id = id;
        TenantId = tenantId;
        UserId = userId;
        WorkDate = workDate;
        Minutes = minutes;
        Activity = activity.Trim();
        Billable = billable;
        ProjectId = projectId;
        TaskId = taskId;
        AccountId = accountId;
        Notes = Clean(notes);
        Status = "Draft";
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid UserId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public int Minutes { get; private set; }
    public string Activity { get; private set; }
    public bool Billable { get; private set; }
    public Guid? ProjectId { get; private set; }
    public Guid? TaskId { get; private set; }
    public Guid? AccountId { get; private set; }
    public string? Notes { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(
        Guid userId,
        DateOnly workDate,
        int minutes,
        string activity,
        bool billable,
        Guid? projectId,
        Guid? taskId,
        Guid? accountId,
        string? notes)
    {
        if (Status is not ("Draft" or "Rejected"))
            throw new InvalidOperationException("Only draft or rejected timesheets can be edited.");
        if (userId == Guid.Empty) throw new ArgumentException("User is required.", nameof(userId));
        Validate(minutes, activity);
        UserId = userId;
        WorkDate = workDate;
        Minutes = minutes;
        Activity = activity.Trim();
        Billable = billable;
        ProjectId = projectId;
        TaskId = taskId;
        AccountId = accountId;
        Notes = Clean(notes);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void ChangeStatus(string status)
    {
        var next = NormalizeStatus(status);
        var valid = (Status, next) switch
        {
            ("Draft", "Submitted") => true,
            ("Rejected", "Submitted") => true,
            ("Submitted", "Approved") => true,
            ("Submitted", "Rejected") => true,
            _ => false
        };
        if (!valid) throw new InvalidOperationException($"Cannot move {Status} timesheet to {next}.");
        Status = next;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public static CrmTimesheetEntry Restore(
        Guid id, Guid tenantId, Guid userId, DateOnly workDate, int minutes, string activity,
        bool billable, Guid? projectId, Guid? taskId, Guid? accountId, string? notes,
        string status, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
    {
        var entry = new CrmTimesheetEntry(
            id, tenantId, userId, workDate, minutes, activity, billable,
            projectId, taskId, accountId, notes, createdAtUtc);
        entry.Status = NormalizeStatus(status);
        entry.UpdatedAtUtc = updatedAtUtc;
        return entry;
    }

    private static void Validate(int minutes, string activity)
    {
        if (minutes is < 1 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(minutes), "Duration must be between 1 and 1440 minutes.");
        if (string.IsNullOrWhiteSpace(activity))
            throw new ArgumentException("Activity is required.", nameof(activity));
    }

    private static string NormalizeStatus(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "draft" => "Draft",
            "submitted" => "Submitted",
            "approved" => "Approved",
            "rejected" => "Rejected",
            _ => throw new ArgumentException("Timesheet status must be Draft, Submitted, Approved or Rejected.", nameof(value))
        };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public interface ICrmTimesheetStore
{
    Task AddAsync(CrmTimesheetEntry entry, CancellationToken cancellationToken = default);
    Task SaveAsync(CrmTimesheetEntry entry, CancellationToken cancellationToken = default);
    Task<CrmTimesheetEntry?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmTimesheetEntry>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryCrmTimesheetStore : ICrmTimesheetStore
{
    private readonly Dictionary<Guid, CrmTimesheetEntry> _items = [];
    private readonly object _gate = new();

    public Task AddAsync(CrmTimesheetEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_items.ContainsKey(entry.Id)) throw new InvalidOperationException("Timesheet entry already exists.");
            _items.Add(entry.Id, entry);
        }
        return Task.CompletedTask;
    }

    public Task SaveAsync(CrmTimesheetEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_items.ContainsKey(entry.Id)) throw new InvalidOperationException("Timesheet entry does not exist.");
            _items[entry.Id] = entry;
        }
        return Task.CompletedTask;
    }

    public Task<CrmTimesheetEntry?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _items.GetValueOrDefault(id);
            return Task.FromResult(item?.TenantId == tenantId ? item : null);
        }
    }

    public Task<IReadOnlyList<CrmTimesheetEntry>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<CrmTimesheetEntry>>(
                _items.Values.Where(x => x.TenantId == tenantId)
                    .OrderByDescending(x => x.WorkDate)
                    .ThenByDescending(x => x.UpdatedAtUtc)
                    .ToArray());
    }
}

public sealed class PostgresCrmTimesheetStore : ICrmTimesheetStore
{
    private readonly CrmPostgresDatabase _db;
    public PostgresCrmTimesheetStore(CrmPostgresDatabase db) => _db = db;

    public async Task AddAsync(CrmTimesheetEntry entry, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
INSERT INTO businessos_crm.timesheet_entries(
 id,tenant_id,user_id,work_date,minutes,activity,billable,project_id,task_id,account_id,notes,status,created_at_utc,updated_at_utc)
VALUES(
 @id,@tenant,@user,@workDate,@minutes,@activity,@billable,@project,@task,@account,@notes,@status,@created,@updated);
""");
        AddParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(CrmTimesheetEntry entry, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
UPDATE businessos_crm.timesheet_entries SET
 user_id=@user,work_date=@workDate,minutes=@minutes,activity=@activity,billable=@billable,
 project_id=@project,task_id=@task,account_id=@account,notes=@notes,status=@status,updated_at_utc=@updated
WHERE tenant_id=@tenant AND id=@id;
""");
        AddParameters(command, entry);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Timesheet entry does not exist.");
    }

    public async Task<CrmTimesheetEntry?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(SelectSql + " WHERE tenant_id=@tenant AND id=@id LIMIT 1");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<CrmTimesheetEntry>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(
            SelectSql + " WHERE tenant_id=@tenant ORDER BY work_date DESC,updated_at_utc DESC");
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CrmTimesheetEntry>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    private const string SelectSql = """
SELECT id,tenant_id,user_id,work_date,minutes,activity,billable,project_id,task_id,account_id,notes,status,created_at_utc,updated_at_utc
FROM businessos_crm.timesheet_entries
""";

    private static void AddParameters(NpgsqlCommand command, CrmTimesheetEntry entry)
    {
        command.Parameters.AddWithValue("id", entry.Id);
        command.Parameters.AddWithValue("tenant", entry.TenantId);
        command.Parameters.AddWithValue("user", entry.UserId);
        command.Parameters.AddWithValue("workDate", NpgsqlDbType.Date, entry.WorkDate);
        command.Parameters.AddWithValue("minutes", entry.Minutes);
        command.Parameters.AddWithValue("activity", entry.Activity);
        command.Parameters.AddWithValue("billable", entry.Billable);
        Nullable(command, "project", NpgsqlDbType.Uuid, entry.ProjectId);
        Nullable(command, "task", NpgsqlDbType.Uuid, entry.TaskId);
        Nullable(command, "account", NpgsqlDbType.Uuid, entry.AccountId);
        Nullable(command, "notes", NpgsqlDbType.Text, entry.Notes);
        command.Parameters.AddWithValue("status", entry.Status);
        command.Parameters.AddWithValue("created", entry.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", entry.UpdatedAtUtc);
    }

    private static CrmTimesheetEntry Read(NpgsqlDataReader reader) =>
        CrmTimesheetEntry.Restore(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetFieldValue<DateOnly>(3),
            reader.GetInt32(4), reader.GetString(5), reader.GetBoolean(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetString(11), reader.GetFieldValue<DateTimeOffset>(12), reader.GetFieldValue<DateTimeOffset>(13));

    private static void Nullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
}
