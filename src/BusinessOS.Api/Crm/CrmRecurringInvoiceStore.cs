using System.Text.Json;
using BusinessOS.Sales;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public sealed record CrmRecurringInvoiceTemplate(
    Guid Id,
    Guid TenantId,
    string Name,
    Guid AccountId,
    Guid? OpportunityId,
    string Subject,
    string CurrencyCode,
    int DueDays,
    decimal DiscountPercent,
    string? Notes,
    string? Terms,
    IReadOnlyList<SalesDocumentLine> Lines,
    string Frequency,
    DateOnly NextIssueDate,
    bool Active,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public interface ICrmRecurringInvoiceStore
{
    Task AddAsync(CrmRecurringInvoiceTemplate template, CancellationToken cancellationToken = default);
    Task SaveAsync(CrmRecurringInvoiceTemplate template, CancellationToken cancellationToken = default);
    Task<CrmRecurringInvoiceTemplate?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmRecurringInvoiceTemplate>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public static class CrmRecurringInvoiceRules
{
    public static string NormalizeFrequency(string? value)
    {
        var normalized = value?.Trim();
        return normalized?.ToLowerInvariant() switch
        {
            "monthly" => "Monthly",
            "quarterly" => "Quarterly",
            "yearly" => "Yearly",
            _ => throw new ArgumentException("Frequency must be Monthly, Quarterly or Yearly.", nameof(value))
        };
    }

    public static DateOnly NextDate(DateOnly date, string frequency) => NormalizeFrequency(frequency) switch
    {
        "Monthly" => date.AddMonths(1),
        "Quarterly" => date.AddMonths(3),
        "Yearly" => date.AddYears(1),
        _ => throw new ArgumentOutOfRangeException(nameof(frequency))
    };

    public static CrmRecurringInvoiceTemplate Create(
        Guid tenantId,
        string name,
        SalesInvoice source,
        string frequency,
        DateOnly nextIssueDate,
        int dueDays)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Recurring template name is required.", nameof(name));
        if (dueDays is < 0 or > 365) throw new ArgumentOutOfRangeException(nameof(dueDays), "Due days must be between 0 and 365.");
        if (source.Status == SalesInvoiceStatus.Void) throw new InvalidOperationException("Void invoice cannot be used as a recurring template.");
        var now = DateTimeOffset.UtcNow;
        return new CrmRecurringInvoiceTemplate(
            Guid.NewGuid(), tenantId, name.Trim(), source.AccountId, source.OpportunityId, source.Subject,
            source.CurrencyCode, dueDays, source.DiscountPercent, source.Notes, source.Terms,
            source.Lines.Select(line => line with { Id = Guid.NewGuid() }).ToArray(),
            NormalizeFrequency(frequency), nextIssueDate, true, now, now);
    }
}

public sealed class InMemoryCrmRecurringInvoiceStore : ICrmRecurringInvoiceStore
{
    private readonly Dictionary<Guid, CrmRecurringInvoiceTemplate> _items = [];
    private readonly object _gate = new();

    public Task AddAsync(CrmRecurringInvoiceTemplate template, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_items.ContainsKey(template.Id)) throw new InvalidOperationException("Recurring invoice template already exists.");
            _items.Add(template.Id, template);
        }
        return Task.CompletedTask;
    }

    public Task SaveAsync(CrmRecurringInvoiceTemplate template, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_items.ContainsKey(template.Id)) throw new InvalidOperationException("Recurring invoice template not found.");
            _items[template.Id] = template;
        }
        return Task.CompletedTask;
    }

    public Task<CrmRecurringInvoiceTemplate?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var value = _items.GetValueOrDefault(id);
            return Task.FromResult(value?.TenantId == tenantId ? value : null);
        }
    }

    public Task<IReadOnlyList<CrmRecurringInvoiceTemplate>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<CrmRecurringInvoiceTemplate>>(
                _items.Values.Where(x => x.TenantId == tenantId)
                    .OrderBy(x => x.NextIssueDate).ThenBy(x => x.Name).ToArray());
    }
}

public sealed class PostgresCrmRecurringInvoiceStore : ICrmRecurringInvoiceStore
{
    private readonly CrmPostgresDatabase _db;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PostgresCrmRecurringInvoiceStore(CrmPostgresDatabase db) => _db = db;

    public async Task AddAsync(CrmRecurringInvoiceTemplate template, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
INSERT INTO businessos_crm.recurring_invoice_templates(
 id,tenant_id,name,account_id,opportunity_id,subject,currency_code,due_days,discount_percent,
 notes,terms,lines,frequency,next_issue_date,active,created_at_utc,updated_at_utc)
VALUES(
 @id,@tenant,@name,@account,@opportunity,@subject,@currency,@dueDays,@discount,
 @notes,@terms,CAST(@lines AS jsonb),@frequency,@nextIssue,@active,@created,@updated);
""");
        AddParameters(command, template);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(CrmRecurringInvoiceTemplate template, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
UPDATE businessos_crm.recurring_invoice_templates SET
 name=@name,account_id=@account,opportunity_id=@opportunity,subject=@subject,currency_code=@currency,
 due_days=@dueDays,discount_percent=@discount,notes=@notes,terms=@terms,lines=CAST(@lines AS jsonb),
 frequency=@frequency,next_issue_date=@nextIssue,active=@active,updated_at_utc=@updated
WHERE tenant_id=@tenant AND id=@id;
""");
        AddParameters(command, template);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Recurring invoice template not found.");
    }

    public async Task<CrmRecurringInvoiceTemplate?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(SelectSql + " WHERE tenant_id=@tenant AND id=@id LIMIT 1");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<CrmRecurringInvoiceTemplate>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(
            SelectSql + " WHERE tenant_id=@tenant ORDER BY next_issue_date,name");
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<CrmRecurringInvoiceTemplate>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(Read(reader));
        return items;
    }

    private const string SelectSql = """
SELECT id,tenant_id,name,account_id,opportunity_id,subject,currency_code,due_days,discount_percent,
       notes,terms,lines::text,frequency,next_issue_date,active,created_at_utc,updated_at_utc
FROM businessos_crm.recurring_invoice_templates
""";

    private static void AddParameters(NpgsqlCommand command, CrmRecurringInvoiceTemplate item)
    {
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("tenant", item.TenantId);
        command.Parameters.AddWithValue("name", item.Name);
        command.Parameters.AddWithValue("account", item.AccountId);
        Nullable(command, "opportunity", NpgsqlDbType.Uuid, item.OpportunityId);
        command.Parameters.AddWithValue("subject", item.Subject);
        command.Parameters.AddWithValue("currency", item.CurrencyCode);
        command.Parameters.AddWithValue("dueDays", item.DueDays);
        command.Parameters.AddWithValue("discount", item.DiscountPercent);
        Nullable(command, "notes", NpgsqlDbType.Text, item.Notes);
        Nullable(command, "terms", NpgsqlDbType.Text, item.Terms);
        command.Parameters.AddWithValue("lines", JsonSerializer.Serialize(item.Lines, JsonOptions));
        command.Parameters.AddWithValue("frequency", item.Frequency);
        command.Parameters.AddWithValue("nextIssue", item.NextIssueDate);
        command.Parameters.AddWithValue("active", item.Active);
        command.Parameters.AddWithValue("created", item.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", item.UpdatedAtUtc);
    }

    private static CrmRecurringInvoiceTemplate Read(NpgsqlDataReader reader)
    {
        var lines = JsonSerializer.Deserialize<List<SalesDocumentLine>>(reader.GetString(11), JsonOptions) ?? [];
        return new CrmRecurringInvoiceTemplate(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetString(5), reader.GetString(6),
            reader.GetInt32(7), reader.GetDecimal(8), reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10), lines, reader.GetString(12),
            reader.GetFieldValue<DateOnly>(13), reader.GetBoolean(14),
            reader.GetFieldValue<DateTimeOffset>(15), reader.GetFieldValue<DateTimeOffset>(16));
    }

    private static void Nullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
}
