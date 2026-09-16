using BusinessOS.Crm;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public sealed class PostgresCrmTeamRepository : ICrmTeamRepository
{
    private readonly CrmPostgresDatabase _db;
    public PostgresCrmTeamRepository(CrmPostgresDatabase db)=>_db=db;

    public async Task AddAsync(CrmTeamMember x,CancellationToken ct=default)
    {
        await _db.EnsureReadyAsync(ct);
        const string sql="INSERT INTO businessos_crm.team_members(id,tenant_id,display_name,email,mobile_number,role,active,created_at_utc) VALUES(@id,@tenant,@name,@email,@mobile,@role,@active,@created)";
        await using var c=_db.DataSource.CreateCommand(sql); Add(c,x);
        try{await c.ExecuteNonQueryAsync(ct);}catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.UniqueViolation){throw new InvalidOperationException("Team member id or email already exists.",ex);}
    }
    public async Task SaveAsync(CrmTeamMember x,CancellationToken ct=default)
    {
        await _db.EnsureReadyAsync(ct);
        const string sql="UPDATE businessos_crm.team_members SET display_name=@name,email=@email,mobile_number=@mobile,role=@role,active=@active WHERE tenant_id=@tenant AND id=@id";
        await using var c=_db.DataSource.CreateCommand(sql); Add(c,x); if(await c.ExecuteNonQueryAsync(ct)==0)throw new InvalidOperationException("Team member does not exist.");
    }
    public async Task<CrmTeamMember?> GetAsync(Guid tenantId,Guid id,CancellationToken ct=default)
    {
        await _db.EnsureReadyAsync(ct); const string sql=Select+" WHERE tenant_id=@tenant AND id=@id LIMIT 1"; await using var c=_db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("tenant",tenantId);c.Parameters.AddWithValue("id",id);await using var r=await c.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?Read(r):null;
    }
    public async Task<IReadOnlyList<CrmTeamMember>> ListAsync(Guid tenantId,CancellationToken ct=default)
    {
        await _db.EnsureReadyAsync(ct); const string sql=Select+" WHERE tenant_id=@tenant ORDER BY display_name"; await using var c=_db.DataSource.CreateCommand(sql);c.Parameters.AddWithValue("tenant",tenantId);await using var r=await c.ExecuteReaderAsync(ct);var items=new List<CrmTeamMember>();while(await r.ReadAsync(ct))items.Add(Read(r));return items;
    }
    private const string Select="SELECT id,tenant_id,display_name,email,mobile_number,role,active,created_at_utc FROM businessos_crm.team_members";
    private static CrmTeamMember Read(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),(CrmRoleCode)r.GetInt32(5),r.GetBoolean(6),r.GetFieldValue<DateTimeOffset>(7));
    private static void Add(NpgsqlCommand c,CrmTeamMember x){c.Parameters.AddWithValue("id",x.Id);c.Parameters.AddWithValue("tenant",x.TenantId);c.Parameters.AddWithValue("name",x.DisplayName);c.Parameters.AddWithValue("email",x.Email);c.Parameters.Add("mobile",NpgsqlDbType.Text).Value=(object?)x.MobileNumber??DBNull.Value;c.Parameters.AddWithValue("role",(int)x.Role);c.Parameters.AddWithValue("active",x.Active);c.Parameters.AddWithValue("created",x.CreatedAtUtc);}
}
