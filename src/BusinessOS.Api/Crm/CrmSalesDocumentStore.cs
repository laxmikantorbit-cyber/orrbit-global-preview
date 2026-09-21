using System.Text.Json;
using BusinessOS.Sales;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public interface ICrmSalesDocumentStore
{
    Task AddAsync(SalesDocument document, CancellationToken cancellationToken = default);
    Task SaveAsync(SalesDocument document, CancellationToken cancellationToken = default);
    Task<SalesDocument?> GetAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SalesDocument>> ListAsync(Guid tenantId, SalesDocumentKind? kind = null, CancellationToken cancellationToken = default);
    Task<string> NextNumberAsync(Guid tenantId, SalesDocumentKind kind, CancellationToken cancellationToken = default);
}

public sealed class InMemoryCrmSalesDocumentStore : ICrmSalesDocumentStore
{
    private readonly Dictionary<Guid, SalesDocument> _items = [];
    private readonly object _gate = new();

    public Task AddAsync(SalesDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_items.ContainsKey(document.Id)) throw new InvalidOperationException("Sales document already exists.");
            if (_items.Values.Any(x => x.TenantId == document.TenantId && x.Kind == document.Kind &&
                x.DocumentNumber.Equals(document.DocumentNumber, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Sales document number already exists.");
            _items.Add(document.Id, document);
        }
        return Task.CompletedTask;
    }

    public Task SaveAsync(SalesDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_items.ContainsKey(document.Id)) throw new InvalidOperationException("Sales document does not exist.");
            _items[document.Id] = document;
        }
        return Task.CompletedTask;
    }

    public Task<SalesDocument?> GetAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _items.GetValueOrDefault(documentId);
            return Task.FromResult(item?.TenantId == tenantId ? item : null);
        }
    }

    public Task<IReadOnlyList<SalesDocument>> ListAsync(
        Guid tenantId, SalesDocumentKind? kind = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<SalesDocument>>(_items.Values
                .Where(x => x.TenantId == tenantId && (!kind.HasValue || x.Kind == kind))
                .OrderByDescending(x => x.IssueDate).ThenByDescending(x => x.DocumentNumber).ToArray());
    }

    public Task<string> NextNumberAsync(Guid tenantId, SalesDocumentKind kind, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var max = _items.Values.Where(x => x.TenantId == tenantId && x.Kind == kind)
                .Select(x => int.TryParse(x.DocumentNumber.Split('-').LastOrDefault(), out var n) ? n : 0)
                .DefaultIfEmpty(0).Max();
            return Task.FromResult(FormatNumber(kind, max + 1));
        }
    }

    internal static string FormatNumber(SalesDocumentKind kind, int sequence) =>
        $"{(kind == SalesDocumentKind.Proposal ? "PRO" : "EST")}-{sequence:000000}";
}

public sealed class PostgresCrmSalesDocumentStore : ICrmSalesDocumentStore
{
    private readonly CrmPostgresDatabase _db;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PostgresCrmSalesDocumentStore(CrmPostgresDatabase db) => _db = db;

    public async Task AddAsync(SalesDocument document, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        const string sql = """
INSERT INTO businessos_crm.sales_documents(
 id,tenant_id,account_id,opportunity_id,kind,document_number,subject,status,currency_code,
 issue_date,expiry_date,discount_percent,notes,terms,lines,created_at_utc,updated_at_utc)
VALUES(
 @id,@tenant,@account,@opportunity,@kind,@number,@subject,@status,@currency,
 @issue,@expiry,@discount,@notes,@terms,CAST(@lines AS jsonb),@created,@updated);
""";
        await using var command = _db.DataSource.CreateCommand(sql);
        AddParameters(command, document);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(SalesDocument document, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        const string sql = """
UPDATE businessos_crm.sales_documents SET
 account_id=@account,opportunity_id=@opportunity,subject=@subject,status=@status,currency_code=@currency,
 issue_date=@issue,expiry_date=@expiry,discount_percent=@discount,notes=@notes,terms=@terms,
 lines=CAST(@lines AS jsonb),updated_at_utc=@updated
WHERE tenant_id=@tenant AND id=@id;
""";
        await using var command = _db.DataSource.CreateCommand(sql);
        AddParameters(command, document);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Sales document does not exist.");
    }

    public async Task<SalesDocument?> GetAsync(
        Guid tenantId, Guid documentId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(SelectSql + " WHERE tenant_id=@tenant AND id=@id LIMIT 1");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", documentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<SalesDocument>> ListAsync(
        Guid tenantId, SalesDocumentKind? kind = null, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        var sql = SelectSql + " WHERE tenant_id=@tenant" + (kind.HasValue ? " AND kind=@kind" : "") +
            " ORDER BY issue_date DESC, document_number DESC";
        await using var command = _db.DataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("tenant", tenantId);
        if (kind.HasValue) command.Parameters.AddWithValue("kind", (int)kind.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<SalesDocument>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(Read(reader));
        return items;
    }

    public async Task<string> NextNumberAsync(
        Guid tenantId, SalesDocumentKind kind, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        const string sql = """
SELECT COALESCE(MAX(
 CASE WHEN document_number ~ '^[A-Z]{3}-[0-9]{6}$'
 THEN RIGHT(document_number,6)::integer ELSE 0 END),0) + 1
FROM businessos_crm.sales_documents
WHERE tenant_id=@tenant AND kind=@kind;
""";
        await using var command = _db.DataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("kind", (int)kind);
        var next = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return InMemoryCrmSalesDocumentStore.FormatNumber(kind, next);
    }

    private const string SelectSql = """
SELECT id,tenant_id,account_id,opportunity_id,kind,document_number,subject,status,currency_code,
 issue_date,expiry_date,discount_percent,notes,terms,lines::text,created_at_utc,updated_at_utc
FROM businessos_crm.sales_documents
""";

    private static void AddParameters(NpgsqlCommand command, SalesDocument document)
    {
        command.Parameters.AddWithValue("id", document.Id);
        command.Parameters.AddWithValue("tenant", document.TenantId);
        command.Parameters.AddWithValue("account", document.AccountId);
        Nullable(command, "opportunity", NpgsqlDbType.Uuid, document.OpportunityId);
        command.Parameters.AddWithValue("kind", (int)document.Kind);
        command.Parameters.AddWithValue("number", document.DocumentNumber);
        command.Parameters.AddWithValue("subject", document.Subject);
        command.Parameters.AddWithValue("status", (int)document.Status);
        command.Parameters.AddWithValue("currency", document.CurrencyCode);
        command.Parameters.AddWithValue("issue", NpgsqlDbType.Date, document.IssueDate);
        Nullable(command, "expiry", NpgsqlDbType.Date, document.ExpiryDate);
        command.Parameters.AddWithValue("discount", document.DiscountPercent);
        Nullable(command, "notes", NpgsqlDbType.Text, document.Notes);
        Nullable(command, "terms", NpgsqlDbType.Text, document.Terms);
        command.Parameters.AddWithValue("lines", JsonSerializer.Serialize(document.Lines, JsonOptions));
        command.Parameters.AddWithValue("created", document.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", document.UpdatedAtUtc);
    }

    private static SalesDocument Read(NpgsqlDataReader reader)
    {
        var lines = JsonSerializer.Deserialize<SalesDocumentLine[]>(reader.GetString(14), JsonOptions) ?? [];
        return SalesDocument.Restore(
            reader.GetGuid(0), reader.GetGuid(1), (SalesDocumentKind)reader.GetInt32(4),
            reader.GetString(5), reader.GetGuid(2), reader.GetString(6), lines, reader.GetString(8),
            reader.GetFieldValue<DateOnly>(9), reader.IsDBNull(10) ? null : reader.GetFieldValue<DateOnly>(10),
            reader.GetDecimal(11), reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(12) ? null : reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetString(13),
            (SalesDocumentStatus)reader.GetInt32(7), reader.GetFieldValue<DateTimeOffset>(15),
            reader.GetFieldValue<DateTimeOffset>(16));
    }

    private static void Nullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
}
