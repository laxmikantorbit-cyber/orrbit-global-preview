using System.Text.Json;
using BusinessOS.Crm;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public interface ICrmBusinessRecordStore
{
    Task AddAsync(CrmBusinessRecord record, CancellationToken cancellationToken = default);
    Task SaveAsync(CrmBusinessRecord record, CancellationToken cancellationToken = default);
    Task<CrmBusinessRecord?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmBusinessRecord>> ListAsync(Guid tenantId, CrmBusinessModule? module = null, CancellationToken cancellationToken = default);
}

public sealed class InMemoryCrmBusinessRecordStore : ICrmBusinessRecordStore
{
    private readonly Dictionary<Guid, CrmBusinessRecord> _records = [];
    private readonly object _gate = new();

    public Task AddAsync(CrmBusinessRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_records.ContainsKey(record.Id)) throw new InvalidOperationException("Business record already exists.");
            _records.Add(record.Id, record);
        }
        return Task.CompletedTask;
    }

    public Task SaveAsync(CrmBusinessRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_records.ContainsKey(record.Id)) throw new InvalidOperationException("Business record does not exist.");
            _records[record.Id] = record;
        }
        return Task.CompletedTask;
    }

    public Task<CrmBusinessRecord?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var record = _records.GetValueOrDefault(id);
            return Task.FromResult(record?.TenantId == tenantId ? record : null);
        }
    }

    public Task<IReadOnlyList<CrmBusinessRecord>> ListAsync(Guid tenantId, CrmBusinessModule? module = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<CrmBusinessRecord>>(_records.Values
                .Where(x => x.TenantId == tenantId && (!module.HasValue || x.Module == module))
                .OrderByDescending(x => x.UpdatedAtUtc)
                .ThenBy(x => x.Title)
                .ToArray());
        }
    }
}

public sealed class PostgresCrmBusinessRecordStore : ICrmBusinessRecordStore
{
    private readonly CrmPostgresDatabase _db;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PostgresCrmBusinessRecordStore(CrmPostgresDatabase db) => _db = db;

    public async Task AddAsync(CrmBusinessRecord record, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
INSERT INTO businessos_crm.business_records(
 id,tenant_id,module,account_id,title,status,amount,category,priority,start_date,due_date,owner_user_id,description,metadata,created_at_utc,updated_at_utc)
VALUES(
 @id,@tenant,@module,@account,@title,@status,@amount,@category,@priority,@start,@due,@owner,@description,CAST(@metadata AS jsonb),@created,@updated);
""");
        AddParameters(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(CrmBusinessRecord record, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
UPDATE businessos_crm.business_records SET
 account_id=@account,title=@title,status=@status,amount=@amount,category=@category,priority=@priority,
 start_date=@start,due_date=@due,owner_user_id=@owner,description=@description,metadata=CAST(@metadata AS jsonb),updated_at_utc=@updated
WHERE tenant_id=@tenant AND id=@id;
""");
        AddParameters(command, record);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Business record does not exist.");
    }

    public async Task<CrmBusinessRecord?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(SelectSql + " WHERE tenant_id=@tenant AND id=@id LIMIT 1");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<CrmBusinessRecord>> ListAsync(Guid tenantId, CrmBusinessModule? module = null, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        var sql = SelectSql + " WHERE tenant_id=@tenant" + (module.HasValue ? " AND module=@module" : "") + " ORDER BY updated_at_utc DESC,title";
        await using var command = _db.DataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("tenant", tenantId);
        if (module.HasValue) command.Parameters.AddWithValue("module", (int)module.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CrmBusinessRecord>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    private const string SelectSql = """
SELECT id,tenant_id,module,account_id,title,status,amount,category,priority,start_date,due_date,owner_user_id,description,metadata::text,created_at_utc,updated_at_utc
FROM businessos_crm.business_records
""";

    private static void AddParameters(NpgsqlCommand command, CrmBusinessRecord record)
    {
        command.Parameters.AddWithValue("id", record.Id);
        command.Parameters.AddWithValue("tenant", record.TenantId);
        command.Parameters.AddWithValue("module", (int)record.Module);
        Nullable(command, "account", NpgsqlDbType.Uuid, record.AccountId);
        command.Parameters.AddWithValue("title", record.Title);
        command.Parameters.AddWithValue("status", record.Status);
        Nullable(command, "amount", NpgsqlDbType.Numeric, record.Amount);
        Nullable(command, "category", NpgsqlDbType.Text, record.Category);
        Nullable(command, "priority", NpgsqlDbType.Text, record.Priority);
        Nullable(command, "start", NpgsqlDbType.Date, record.StartDate);
        Nullable(command, "due", NpgsqlDbType.Date, record.DueDate);
        Nullable(command, "owner", NpgsqlDbType.Uuid, record.OwnerUserId);
        Nullable(command, "description", NpgsqlDbType.Text, record.Description);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(record.Metadata, JsonOptions));
        command.Parameters.AddWithValue("created", record.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", record.UpdatedAtUtc);
    }

    private static CrmBusinessRecord Read(NpgsqlDataReader reader)
    {
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(13), JsonOptions) ?? [];
        return CrmBusinessRecord.Restore(
            reader.GetGuid(0), reader.GetGuid(1), (CrmBusinessModule)reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetString(4), reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetDecimal(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateOnly>(10), reader.IsDBNull(11) ? null : reader.GetGuid(11),
            reader.IsDBNull(12) ? null : reader.GetString(12), metadata,
            reader.GetFieldValue<DateTimeOffset>(14), reader.GetFieldValue<DateTimeOffset>(15));
    }

    private static void Nullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
}
