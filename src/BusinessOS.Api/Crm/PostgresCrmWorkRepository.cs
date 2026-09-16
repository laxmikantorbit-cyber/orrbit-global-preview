using BusinessOS.Crm;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public sealed class PostgresCrmWorkRepository : ICrmWorkRepository
{
    private readonly CrmPostgresDatabase _database;
    public PostgresCrmWorkRepository(CrmPostgresDatabase database) => _database = database;

    public async Task AddActivityAsync(LeadActivity activity, CancellationToken cancellationToken = default)
    {
        await _database.EnsureReadyAsync(cancellationToken);
        const string sql = """
INSERT INTO businessos_crm.activities(id,tenant_id,lead_id,type,summary,details,actor_user_id,occurred_at_utc)
VALUES(@id,@tenant,@lead,@type,@summary,@details,@actor,@occurred);
""";
        await using var command = _database.DataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", activity.Id); command.Parameters.AddWithValue("tenant", activity.TenantId);
        command.Parameters.AddWithValue("lead", activity.LeadId); command.Parameters.AddWithValue("type", (int)activity.Type);
        command.Parameters.AddWithValue("summary", activity.Summary); AddNullable(command,"details",NpgsqlDbType.Text,activity.Details);
        AddNullable(command,"actor",NpgsqlDbType.Uuid,activity.ActorUserId); command.Parameters.AddWithValue("occurred",activity.OccurredAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LeadActivity>> ListActivitiesAsync(Guid tenantId, Guid leadId, CancellationToken cancellationToken = default)
    {
        await _database.EnsureReadyAsync(cancellationToken);
        const string sql = "SELECT id,tenant_id,lead_id,type,summary,details,actor_user_id,occurred_at_utc FROM businessos_crm.activities WHERE tenant_id=@tenant AND lead_id=@lead ORDER BY occurred_at_utc DESC";
        await using var command = _database.DataSource.CreateCommand(sql); command.Parameters.AddWithValue("tenant",tenantId); command.Parameters.AddWithValue("lead",leadId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var items=new List<LeadActivity>();
        while(await reader.ReadAsync(cancellationToken)) items.Add(new LeadActivity(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),(CrmActivityType)reader.GetInt32(3),reader.GetString(4),GetString(reader,5),GetGuid(reader,6),reader.GetFieldValue<DateTimeOffset>(7)));
        return items;
    }

    public async Task AddFollowUpAsync(LeadFollowUp followUp, CancellationToken cancellationToken = default)
    {
        await _database.EnsureReadyAsync(cancellationToken);
        const string sql = """
INSERT INTO businessos_crm.follow_ups(id,tenant_id,lead_id,due_at_utc,channel,purpose,owner_user_id,status,outcome,created_at_utc,completed_at_utc)
VALUES(@id,@tenant,@lead,@due,@channel,@purpose,@owner,@status,@outcome,@created,@completed);
""";
        await using var command=_database.DataSource.CreateCommand(sql); AddFollowUpParameters(command,followUp); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveFollowUpAsync(LeadFollowUp followUp, CancellationToken cancellationToken = default)
    {
        await _database.EnsureReadyAsync(cancellationToken);
        const string sql="UPDATE businessos_crm.follow_ups SET due_at_utc=@due,channel=@channel,purpose=@purpose,owner_user_id=@owner,status=@status,outcome=@outcome,completed_at_utc=@completed WHERE tenant_id=@tenant AND id=@id";
        await using var command=_database.DataSource.CreateCommand(sql); AddFollowUpParameters(command,followUp); if(await command.ExecuteNonQueryAsync(cancellationToken)==0) throw new InvalidOperationException("Follow-up does not exist.");
    }

    public async Task<LeadFollowUp?> GetFollowUpAsync(Guid tenantId, Guid followUpId, CancellationToken cancellationToken = default)
    {
        await _database.EnsureReadyAsync(cancellationToken);
        const string sql=FollowUpSelect+" WHERE tenant_id=@tenant AND id=@id LIMIT 1"; await using var command=_database.DataSource.CreateCommand(sql); command.Parameters.AddWithValue("tenant",tenantId); command.Parameters.AddWithValue("id",followUpId);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken)?ReadFollowUp(reader):null;
    }

    public async Task<IReadOnlyList<LeadFollowUp>> ListFollowUpsAsync(Guid tenantId, Guid? leadId = null, CancellationToken cancellationToken = default)
    {
        await _database.EnsureReadyAsync(cancellationToken);
        var sql=FollowUpSelect+" WHERE tenant_id=@tenant"+(leadId.HasValue?" AND lead_id=@lead":"")+" ORDER BY status,due_at_utc"; await using var command=_database.DataSource.CreateCommand(sql); command.Parameters.AddWithValue("tenant",tenantId); if(leadId.HasValue) command.Parameters.AddWithValue("lead",leadId.Value);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken); var items=new List<LeadFollowUp>(); while(await reader.ReadAsync(cancellationToken)) items.Add(ReadFollowUp(reader)); return items;
    }

    public async Task AddTaskAsync(CrmTask task, CancellationToken cancellationToken = default)
    {
        await _database.EnsureReadyAsync(cancellationToken);
        const string sql="INSERT INTO businessos_crm.tasks(id,tenant_id,lead_id,title,details,due_at_utc,priority,assignee_user_id,status,created_at_utc,completed_at_utc) VALUES(@id,@tenant,@lead,@title,@details,@due,@priority,@assignee,@status,@created,@completed)";
        await using var command=_database.DataSource.CreateCommand(sql); AddTaskParameters(command,task); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveTaskAsync(CrmTask task, CancellationToken cancellationToken = default)
    {
        await _database.EnsureReadyAsync(cancellationToken);
        const string sql="UPDATE businessos_crm.tasks SET title=@title,details=@details,due_at_utc=@due,priority=@priority,assignee_user_id=@assignee,status=@status,completed_at_utc=@completed WHERE tenant_id=@tenant AND id=@id";
        await using var command=_database.DataSource.CreateCommand(sql); AddTaskParameters(command,task); if(await command.ExecuteNonQueryAsync(cancellationToken)==0) throw new InvalidOperationException("Task does not exist.");
    }

    public async Task<CrmTask?> GetTaskAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken = default)
    {
        await _database.EnsureReadyAsync(cancellationToken);
        const string sql=TaskSelect+" WHERE tenant_id=@tenant AND id=@id LIMIT 1"; await using var command=_database.DataSource.CreateCommand(sql); command.Parameters.AddWithValue("tenant",tenantId); command.Parameters.AddWithValue("id",taskId);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken)?ReadTask(reader):null;
    }

    public async Task<IReadOnlyList<CrmTask>> ListTasksAsync(Guid tenantId, Guid? leadId = null, CancellationToken cancellationToken = default)
    {
        await _database.EnsureReadyAsync(cancellationToken);
        var sql=TaskSelect+" WHERE tenant_id=@tenant"+(leadId.HasValue?" AND lead_id=@lead":"")+" ORDER BY status,due_at_utc NULLS LAST"; await using var command=_database.DataSource.CreateCommand(sql); command.Parameters.AddWithValue("tenant",tenantId); if(leadId.HasValue) command.Parameters.AddWithValue("lead",leadId.Value);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken); var items=new List<CrmTask>(); while(await reader.ReadAsync(cancellationToken)) items.Add(ReadTask(reader)); return items;
    }

    private const string FollowUpSelect="SELECT id,tenant_id,lead_id,due_at_utc,channel,purpose,owner_user_id,status,outcome,created_at_utc,completed_at_utc FROM businessos_crm.follow_ups";
    private const string TaskSelect="SELECT id,tenant_id,lead_id,title,details,due_at_utc,priority,assignee_user_id,status,created_at_utc,completed_at_utc FROM businessos_crm.tasks";

    private static LeadFollowUp ReadFollowUp(NpgsqlDataReader r)
    {
        var x=new LeadFollowUp(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetFieldValue<DateTimeOffset>(3),(FollowUpChannel)r.GetInt32(4),r.GetString(5),GetGuid(r,6),r.GetFieldValue<DateTimeOffset>(9));
        var status=(CrmWorkStatus)r.GetInt32(7); if(status==CrmWorkStatus.Completed)x.Complete(GetString(r,8),GetDate(r,10)); else if(status==CrmWorkStatus.Cancelled)x.Cancel(GetString(r,8)); return x;
    }
    private static CrmTask ReadTask(NpgsqlDataReader r)
    {
        var x=new CrmTask(r.GetGuid(0),r.GetGuid(1),r.GetString(3),GetDate(r,5),GetGuid(r,2),GetString(r,4),(LeadPriority)r.GetInt32(6),GetGuid(r,7),r.GetFieldValue<DateTimeOffset>(9));
        var status=(CrmWorkStatus)r.GetInt32(8); if(status==CrmWorkStatus.Completed)x.Complete(GetDate(r,10)); else if(status==CrmWorkStatus.Cancelled)x.Cancel(); return x;
    }
    private static void AddFollowUpParameters(NpgsqlCommand c,LeadFollowUp x){c.Parameters.AddWithValue("id",x.Id);c.Parameters.AddWithValue("tenant",x.TenantId);c.Parameters.AddWithValue("lead",x.LeadId);c.Parameters.AddWithValue("due",x.DueAtUtc);c.Parameters.AddWithValue("channel",(int)x.Channel);c.Parameters.AddWithValue("purpose",x.Purpose);AddNullable(c,"owner",NpgsqlDbType.Uuid,x.OwnerUserId);c.Parameters.AddWithValue("status",(int)x.Status);AddNullable(c,"outcome",NpgsqlDbType.Text,x.Outcome);c.Parameters.AddWithValue("created",x.CreatedAtUtc);AddNullable(c,"completed",NpgsqlDbType.TimestampTz,x.CompletedAtUtc);}
    private static void AddTaskParameters(NpgsqlCommand c,CrmTask x){c.Parameters.AddWithValue("id",x.Id);c.Parameters.AddWithValue("tenant",x.TenantId);AddNullable(c,"lead",NpgsqlDbType.Uuid,x.LeadId);c.Parameters.AddWithValue("title",x.Title);AddNullable(c,"details",NpgsqlDbType.Text,x.Details);AddNullable(c,"due",NpgsqlDbType.TimestampTz,x.DueAtUtc);c.Parameters.AddWithValue("priority",(int)x.Priority);AddNullable(c,"assignee",NpgsqlDbType.Uuid,x.AssigneeUserId);c.Parameters.AddWithValue("status",(int)x.Status);c.Parameters.AddWithValue("created",x.CreatedAtUtc);AddNullable(c,"completed",NpgsqlDbType.TimestampTz,x.CompletedAtUtc);}
    private static string? GetString(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i); private static Guid? GetGuid(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetGuid(i); private static DateTimeOffset? GetDate(NpgsqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetFieldValue<DateTimeOffset>(i); private static void AddNullable(NpgsqlCommand c,string n,NpgsqlDbType type,object? v)=>c.Parameters.Add(n,type).Value=v??DBNull.Value;
}
