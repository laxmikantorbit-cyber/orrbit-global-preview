using BusinessOS.Crm;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public interface ICrmEstimateRequestStore
{
    Task AddAsync(CrmEstimateRequest request, CancellationToken cancellationToken = default);
    Task SaveAsync(CrmEstimateRequest request, CancellationToken cancellationToken = default);
    Task<CrmEstimateRequest?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmEstimateRequest>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryCrmEstimateRequestStore : ICrmEstimateRequestStore
{
    private readonly Dictionary<Guid, CrmEstimateRequest> _items = [];
    private readonly object _gate = new();

    public Task AddAsync(CrmEstimateRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_items.ContainsKey(request.Id)) throw new InvalidOperationException("Estimate request already exists.");
            _items.Add(request.Id, request);
        }
        return Task.CompletedTask;
    }

    public Task SaveAsync(CrmEstimateRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_items.ContainsKey(request.Id)) throw new InvalidOperationException("Estimate request does not exist.");
            _items[request.Id] = request;
        }
        return Task.CompletedTask;
    }

    public Task<CrmEstimateRequest?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _items.GetValueOrDefault(id);
            return Task.FromResult(item?.TenantId == tenantId ? item : null);
        }
    }

    public Task<IReadOnlyList<CrmEstimateRequest>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<CrmEstimateRequest>>(
                _items.Values.Where(x => x.TenantId == tenantId)
                    .OrderByDescending(x => x.UpdatedAtUtc).ToArray());
    }
}

public sealed class PostgresCrmEstimateRequestStore : ICrmEstimateRequestStore
{
    private readonly CrmPostgresDatabase _db;
    public PostgresCrmEstimateRequestStore(CrmPostgresDatabase db) => _db = db;

    public async Task AddAsync(CrmEstimateRequest request, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
INSERT INTO businessos_crm.estimate_requests(
 id,tenant_id,source,requirement,contact_name,mobile_number,email,expected_value,assigned_user_id,status,
 converted_lead_id,converted_estimate_id,created_at_utc,updated_at_utc)
VALUES(
 @id,@tenant,@source,@requirement,@contact,@mobile,@email,@value,@assigned,@status,
 @lead,@estimate,@created,@updated);
""");
        AddParameters(command, request);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(CrmEstimateRequest request, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
UPDATE businessos_crm.estimate_requests SET
 source=@source,requirement=@requirement,contact_name=@contact,mobile_number=@mobile,email=@email,
 expected_value=@value,assigned_user_id=@assigned,status=@status,converted_lead_id=@lead,
 converted_estimate_id=@estimate,updated_at_utc=@updated
WHERE tenant_id=@tenant AND id=@id;
""");
        AddParameters(command, request);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Estimate request does not exist.");
    }

    public async Task<CrmEstimateRequest?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(SelectSql + " WHERE tenant_id=@tenant AND id=@id LIMIT 1");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<CrmEstimateRequest>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(
            SelectSql + " WHERE tenant_id=@tenant ORDER BY updated_at_utc DESC,created_at_utc DESC");
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CrmEstimateRequest>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    private const string SelectSql = """
SELECT id,tenant_id,source,requirement,contact_name,mobile_number,email,expected_value,assigned_user_id,status,
       converted_lead_id,converted_estimate_id,created_at_utc,updated_at_utc
FROM businessos_crm.estimate_requests
""";

    private static void AddParameters(NpgsqlCommand command, CrmEstimateRequest request)
    {
        command.Parameters.AddWithValue("id", request.Id);
        command.Parameters.AddWithValue("tenant", request.TenantId);
        command.Parameters.AddWithValue("source", request.Source);
        command.Parameters.AddWithValue("requirement", request.Requirement);
        Nullable(command, "contact", NpgsqlDbType.Text, request.ContactName);
        Nullable(command, "mobile", NpgsqlDbType.Text, request.MobileNumber);
        Nullable(command, "email", NpgsqlDbType.Text, request.Email);
        Nullable(command, "value", NpgsqlDbType.Numeric, request.ExpectedValue);
        Nullable(command, "assigned", NpgsqlDbType.Uuid, request.AssignedUserId);
        command.Parameters.AddWithValue("status", (int)request.Status);
        Nullable(command, "lead", NpgsqlDbType.Uuid, request.ConvertedLeadId);
        Nullable(command, "estimate", NpgsqlDbType.Uuid, request.ConvertedEstimateId);
        command.Parameters.AddWithValue("created", request.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", request.UpdatedAtUtc);
    }

    private static CrmEstimateRequest Read(NpgsqlDataReader reader) =>
        CrmEstimateRequest.Restore(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8), (CrmEstimateRequestStatus)reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10), reader.IsDBNull(11) ? null : reader.GetGuid(11),
            reader.GetFieldValue<DateTimeOffset>(12), reader.GetFieldValue<DateTimeOffset>(13));

    private static void Nullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
}
