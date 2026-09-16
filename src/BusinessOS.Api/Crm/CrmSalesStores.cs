using System.Text.Json;
using BusinessOS.Customers;
using BusinessOS.Sales;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public interface ICrmAccountStore
{
    Task AddAsync(Organisation account, CancellationToken cancellationToken = default);
    Task SaveAsync(Organisation account, CancellationToken cancellationToken = default);
    Task<Organisation?> GetAsync(Guid tenantId, Guid accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Organisation>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public interface ICrmOpportunityStore
{
    Task AddAsync(Opportunity opportunity, CancellationToken cancellationToken = default);
    Task SaveAsync(Opportunity opportunity, CancellationToken cancellationToken = default);
    Task<Opportunity?> GetAsync(Guid tenantId, Guid opportunityId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Opportunity>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryCrmAccountStore : ICrmAccountStore
{
    private readonly Dictionary<Guid,Organisation> _items=[]; private readonly object _gate=new();
    public Task AddAsync(Organisation x,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate){if(_items.ContainsKey(x.Id))throw new InvalidOperationException("Account id already exists.");_items.Add(x.Id,x);}return Task.CompletedTask;}
    public Task SaveAsync(Organisation x,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate){if(!_items.ContainsKey(x.Id))throw new InvalidOperationException("Account does not exist.");_items[x.Id]=x;}return Task.CompletedTask;}
    public Task<Organisation?> GetAsync(Guid tenantId,Guid id,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate){var x=_items.GetValueOrDefault(id);return Task.FromResult(x?.TenantId==tenantId?x:null);}}
    public Task<IReadOnlyList<Organisation>> ListAsync(Guid tenantId,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate)return Task.FromResult<IReadOnlyList<Organisation>>(_items.Values.Where(x=>x.TenantId==tenantId).ToArray());}
}

public sealed class InMemoryCrmOpportunityStore : ICrmOpportunityStore
{
    private readonly Dictionary<Guid,Opportunity> _items=[]; private readonly object _gate=new();
    public Task AddAsync(Opportunity x,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate){if(_items.ContainsKey(x.Id))throw new InvalidOperationException("Opportunity id already exists.");_items.Add(x.Id,x);}return Task.CompletedTask;}
    public Task SaveAsync(Opportunity x,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate){if(!_items.ContainsKey(x.Id))throw new InvalidOperationException("Opportunity does not exist.");_items[x.Id]=x;}return Task.CompletedTask;}
    public Task<Opportunity?> GetAsync(Guid tenantId,Guid id,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate){var x=_items.GetValueOrDefault(id);return Task.FromResult(x?.TenantId==tenantId?x:null);}}
    public Task<IReadOnlyList<Opportunity>> ListAsync(Guid tenantId,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate)return Task.FromResult<IReadOnlyList<Opportunity>>(_items.Values.Where(x=>x.TenantId==tenantId).ToArray());}
}

public sealed class PostgresCrmAccountStore : ICrmAccountStore
{
    private readonly CrmPostgresDatabase _db; private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
    public PostgresCrmAccountStore(CrmPostgresDatabase db)=>_db=db;
    public async Task AddAsync(Organisation x,CancellationToken ct=default){await _db.EnsureReadyAsync(ct);const string sql="INSERT INTO businessos_crm.accounts(id,tenant_id,name,legal_name,gstin,display_code,status,roles,contacts,addresses) VALUES(@id,@tenant,@name,@legal,@gstin,@code,@status,CAST(@roles AS jsonb),CAST(@contacts AS jsonb),CAST(@addresses AS jsonb))";await using var c=_db.DataSource.CreateCommand(sql);Add(c,x);await c.ExecuteNonQueryAsync(ct);}
    public async Task SaveAsync(Organisation x,CancellationToken ct=default){await _db.EnsureReadyAsync(ct);const string sql="UPDATE businessos_crm.accounts SET name=@name,legal_name=@legal,gstin=@gstin,display_code=@code,status=@status,roles=CAST(@roles AS jsonb),contacts=CAST(@contacts AS jsonb),addresses=CAST(@addresses AS jsonb) WHERE tenant_id=@tenant AND id=@id";await using var c=_db.DataSource.CreateCommand(sql);Add(c,x);if(await c.ExecuteNonQueryAsync(ct)==0)throw new InvalidOperationException("Account does not exist.");}
    public async Task<Organisation?> GetAsync(Guid tenantId,Guid id,CancellationToken ct=default){await _db.EnsureReadyAsync(ct);const string sql=Select+" WHERE tenant_id=@tenant AND id=@id LIMIT 1";await using var c=_db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("tenant",tenantId);c.Parameters.AddWithValue("id",id);await using var r=await c.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?Read(r):null;}
    public async Task<IReadOnlyList<Organisation>> ListAsync(Guid tenantId,CancellationToken ct=default){await _db.EnsureReadyAsync(ct);const string sql=Select+" WHERE tenant_id=@tenant ORDER BY name";await using var c=_db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("tenant",tenantId);await using var r=await c.ExecuteReaderAsync(ct);var items=new List<Organisation>();while(await r.ReadAsync(ct))items.Add(Read(r));return items;}
    private const string Select="SELECT id,tenant_id,name,legal_name,gstin,display_code,status,roles::text,contacts::text,addresses::text FROM businessos_crm.accounts";
    private static void Add(NpgsqlCommand c,Organisation x){c.Parameters.AddWithValue("id",x.Id);c.Parameters.AddWithValue("tenant",x.TenantId);c.Parameters.AddWithValue("name",x.Name);Nullable(c,"legal",NpgsqlDbType.Text,x.LegalName);Nullable(c,"gstin",NpgsqlDbType.Text,x.Gstin);Nullable(c,"code",NpgsqlDbType.Text,x.DisplayCode);c.Parameters.AddWithValue("status",(int)x.Status);c.Parameters.AddWithValue("roles",JsonSerializer.Serialize(x.Roles,JsonOptions));c.Parameters.AddWithValue("contacts",JsonSerializer.Serialize(x.Contacts,JsonOptions));c.Parameters.AddWithValue("addresses",JsonSerializer.Serialize(x.Addresses,JsonOptions));}
    private static Organisation Read(NpgsqlDataReader r){var x=new Organisation(r.GetGuid(0),r.GetGuid(1),r.GetString(2),Str(r,3),Str(r,4),Str(r,5),(OrganisationStatus)r.GetInt32(6));foreach(var role in JsonSerializer.Deserialize<OrganisationRole[]>(r.GetString(7),JsonOptions)??[])x.AddRole(role);foreach(var contact in JsonSerializer.Deserialize<ContactPerson[]>(r.GetString(8),JsonOptions)??[])x.AddContact(contact);foreach(var address in JsonSerializer.Deserialize<OrganisationAddress[]>(r.GetString(9),JsonOptions)??[])x.AddAddress(address);return x;}
    private static string? Str(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i); private static void Nullable(NpgsqlCommand c,string n,NpgsqlDbType type,object? v)=>c.Parameters.Add(n,type).Value=v??DBNull.Value;
}

public sealed class PostgresCrmOpportunityStore : ICrmOpportunityStore
{
    private readonly CrmPostgresDatabase _db; public PostgresCrmOpportunityStore(CrmPostgresDatabase db)=>_db=db;
    public async Task AddAsync(Opportunity x,CancellationToken ct=default){await _db.EnsureReadyAsync(ct);const string sql="INSERT INTO businessos_crm.opportunities(id,tenant_id,organisation_id,originating_lead_id,owner_user_id,title,stage,estimated_value,currency_code,probability_percent,expected_close_date,loss_reason) VALUES(@id,@tenant,@org,@lead,@owner,@title,@stage,@value,@currency,@probability,@close,@loss)";await using var c=_db.DataSource.CreateCommand(sql);Add(c,x);await c.ExecuteNonQueryAsync(ct);}
    public async Task SaveAsync(Opportunity x,CancellationToken ct=default){await _db.EnsureReadyAsync(ct);const string sql="UPDATE businessos_crm.opportunities SET owner_user_id=@owner,title=@title,stage=@stage,estimated_value=@value,currency_code=@currency,probability_percent=@probability,expected_close_date=@close,loss_reason=@loss WHERE tenant_id=@tenant AND id=@id";await using var c=_db.DataSource.CreateCommand(sql);Add(c,x);if(await c.ExecuteNonQueryAsync(ct)==0)throw new InvalidOperationException("Opportunity does not exist.");}
    public async Task<Opportunity?> GetAsync(Guid tenantId,Guid id,CancellationToken ct=default){await _db.EnsureReadyAsync(ct);const string sql=Select+" WHERE tenant_id=@tenant AND id=@id LIMIT 1";await using var c=_db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("tenant",tenantId);c.Parameters.AddWithValue("id",id);await using var r=await c.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?Read(r):null;}
    public async Task<IReadOnlyList<Opportunity>> ListAsync(Guid tenantId,CancellationToken ct=default){await _db.EnsureReadyAsync(ct);const string sql=Select+" WHERE tenant_id=@tenant ORDER BY estimated_value DESC";await using var c=_db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("tenant",tenantId);await using var r=await c.ExecuteReaderAsync(ct);var items=new List<Opportunity>();while(await r.ReadAsync(ct))items.Add(Read(r));return items;}
    private const string Select="SELECT id,tenant_id,organisation_id,originating_lead_id,owner_user_id,title,stage,estimated_value,currency_code,probability_percent,expected_close_date,loss_reason FROM businessos_crm.opportunities";
    private static void Add(NpgsqlCommand c,Opportunity x){c.Parameters.AddWithValue("id",x.Id);c.Parameters.AddWithValue("tenant",x.TenantId);c.Parameters.AddWithValue("org",x.OrganisationId);Nullable(c,"lead",NpgsqlDbType.Uuid,x.OriginatingLeadId);Nullable(c,"owner",NpgsqlDbType.Uuid,x.OwnerUserId);c.Parameters.AddWithValue("title",x.Title);c.Parameters.AddWithValue("stage",(int)x.Stage);c.Parameters.AddWithValue("value",x.Forecast.EstimatedValue);c.Parameters.AddWithValue("currency",x.Forecast.CurrencyCode);c.Parameters.AddWithValue("probability",x.Forecast.ProbabilityPercent);Nullable(c,"close",NpgsqlDbType.Date,x.Forecast.ExpectedCloseDate);Nullable(c,"loss",NpgsqlDbType.Text,x.LossReason);}
    private static Opportunity Read(NpgsqlDataReader r){var forecast=new OpportunityForecast(r.GetDecimal(7),r.GetString(8),r.GetInt32(9),r.IsDBNull(10)?null:r.GetFieldValue<DateOnly>(10));var x=new Opportunity(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(5),forecast,r.IsDBNull(3)?null:r.GetGuid(3),r.IsDBNull(4)?null:r.GetGuid(4));var stage=(OpportunityStage)r.GetInt32(6);if(stage==OpportunityStage.Won)x.MarkWon();else if(stage==OpportunityStage.Lost)x.MarkLost(r.IsDBNull(11)?"Lost":r.GetString(11));else if(stage!=OpportunityStage.Discovery)x.MoveTo(stage);return x;}
    private static void Nullable(NpgsqlCommand c,string n,NpgsqlDbType type,object? v)=>c.Parameters.Add(n,type).Value=v??DBNull.Value;
}
