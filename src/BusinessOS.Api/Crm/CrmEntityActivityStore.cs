using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public sealed record CrmEntityActivity(
    Guid Id,
    Guid TenantId,
    string EntityType,
    Guid EntityId,
    Guid? ContactId,
    string Channel,
    string Summary,
    string? Details,
    Guid? ActorUserId,
    DateTimeOffset OccurredAtUtc);

public interface ICrmEntityActivityStore
{
    Task AddAsync(CrmEntityActivity activity, CancellationToken ct = default);
    Task<IReadOnlyList<CrmEntityActivity>> ListAsync(Guid tenantId, string entityType, Guid entityId, CancellationToken ct = default);
    Task<IReadOnlyList<CrmEntityActivity>> ListContactAsync(Guid tenantId, Guid contactId, CancellationToken ct = default);
}

public sealed class InMemoryCrmEntityActivityStore : ICrmEntityActivityStore
{
    private readonly Dictionary<Guid, CrmEntityActivity> _items = [];
    private readonly object _gate = new();
    public Task AddAsync(CrmEntityActivity x, CancellationToken ct = default){ct.ThrowIfCancellationRequested();lock(_gate){if(_items.ContainsKey(x.Id))throw new InvalidOperationException("CRM activity id already exists.");_items[x.Id]=x;}return Task.CompletedTask;}
    public Task<IReadOnlyList<CrmEntityActivity>> ListAsync(Guid tenantId,string entityType,Guid entityId,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate)return Task.FromResult<IReadOnlyList<CrmEntityActivity>>(_items.Values.Where(x=>x.TenantId==tenantId&&x.EntityId==entityId&&x.EntityType.Equals(entityType,StringComparison.OrdinalIgnoreCase)).OrderByDescending(x=>x.OccurredAtUtc).ToArray());}
    public Task<IReadOnlyList<CrmEntityActivity>> ListContactAsync(Guid tenantId,Guid contactId,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate)return Task.FromResult<IReadOnlyList<CrmEntityActivity>>(_items.Values.Where(x=>x.TenantId==tenantId&&x.ContactId==contactId).OrderByDescending(x=>x.OccurredAtUtc).ToArray());}
}

public sealed class PostgresCrmEntityActivityStore : ICrmEntityActivityStore
{
    private readonly CrmPostgresDatabase _db;
    public PostgresCrmEntityActivityStore(CrmPostgresDatabase db)=>_db=db;

    public async Task AddAsync(CrmEntityActivity x,CancellationToken ct=default)
    {
        await _db.EnsureReadyAsync(ct);
        const string sql="INSERT INTO businessos_crm.entity_activities(id,tenant_id,entity_type,entity_id,contact_id,channel,summary,details,actor_user_id,occurred_at_utc) VALUES(@id,@tenant,@type,@entity,@contact,@channel,@summary,@details,@actor,@occurred)";
        await using var c=_db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("id",x.Id);c.Parameters.AddWithValue("tenant",x.TenantId);c.Parameters.AddWithValue("type",x.EntityType);c.Parameters.AddWithValue("entity",x.EntityId);Nullable(c,"contact",NpgsqlDbType.Uuid,x.ContactId);c.Parameters.AddWithValue("channel",x.Channel);c.Parameters.AddWithValue("summary",x.Summary);Nullable(c,"details",NpgsqlDbType.Text,x.Details);Nullable(c,"actor",NpgsqlDbType.Uuid,x.ActorUserId);c.Parameters.AddWithValue("occurred",x.OccurredAtUtc);await c.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CrmEntityActivity>> ListAsync(Guid tenantId,string entityType,Guid entityId,CancellationToken ct=default)
    {
        await _db.EnsureReadyAsync(ct);const string sql=Select+" WHERE tenant_id=@tenant AND lower(entity_type)=lower(@type) AND entity_id=@entity ORDER BY occurred_at_utc DESC";await using var c=_db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("tenant",tenantId);c.Parameters.AddWithValue("type",entityType);c.Parameters.AddWithValue("entity",entityId);return await ReadAll(c,ct);
    }

    public async Task<IReadOnlyList<CrmEntityActivity>> ListContactAsync(Guid tenantId,Guid contactId,CancellationToken ct=default)
    {
        await _db.EnsureReadyAsync(ct);const string sql=Select+" WHERE tenant_id=@tenant AND contact_id=@contact ORDER BY occurred_at_utc DESC";await using var c=_db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("tenant",tenantId);c.Parameters.AddWithValue("contact",contactId);return await ReadAll(c,ct);
    }

    private static async Task<IReadOnlyList<CrmEntityActivity>> ReadAll(NpgsqlCommand c,CancellationToken ct){await using var r=await c.ExecuteReaderAsync(ct);var items=new List<CrmEntityActivity>();while(await r.ReadAsync(ct))items.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetGuid(3),GuidOrNull(r,4),r.GetString(5),r.GetString(6),Str(r,7),GuidOrNull(r,8),r.GetFieldValue<DateTimeOffset>(9)));return items;}
    private const string Select="SELECT id,tenant_id,entity_type,entity_id,contact_id,channel,summary,details,actor_user_id,occurred_at_utc FROM businessos_crm.entity_activities";
    private static Guid? GuidOrNull(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetGuid(i);private static string? Str(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);private static void Nullable(NpgsqlCommand c,string n,NpgsqlDbType t,object? v)=>c.Parameters.Add(n,t).Value=v??DBNull.Value;
}
