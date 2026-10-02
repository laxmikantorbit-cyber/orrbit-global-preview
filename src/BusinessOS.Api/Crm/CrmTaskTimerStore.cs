using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public sealed class CrmTaskTimerEntry
{
    public CrmTaskTimerEntry(
        Guid id,
        Guid tenantId,
        Guid taskId,
        Guid userId,
        DateTimeOffset startedAtUtc,
        string? note = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Timer id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (taskId == Guid.Empty) throw new ArgumentException("Task id is required.", nameof(taskId));
        if (userId == Guid.Empty) throw new ArgumentException("User id is required.", nameof(userId));
        Id = id;
        TenantId = tenantId;
        TaskId = taskId;
        UserId = userId;
        StartedAtUtc = startedAtUtc;
        Note = Clean(note);
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid TaskId { get; }
    public Guid UserId { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset? StoppedAtUtc { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public bool IsRunning => !StoppedAtUtc.HasValue;
    public long DurationSeconds =>
        Math.Max(0, (long)Math.Floor(((StoppedAtUtc ?? DateTimeOffset.UtcNow) - StartedAtUtc).TotalSeconds));

    public void Stop(DateTimeOffset stoppedAtUtc)
    {
        if (StoppedAtUtc.HasValue) throw new InvalidOperationException("Timer is already stopped.");
        if (stoppedAtUtc < StartedAtUtc) throw new ArgumentException("Stop time cannot be before start time.", nameof(stoppedAtUtc));
        StoppedAtUtc = stoppedAtUtc;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public static CrmTaskTimerEntry Restore(
        Guid id,
        Guid tenantId,
        Guid taskId,
        Guid userId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? stoppedAtUtc,
        string? note,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        var entry = new CrmTaskTimerEntry(id, tenantId, taskId, userId, startedAtUtc, note, createdAtUtc);
        entry.StoppedAtUtc = stoppedAtUtc;
        entry.UpdatedAtUtc = updatedAtUtc;
        return entry;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public interface ICrmTaskTimerStore
{
    Task AddAsync(CrmTaskTimerEntry entry, CancellationToken cancellationToken = default);
    Task SaveAsync(CrmTaskTimerEntry entry, CancellationToken cancellationToken = default);
    Task<CrmTaskTimerEntry?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmTaskTimerEntry>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryCrmTaskTimerStore : ICrmTaskTimerStore
{
    private readonly Dictionary<Guid, CrmTaskTimerEntry> _items = [];
    private readonly object _gate = new();

    public Task AddAsync(CrmTaskTimerEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_items.ContainsKey(entry.Id)) throw new InvalidOperationException("Task timer already exists.");
            _items.Add(entry.Id, entry);
        }
        return Task.CompletedTask;
    }

    public Task SaveAsync(CrmTaskTimerEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_items.ContainsKey(entry.Id)) throw new InvalidOperationException("Task timer does not exist.");
            _items[entry.Id] = entry;
        }
        return Task.CompletedTask;
    }

    public Task<CrmTaskTimerEntry?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _items.GetValueOrDefault(id);
            return Task.FromResult(item?.TenantId == tenantId ? item : null);
        }
    }

    public Task<IReadOnlyList<CrmTaskTimerEntry>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<CrmTaskTimerEntry>>(
                _items.Values.Where(x => x.TenantId == tenantId)
                    .OrderByDescending(x => x.StartedAtUtc)
                    .ToArray());
    }
}

public sealed class PostgresCrmTaskTimerStore : ICrmTaskTimerStore
{
    private readonly CrmPostgresDatabase _db;
    public PostgresCrmTaskTimerStore(CrmPostgresDatabase db) => _db = db;

    public async Task AddAsync(CrmTaskTimerEntry entry, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
INSERT INTO businessos_crm.task_timer_entries(
 id,tenant_id,task_id,user_id,started_at_utc,stopped_at_utc,note,created_at_utc,updated_at_utc)
VALUES(
 @id,@tenant,@task,@user,@started,@stopped,@note,@created,@updated);
""");
        AddParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(CrmTaskTimerEntry entry, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
UPDATE businessos_crm.task_timer_entries SET
 stopped_at_utc=@stopped,note=@note,updated_at_utc=@updated
WHERE tenant_id=@tenant AND id=@id;
""");
        AddParameters(command, entry);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Task timer does not exist.");
    }

    public async Task<CrmTaskTimerEntry?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(
            SelectSql + " WHERE tenant_id=@tenant AND id=@id LIMIT 1");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<CrmTaskTimerEntry>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(
            SelectSql + " WHERE tenant_id=@tenant ORDER BY started_at_utc DESC");
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CrmTaskTimerEntry>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    private const string SelectSql = """
SELECT id,tenant_id,task_id,user_id,started_at_utc,stopped_at_utc,note,created_at_utc,updated_at_utc
FROM businessos_crm.task_timer_entries
""";

    private static void AddParameters(NpgsqlCommand command, CrmTaskTimerEntry entry)
    {
        command.Parameters.AddWithValue("id", entry.Id);
        command.Parameters.AddWithValue("tenant", entry.TenantId);
        command.Parameters.AddWithValue("task", entry.TaskId);
        command.Parameters.AddWithValue("user", entry.UserId);
        command.Parameters.AddWithValue("started", entry.StartedAtUtc);
        command.Parameters.Add("stopped", NpgsqlDbType.TimestampTz).Value = entry.StoppedAtUtc ?? (object)DBNull.Value;
        command.Parameters.Add("note", NpgsqlDbType.Text).Value = entry.Note ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("created", entry.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", entry.UpdatedAtUtc);
    }

    private static CrmTaskTimerEntry Read(NpgsqlDataReader reader) =>
        CrmTaskTimerEntry.Restore(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetFieldValue<DateTimeOffset>(8));
}
