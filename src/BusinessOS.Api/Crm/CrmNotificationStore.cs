using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public sealed record CrmPersistentNotification(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string SourceKey,
    string Type,
    string Title,
    string Detail,
    Guid? LeadId,
    Guid? RecordId,
    DateTimeOffset? DueAtUtc,
    string Severity,
    bool IsRead,
    bool Active,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public interface ICrmNotificationStore
{
    Task<IReadOnlyList<CrmPersistentNotification>> ListAsync(Guid tenantId, Guid userId, bool includeRead, CancellationToken ct = default);
    Task UpsertAsync(CrmPersistentNotification notification, CancellationToken ct = default);
    Task DeactivateMissingAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> activeSourceKeys, CancellationToken ct = default);
    Task<bool> MarkReadAsync(Guid tenantId, Guid userId, Guid notificationId, bool isRead, CancellationToken ct = default);
    Task<int> MarkAllReadAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}

public sealed class InMemoryCrmNotificationStore : ICrmNotificationStore
{
    private readonly Dictionary<Guid, CrmPersistentNotification> _items = [];
    private readonly object _gate = new();

    public Task<IReadOnlyList<CrmPersistentNotification>> ListAsync(Guid tenantId, Guid userId, bool includeRead, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult<IReadOnlyList<CrmPersistentNotification>>(_items.Values
            .Where(x => x.TenantId == tenantId && x.UserId == userId && x.Active && (includeRead || !x.IsRead))
            .OrderBy(x => x.IsRead).ThenBy(x => x.DueAtUtc ?? DateTimeOffset.MaxValue).ThenByDescending(x => x.UpdatedAtUtc).ToArray());
    }

    public Task UpsertAsync(CrmPersistentNotification x, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var existing = _items.Values.FirstOrDefault(n => n.TenantId == x.TenantId && n.UserId == x.UserId && n.SourceKey == x.SourceKey);
            if (existing is null) _items[x.Id] = x;
            else _items[existing.Id] = x with { Id = existing.Id, IsRead = existing.IsRead, CreatedAtUtc = existing.CreatedAtUtc };
        }
        return Task.CompletedTask;
    }

    public Task DeactivateMissingAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> activeSourceKeys, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var keys = activeSourceKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
            foreach (var item in _items.Values.Where(x => x.TenantId == tenantId && x.UserId == userId && x.Active && !keys.Contains(x.SourceKey)).ToArray())
                _items[item.Id] = item with { Active = false, UpdatedAtUtc = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }

    public Task<bool> MarkReadAsync(Guid tenantId, Guid userId, Guid notificationId, bool isRead, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_items.TryGetValue(notificationId, out var item) || item.TenantId != tenantId || item.UserId != userId) return Task.FromResult(false);
            _items[notificationId] = item with { IsRead = isRead, UpdatedAtUtc = DateTimeOffset.UtcNow };
            return Task.FromResult(true);
        }
    }

    public Task<int> MarkAllReadAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var count = 0;
        lock (_gate)
            foreach (var item in _items.Values.Where(x => x.TenantId == tenantId && x.UserId == userId && x.Active && !x.IsRead).ToArray())
            { _items[item.Id] = item with { IsRead = true, UpdatedAtUtc = DateTimeOffset.UtcNow }; count++; }
        return Task.FromResult(count);
    }
}

public sealed class PostgresCrmNotificationStore : ICrmNotificationStore
{
    private readonly CrmPostgresDatabase _db;
    public PostgresCrmNotificationStore(CrmPostgresDatabase db) => _db = db;

    public async Task<IReadOnlyList<CrmPersistentNotification>> ListAsync(Guid tenantId, Guid userId, bool includeRead, CancellationToken ct = default)
    {
        await _db.EnsureReadyAsync(ct);
        var sql = Select + " WHERE tenant_id=@tenant AND user_id=@user AND active=true" + (includeRead ? "" : " AND is_read=false") + " ORDER BY is_read,due_at_utc NULLS LAST,updated_at_utc DESC";
        await using var c = _db.DataSource.CreateCommand(sql); c.Parameters.AddWithValue("tenant", tenantId); c.Parameters.AddWithValue("user", userId);
        await using var r = await c.ExecuteReaderAsync(ct); var items = new List<CrmPersistentNotification>(); while (await r.ReadAsync(ct)) items.Add(Read(r)); return items;
    }

    public async Task UpsertAsync(CrmPersistentNotification x, CancellationToken ct = default)
    {
        await _db.EnsureReadyAsync(ct);
        const string sql = """
INSERT INTO businessos_crm.notifications(id,tenant_id,user_id,source_key,type,title,detail,lead_id,record_id,due_at_utc,severity,is_read,active,created_at_utc,updated_at_utc)
VALUES(@id,@tenant,@user,@source,@type,@title,@detail,@lead,@record,@due,@severity,@read,@active,@created,@updated)
ON CONFLICT(tenant_id,user_id,source_key) DO UPDATE SET
 type=excluded.type,title=excluded.title,detail=excluded.detail,lead_id=excluded.lead_id,record_id=excluded.record_id,
 due_at_utc=excluded.due_at_utc,severity=excluded.severity,active=true,updated_at_utc=excluded.updated_at_utc;
""";
        await using var c = _db.DataSource.CreateCommand(sql); Add(c, x); await c.ExecuteNonQueryAsync(ct);
    }

    public async Task DeactivateMissingAsync(Guid tenantId, Guid userId, IReadOnlyCollection<string> activeSourceKeys, CancellationToken ct = default)
    {
        await _db.EnsureReadyAsync(ct);
        var keys = activeSourceKeys.ToArray();
        var sql = keys.Length == 0
            ? "UPDATE businessos_crm.notifications SET active=false,updated_at_utc=now() WHERE tenant_id=@tenant AND user_id=@user AND active=true"
            : "UPDATE businessos_crm.notifications SET active=false,updated_at_utc=now() WHERE tenant_id=@tenant AND user_id=@user AND active=true AND NOT(source_key=ANY(@keys))";
        await using var c = _db.DataSource.CreateCommand(sql); c.Parameters.AddWithValue("tenant", tenantId); c.Parameters.AddWithValue("user", userId); if (keys.Length > 0) c.Parameters.AddWithValue("keys", keys); await c.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> MarkReadAsync(Guid tenantId, Guid userId, Guid notificationId, bool isRead, CancellationToken ct = default)
    {
        await _db.EnsureReadyAsync(ct); await using var c = _db.DataSource.CreateCommand("UPDATE businessos_crm.notifications SET is_read=@read,updated_at_utc=now() WHERE tenant_id=@tenant AND user_id=@user AND id=@id");
        c.Parameters.AddWithValue("read", isRead); c.Parameters.AddWithValue("tenant", tenantId); c.Parameters.AddWithValue("user", userId); c.Parameters.AddWithValue("id", notificationId); return await c.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<int> MarkAllReadAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        await _db.EnsureReadyAsync(ct); await using var c = _db.DataSource.CreateCommand("UPDATE businessos_crm.notifications SET is_read=true,updated_at_utc=now() WHERE tenant_id=@tenant AND user_id=@user AND active=true AND is_read=false"); c.Parameters.AddWithValue("tenant", tenantId); c.Parameters.AddWithValue("user", userId); return await c.ExecuteNonQueryAsync(ct);
    }

    private const string Select = "SELECT id,tenant_id,user_id,source_key,type,title,detail,lead_id,record_id,due_at_utc,severity,is_read,active,created_at_utc,updated_at_utc FROM businessos_crm.notifications";
    private static CrmPersistentNotification Read(NpgsqlDataReader r) => new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),GuidOrNull(r,7),GuidOrNull(r,8),DateOrNull(r,9),r.GetString(10),r.GetBoolean(11),r.GetBoolean(12),r.GetFieldValue<DateTimeOffset>(13),r.GetFieldValue<DateTimeOffset>(14));
    private static void Add(NpgsqlCommand c, CrmPersistentNotification x){c.Parameters.AddWithValue("id",x.Id);c.Parameters.AddWithValue("tenant",x.TenantId);c.Parameters.AddWithValue("user",x.UserId);c.Parameters.AddWithValue("source",x.SourceKey);c.Parameters.AddWithValue("type",x.Type);c.Parameters.AddWithValue("title",x.Title);c.Parameters.AddWithValue("detail",x.Detail);Nullable(c,"lead",NpgsqlDbType.Uuid,x.LeadId);Nullable(c,"record",NpgsqlDbType.Uuid,x.RecordId);Nullable(c,"due",NpgsqlDbType.TimestampTz,x.DueAtUtc);c.Parameters.AddWithValue("severity",x.Severity);c.Parameters.AddWithValue("read",x.IsRead);c.Parameters.AddWithValue("active",x.Active);c.Parameters.AddWithValue("created",x.CreatedAtUtc);c.Parameters.AddWithValue("updated",x.UpdatedAtUtc);}
    private static Guid? GuidOrNull(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetGuid(i); private static DateTimeOffset? DateOrNull(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetFieldValue<DateTimeOffset>(i); private static void Nullable(NpgsqlCommand c,string n,NpgsqlDbType t,object? v)=>c.Parameters.Add(n,t).Value=v??DBNull.Value;
}
