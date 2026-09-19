using System.Text.Json;
using BusinessOS.Crm;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public sealed class PostgresCrmLeadRepository : ILeadRepository
{
    private readonly CrmPostgresDatabase _database;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PostgresCrmLeadRepository(CrmPostgresDatabase database) => _database = database;

    public async Task AddAsync(Lead lead, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lead);
        await _database.EnsureReadyAsync(cancellationToken);
        const string sql = """
INSERT INTO businessos_crm.leads
(id, tenant_id, organisation_id, title, attribution, contact_name, mobile_number, email, product_interest, notes,
 status, priority, unqualified_reason, created_at_utc, updated_at_utc, last_contact_at_utc, next_follow_up_at_utc, tags, estimated_value)
VALUES (@id, @tenant_id, @organisation_id, @title, CAST(@attribution AS jsonb), @contact_name, @mobile_number, @email,
 @product_interest, @notes, @status, @priority, @unqualified_reason, @created_at_utc, @updated_at_utc,
 @last_contact_at_utc, @next_follow_up_at_utc, CAST(@tags AS jsonb), @estimated_value);
""";
        await using var command = _database.DataSource.CreateCommand(sql);
        AddParameters(command, lead);
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new InvalidOperationException("Lead id already exists.", ex); }
    }

    public async Task SaveAsync(Lead lead, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lead);
        await _database.EnsureReadyAsync(cancellationToken);
        const string sql = """
UPDATE businessos_crm.leads SET
 organisation_id=@organisation_id, title=@title, attribution=CAST(@attribution AS jsonb), contact_name=@contact_name,
 mobile_number=@mobile_number, email=@email, product_interest=@product_interest, notes=@notes, status=@status,
 priority=@priority, unqualified_reason=@unqualified_reason, updated_at_utc=@updated_at_utc,
 last_contact_at_utc=@last_contact_at_utc, next_follow_up_at_utc=@next_follow_up_at_utc, tags=CAST(@tags AS jsonb),
 estimated_value=@estimated_value
WHERE tenant_id=@tenant_id AND id=@id;
""";
        await using var command = _database.DataSource.CreateCommand(sql);
        AddParameters(command, lead);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Lead does not exist.");
    }

    public async Task<Lead?> GetAsync(Guid tenantId, Guid leadId, CancellationToken cancellationToken = default)
    {
        await _database.EnsureReadyAsync(cancellationToken);
        const string sql = SelectSql + " WHERE tenant_id=@tenant_id AND id=@id LIMIT 1";
        await using var command = _database.DataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", leadId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadLead(reader) : null;
    }

    public async Task<IReadOnlyList<Lead>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _database.EnsureReadyAsync(cancellationToken);
        const string sql = SelectSql + " WHERE tenant_id=@tenant_id ORDER BY created_at_utc DESC";
        await using var command = _database.DataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<Lead>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadLead(reader));
        return items;
    }

    private const string SelectSql = """
SELECT id, tenant_id, organisation_id, title, attribution::text, contact_name, mobile_number, email, product_interest,
 notes, status, priority, unqualified_reason, created_at_utc, updated_at_utc, last_contact_at_utc,
 next_follow_up_at_utc, tags::text, estimated_value FROM businessos_crm.leads
""";

    private static void AddParameters(NpgsqlCommand command, Lead lead)
    {
        command.Parameters.AddWithValue("id", lead.Id);
        command.Parameters.AddWithValue("tenant_id", lead.TenantId);
        command.Parameters.AddWithValue("organisation_id", lead.OrganisationId);
        command.Parameters.AddWithValue("title", lead.Title);
        command.Parameters.AddWithValue("attribution", JsonSerializer.Serialize(lead.Attribution, JsonOptions));
        AddNullable(command, "contact_name", NpgsqlDbType.Text, lead.ContactName);
        AddNullable(command, "mobile_number", NpgsqlDbType.Text, lead.MobileNumber);
        AddNullable(command, "email", NpgsqlDbType.Text, lead.Email);
        AddNullable(command, "product_interest", NpgsqlDbType.Text, lead.ProductInterest);
        AddNullable(command, "notes", NpgsqlDbType.Text, lead.Notes);
        command.Parameters.AddWithValue("status", (int)lead.Status);
        command.Parameters.AddWithValue("priority", (int)lead.Priority);
        AddNullable(command, "unqualified_reason", NpgsqlDbType.Text, lead.UnqualifiedReason);
        command.Parameters.AddWithValue("created_at_utc", lead.CreatedAtUtc);
        command.Parameters.AddWithValue("updated_at_utc", lead.UpdatedAtUtc);
        AddNullable(command, "last_contact_at_utc", NpgsqlDbType.TimestampTz, lead.LastContactAtUtc);
        AddNullable(command, "next_follow_up_at_utc", NpgsqlDbType.TimestampTz, lead.NextFollowUpAtUtc);
        command.Parameters.AddWithValue("tags", JsonSerializer.Serialize(lead.Tags, JsonOptions));
        AddNullable(command, "estimated_value", NpgsqlDbType.Numeric, lead.EstimatedValue);
    }

    private static Lead ReadLead(NpgsqlDataReader reader)
    {
        var attribution = JsonSerializer.Deserialize<LeadAttribution>(reader.GetString(4), JsonOptions)
            ?? new LeadAttribution(null, null, null, null, null, null);
        var tags = JsonSerializer.Deserialize<string[]>(reader.GetString(17), JsonOptions) ?? [];
        return Lead.Restore(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), attribution,
            GetString(reader, 5), GetString(reader, 6), GetString(reader, 7), GetString(reader, 8), GetString(reader, 9),
            (LeadPriority)reader.GetInt32(11), (LeadStatus)reader.GetInt32(10), GetString(reader, 12),
            reader.GetFieldValue<DateTimeOffset>(13), reader.GetFieldValue<DateTimeOffset>(14),
            GetDate(reader, 15), GetDate(reader, 16), tags, GetDecimal(reader, 18));
    }

    private static string? GetString(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static DateTimeOffset? GetDate(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetFieldValue<DateTimeOffset>(index);
    private static decimal? GetDecimal(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetDecimal(index);
    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) => command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
}
