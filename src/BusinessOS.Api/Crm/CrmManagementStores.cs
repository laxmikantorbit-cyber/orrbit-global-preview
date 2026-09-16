using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public sealed record CrmSavedView(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string Module,
    string Name,
    string FiltersJson,
    bool IsDefault,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CrmMasterItem(
    Guid Id,
    Guid TenantId,
    string Category,
    string Code,
    string Name,
    bool Active,
    int SortOrder,
    DateTimeOffset UpdatedAtUtc);

public sealed record CrmAuditEntry(
    Guid Id,
    Guid TenantId,
    Guid? ActorUserId,
    string Action,
    string EntityType,
    string? EntityId,
    string? Detail,
    DateTimeOffset CreatedAtUtc);

public interface ICrmManagementStore
{
    Task<IReadOnlyList<CrmSavedView>> ListViewsAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task SaveViewAsync(CrmSavedView view, CancellationToken ct = default);
    Task RemoveViewAsync(Guid tenantId, Guid userId, Guid viewId, CancellationToken ct = default);
    Task<IReadOnlyList<CrmMasterItem>> ListMastersAsync(Guid tenantId, string? category = null, CancellationToken ct = default);
    Task SaveMasterAsync(CrmMasterItem item, CancellationToken ct = default);
    Task<IReadOnlyList<CrmAuditEntry>> ListAuditAsync(Guid tenantId, int take, CancellationToken ct = default);
    Task AddAuditAsync(CrmAuditEntry entry, CancellationToken ct = default);
}

public sealed class InMemoryCrmManagementStore : ICrmManagementStore
{
    private readonly Dictionary<Guid, CrmSavedView> _views = [];
    private readonly Dictionary<Guid, CrmMasterItem> _masters = [];
    private readonly List<CrmAuditEntry> _audit = [];
    private readonly object _gate = new();

    public Task<IReadOnlyList<CrmSavedView>> ListViewsAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult<IReadOnlyList<CrmSavedView>>(_views.Values.Where(x => x.TenantId == tenantId && x.UserId == userId).OrderBy(x => x.Name).ToArray());
    }

    public Task SaveViewAsync(CrmSavedView view, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (view.IsDefault)
                foreach (var key in _views.Values.Where(x => x.TenantId == view.TenantId && x.UserId == view.UserId && x.Module == view.Module && x.IsDefault && x.Id != view.Id).Select(x => x.Id).ToArray())
                    _views[key] = _views[key] with { IsDefault = false, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _views[view.Id] = view;
        }
        return Task.CompletedTask;
    }

    public Task RemoveViewAsync(Guid tenantId, Guid userId, Guid viewId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate) if (_views.TryGetValue(viewId, out var item) && item.TenantId == tenantId && item.UserId == userId) _views.Remove(viewId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CrmMasterItem>> ListMastersAsync(Guid tenantId, string? category = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult<IReadOnlyList<CrmMasterItem>>(_masters.Values.Where(x => x.TenantId == tenantId && (category == null || x.Category.Equals(category, StringComparison.OrdinalIgnoreCase))).OrderBy(x => x.Category).ThenBy(x => x.SortOrder).ThenBy(x => x.Name).ToArray());
    }

    public Task SaveMasterAsync(CrmMasterItem item, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); lock (_gate) _masters[item.Id] = item; return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CrmAuditEntry>> ListAuditAsync(Guid tenantId, int take, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); lock (_gate) return Task.FromResult<IReadOnlyList<CrmAuditEntry>>(_audit.Where(x => x.TenantId == tenantId).OrderByDescending(x => x.CreatedAtUtc).Take(Math.Clamp(take, 1, 500)).ToArray());
    }

    public Task AddAuditAsync(CrmAuditEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); lock (_gate) _audit.Add(entry); return Task.CompletedTask;
    }
}

public sealed class PostgresCrmManagementStore : ICrmManagementStore
{
    private readonly CrmPostgresDatabase _db;
    public PostgresCrmManagementStore(CrmPostgresDatabase db) => _db = db;

    public async Task<IReadOnlyList<CrmSavedView>> ListViewsAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        await _db.EnsureReadyAsync(ct);
        const string sql = "SELECT id,tenant_id,user_id,module,name,filters_json,is_default,created_at_utc,updated_at_utc FROM businessos_crm.saved_views WHERE tenant_id=@tenant AND user_id=@user ORDER BY name";
        await using var c = _db.DataSource.CreateCommand(sql); c.Parameters.AddWithValue("tenant", tenantId); c.Parameters.AddWithValue("user", userId);
        await using var r = await c.ExecuteReaderAsync(ct); var items = new List<CrmSavedView>();
        while (await r.ReadAsync(ct)) items.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetBoolean(6),r.GetFieldValue<DateTimeOffset>(7),r.GetFieldValue<DateTimeOffset>(8)));
        return items;
    }

    public async Task SaveViewAsync(CrmSavedView view, CancellationToken ct = default)
    {
        await _db.EnsureReadyAsync(ct);
        await using var connection = await _db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        if (view.IsDefault)
        {
            await using var clear = new NpgsqlCommand("UPDATE businessos_crm.saved_views SET is_default=false,updated_at_utc=now() WHERE tenant_id=@tenant AND user_id=@user AND module=@module AND id<>@id", connection, tx);
            clear.Parameters.AddWithValue("tenant", view.TenantId); clear.Parameters.AddWithValue("user", view.UserId); clear.Parameters.AddWithValue("module", view.Module); clear.Parameters.AddWithValue("id", view.Id);
            await clear.ExecuteNonQueryAsync(ct);
        }
        const string sql = "INSERT INTO businessos_crm.saved_views(id,tenant_id,user_id,module,name,filters_json,is_default,created_at_utc,updated_at_utc) VALUES(@id,@tenant,@user,@module,@name,@filters,@default,@created,@updated) ON CONFLICT(id) DO UPDATE SET module=excluded.module,name=excluded.name,filters_json=excluded.filters_json,is_default=excluded.is_default,updated_at_utc=excluded.updated_at_utc WHERE businessos_crm.saved_views.tenant_id=excluded.tenant_id AND businessos_crm.saved_views.user_id=excluded.user_id";
        await using var c = new NpgsqlCommand(sql, connection, tx); AddView(c, view); await c.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task RemoveViewAsync(Guid tenantId, Guid userId, Guid viewId, CancellationToken ct = default)
    {
        await _db.EnsureReadyAsync(ct); await using var c = _db.DataSource.CreateCommand("DELETE FROM businessos_crm.saved_views WHERE tenant_id=@tenant AND user_id=@user AND id=@id"); c.Parameters.AddWithValue("tenant",tenantId);c.Parameters.AddWithValue("user",userId);c.Parameters.AddWithValue("id",viewId);await c.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CrmMasterItem>> ListMastersAsync(Guid tenantId, string? category = null, CancellationToken ct = default)
    {
        await _db.EnsureReadyAsync(ct);
        var sql = "SELECT id,tenant_id,category,code,name,active,sort_order,updated_at_utc FROM businessos_crm.master_items WHERE tenant_id=@tenant" + (string.IsNullOrWhiteSpace(category) ? "" : " AND lower(category)=lower(@category)") + " ORDER BY category,sort_order,name";
        await using var c = _db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("tenant",tenantId);if(!string.IsNullOrWhiteSpace(category))c.Parameters.AddWithValue("category",category.Trim());await using var r=await c.ExecuteReaderAsync(ct);var items=new List<CrmMasterItem>();while(await r.ReadAsync(ct))items.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetBoolean(5),r.GetInt32(6),r.GetFieldValue<DateTimeOffset>(7)));return items;
    }

    public async Task SaveMasterAsync(CrmMasterItem item, CancellationToken ct = default)
    {
        await _db.EnsureReadyAsync(ct);const string sql="INSERT INTO businessos_crm.master_items(id,tenant_id,category,code,name,active,sort_order,updated_at_utc) VALUES(@id,@tenant,@category,@code,@name,@active,@sort,@updated) ON CONFLICT(tenant_id,category,code) DO UPDATE SET name=excluded.name,active=excluded.active,sort_order=excluded.sort_order,updated_at_utc=excluded.updated_at_utc";await using var c=_db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("id",item.Id);c.Parameters.AddWithValue("tenant",item.TenantId);c.Parameters.AddWithValue("category",item.Category);c.Parameters.AddWithValue("code",item.Code);c.Parameters.AddWithValue("name",item.Name);c.Parameters.AddWithValue("active",item.Active);c.Parameters.AddWithValue("sort",item.SortOrder);c.Parameters.AddWithValue("updated",item.UpdatedAtUtc);await c.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CrmAuditEntry>> ListAuditAsync(Guid tenantId, int take, CancellationToken ct = default)
    {
        await _db.EnsureReadyAsync(ct);const string sql="SELECT id,tenant_id,actor_user_id,action,entity_type,entity_id,detail,created_at_utc FROM businessos_crm.audit_events WHERE tenant_id=@tenant ORDER BY created_at_utc DESC LIMIT @take";await using var c=_db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("tenant",tenantId);c.Parameters.AddWithValue("take",Math.Clamp(take,1,500));await using var r=await c.ExecuteReaderAsync(ct);var items=new List<CrmAuditEntry>();while(await r.ReadAsync(ct))items.Add(new(r.GetGuid(0),r.GetGuid(1),r.IsDBNull(2)?null:r.GetGuid(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),r.GetFieldValue<DateTimeOffset>(7)));return items;
    }

    public async Task AddAuditAsync(CrmAuditEntry entry, CancellationToken ct = default)
    {
        await _db.EnsureReadyAsync(ct);const string sql="INSERT INTO businessos_crm.audit_events(id,tenant_id,actor_user_id,action,entity_type,entity_id,detail,created_at_utc) VALUES(@id,@tenant,@actor,@action,@type,@entity,@detail,@created)";await using var c=_db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("id",entry.Id);c.Parameters.AddWithValue("tenant",entry.TenantId);Nullable(c,"actor",NpgsqlDbType.Uuid,entry.ActorUserId);c.Parameters.AddWithValue("action",entry.Action);c.Parameters.AddWithValue("type",entry.EntityType);Nullable(c,"entity",NpgsqlDbType.Text,entry.EntityId);Nullable(c,"detail",NpgsqlDbType.Text,entry.Detail);c.Parameters.AddWithValue("created",entry.CreatedAtUtc);await c.ExecuteNonQueryAsync(ct);
    }

    private static void AddView(NpgsqlCommand c, CrmSavedView x){c.Parameters.AddWithValue("id",x.Id);c.Parameters.AddWithValue("tenant",x.TenantId);c.Parameters.AddWithValue("user",x.UserId);c.Parameters.AddWithValue("module",x.Module);c.Parameters.AddWithValue("name",x.Name);c.Parameters.AddWithValue("filters",x.FiltersJson);c.Parameters.AddWithValue("default",x.IsDefault);c.Parameters.AddWithValue("created",x.CreatedAtUtc);c.Parameters.AddWithValue("updated",x.UpdatedAtUtc);}
    private static void Nullable(NpgsqlCommand c,string name,NpgsqlDbType type,object? value)=>c.Parameters.Add(name,type).Value=value??DBNull.Value;
}
