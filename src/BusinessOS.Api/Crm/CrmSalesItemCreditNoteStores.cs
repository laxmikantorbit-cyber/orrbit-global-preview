using BusinessOS.Sales;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public interface ICrmSalesItemStore
{
    Task AddAsync(SalesItem item, CancellationToken cancellationToken = default);
    Task SaveAsync(SalesItem item, CancellationToken cancellationToken = default);
    Task<SalesItem?> GetAsync(Guid tenantId, Guid itemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SalesItem>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public interface ICrmCreditNoteStore
{
    Task AddAsync(CreditNote note, CancellationToken cancellationToken = default);
    Task SaveAsync(CreditNote note, CancellationToken cancellationToken = default);
    Task<CreditNote?> GetAsync(Guid tenantId, Guid creditNoteId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CreditNote>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<string> NextNumberAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<(CreditNote Note, SalesInvoice Invoice)> IssueAsync(
        Guid tenantId, Guid creditNoteId, DateOnly? asOf = null, CancellationToken cancellationToken = default);
    Task<(CreditNote Note, SalesInvoice Invoice)> VoidAsync(
        Guid tenantId, Guid creditNoteId, DateOnly? asOf = null, CancellationToken cancellationToken = default);
}

public sealed class InMemoryCrmSalesItemStore : ICrmSalesItemStore
{
    private readonly Dictionary<Guid, SalesItem> _items = [];
    private readonly object _gate = new();

    public Task AddAsync(SalesItem item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_items.ContainsKey(item.Id)) throw new InvalidOperationException("Sales item already exists.");
            if (_items.Values.Any(x => x.TenantId == item.TenantId &&
                x.Code.Equals(item.Code, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Sales item code already exists.");
            _items.Add(item.Id, item);
        }
        return Task.CompletedTask;
    }

    public Task SaveAsync(SalesItem item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_items.ContainsKey(item.Id)) throw new InvalidOperationException("Sales item does not exist.");
            _items[item.Id] = item;
        }
        return Task.CompletedTask;
    }

    public Task<SalesItem?> GetAsync(Guid tenantId, Guid itemId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _items.GetValueOrDefault(itemId);
            return Task.FromResult(item?.TenantId == tenantId ? item : null);
        }
    }

    public Task<IReadOnlyList<SalesItem>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<SalesItem>>(
                _items.Values.Where(x => x.TenantId == tenantId)
                    .OrderBy(x => x.Name).ThenBy(x => x.Code).ToArray());
    }
}

public sealed class PostgresCrmSalesItemStore : ICrmSalesItemStore
{
    private readonly CrmPostgresDatabase _db;
    public PostgresCrmSalesItemStore(CrmPostgresDatabase db) => _db = db;

    public async Task AddAsync(SalesItem item, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
INSERT INTO businessos_crm.sales_items(
 id,tenant_id,code,name,description,default_rate,default_tax_percent,status,catalog_product_id,created_at_utc,updated_at_utc)
VALUES(
 @id,@tenant,@code,@name,@description,@rate,@tax,@status,@catalog,@created,@updated);
""");
        AddParameters(command, item);
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new InvalidOperationException("Sales item code already exists.", ex); }
    }

    public async Task SaveAsync(SalesItem item, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
UPDATE businessos_crm.sales_items SET
 name=@name,description=@description,default_rate=@rate,default_tax_percent=@tax,
 status=@status,updated_at_utc=@updated
WHERE tenant_id=@tenant AND id=@id;
""");
        AddParameters(command, item);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Sales item does not exist.");
    }

    public async Task<SalesItem?> GetAsync(Guid tenantId, Guid itemId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(SelectSql + " WHERE tenant_id=@tenant AND id=@id LIMIT 1");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<SalesItem>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(
            SelectSql + " WHERE tenant_id=@tenant ORDER BY name,code");
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<SalesItem>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(Read(reader));
        return items;
    }

    private const string SelectSql = """
SELECT id,tenant_id,code,name,description,default_rate,default_tax_percent,status,catalog_product_id,created_at_utc,updated_at_utc
FROM businessos_crm.sales_items
""";

    private static void AddParameters(NpgsqlCommand command, SalesItem item)
    {
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("tenant", item.TenantId);
        command.Parameters.AddWithValue("code", item.Code);
        command.Parameters.AddWithValue("name", item.Name);
        Nullable(command, "description", NpgsqlDbType.Text, item.Description);
        command.Parameters.AddWithValue("rate", item.DefaultRate);
        command.Parameters.AddWithValue("tax", item.DefaultTaxPercent);
        command.Parameters.AddWithValue("status", (int)item.Status);
        Nullable(command, "catalog", NpgsqlDbType.Uuid, item.CatalogProductId);
        command.Parameters.AddWithValue("created", item.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", item.UpdatedAtUtc);
    }

    private static SalesItem Read(NpgsqlDataReader reader) =>
        SalesItem.Restore(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetDecimal(5), reader.GetDecimal(6),
            (SalesItemStatus)reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetGuid(8),
            reader.GetFieldValue<DateTimeOffset>(9), reader.GetFieldValue<DateTimeOffset>(10));

    private static void Nullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
}

public sealed class InMemoryCrmCreditNoteStore : ICrmCreditNoteStore
{
    private readonly Dictionary<Guid, CreditNote> _notes = [];
    private readonly object _gate = new();
    private readonly ICrmInvoiceStore _invoices;

    public InMemoryCrmCreditNoteStore(ICrmInvoiceStore invoices) => _invoices = invoices;

    public Task AddAsync(CreditNote note, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_notes.ContainsKey(note.Id)) throw new InvalidOperationException("Credit note already exists.");
            if (_notes.Values.Any(x => x.TenantId == note.TenantId &&
                x.CreditNoteNumber.Equals(note.CreditNoteNumber, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Credit note number already exists.");
            _notes.Add(note.Id, note);
        }
        return Task.CompletedTask;
    }

    public Task SaveAsync(CreditNote note, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_notes.ContainsKey(note.Id)) throw new InvalidOperationException("Credit note does not exist.");
            _notes[note.Id] = note;
        }
        return Task.CompletedTask;
    }

    public Task<CreditNote?> GetAsync(Guid tenantId, Guid creditNoteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var note = _notes.GetValueOrDefault(creditNoteId);
            return Task.FromResult(note?.TenantId == tenantId ? note : null);
        }
    }

    public Task<IReadOnlyList<CreditNote>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<CreditNote>>(
                _notes.Values.Where(x => x.TenantId == tenantId)
                    .OrderByDescending(x => x.IssueDate).ThenByDescending(x => x.CreditNoteNumber).ToArray());
    }

    public Task<string> NextNumberAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var next = _notes.Values.Where(x => x.TenantId == tenantId)
                .Select(x => ParseSequence(x.CreditNoteNumber)).DefaultIfEmpty(0).Max() + 1;
            return Task.FromResult(FormatNumber(next));
        }
    }

    public async Task<(CreditNote Note, SalesInvoice Invoice)> IssueAsync(
        Guid tenantId, Guid creditNoteId, DateOnly? asOf = null, CancellationToken cancellationToken = default)
    {
        var note = await GetAsync(tenantId, creditNoteId, cancellationToken)
            ?? throw new InvalidOperationException("Credit note not found.");
        var invoice = await _invoices.GetAsync(tenantId, note.InvoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");
        invoice.ApplyCredit(note.Amount, asOf ?? note.IssueDate);
        note.Issue();
        await _invoices.SaveAsync(invoice, cancellationToken);
        await SaveAsync(note, cancellationToken);
        return (note, invoice);
    }

    public async Task<(CreditNote Note, SalesInvoice Invoice)> VoidAsync(
        Guid tenantId, Guid creditNoteId, DateOnly? asOf = null, CancellationToken cancellationToken = default)
    {
        var note = await GetAsync(tenantId, creditNoteId, cancellationToken)
            ?? throw new InvalidOperationException("Credit note not found.");
        var invoice = await _invoices.GetAsync(tenantId, note.InvoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice not found.");
        if (note.Status != CreditNoteStatus.Issued)
            throw new InvalidOperationException("Only issued credit notes can be voided.");
        invoice.ReverseCredit(note.Amount, asOf ?? note.IssueDate);
        note.Void();
        await _invoices.SaveAsync(invoice, cancellationToken);
        await SaveAsync(note, cancellationToken);
        return (note, invoice);
    }

    internal static string FormatNumber(int sequence) => $"CN-{sequence:000000}";
    private static int ParseSequence(string value) =>
        value.StartsWith("CN-", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(value[3..], out var sequence) ? sequence : 0;
}

public sealed class PostgresCrmCreditNoteStore : ICrmCreditNoteStore
{
    private readonly CrmPostgresDatabase _db;
    public PostgresCrmCreditNoteStore(CrmPostgresDatabase db) => _db = db;

    public async Task AddAsync(CreditNote note, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(InsertSql);
        AddParameters(command, note);
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new InvalidOperationException("Credit note number already exists.", ex); }
    }

    public async Task SaveAsync(CreditNote note, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(UpdateSql);
        AddParameters(command, note);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Credit note does not exist.");
    }

    public async Task<CreditNote?> GetAsync(Guid tenantId, Guid creditNoteId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(SelectSql + " WHERE tenant_id=@tenant AND id=@id LIMIT 1");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", creditNoteId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<CreditNote>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(
            SelectSql + " WHERE tenant_id=@tenant ORDER BY issue_date DESC,credit_note_number DESC");
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var notes = new List<CreditNote>();
        while (await reader.ReadAsync(cancellationToken)) notes.Add(Read(reader));
        return notes;
    }

    public async Task<string> NextNumberAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
SELECT COALESCE(MAX(
 CASE WHEN credit_note_number ~ '^CN-[0-9]{6}$'
 THEN RIGHT(credit_note_number,6)::integer ELSE 0 END),0) + 1
FROM businessos_crm.credit_notes WHERE tenant_id=@tenant;
""");
        command.Parameters.AddWithValue("tenant", tenantId);
        var next = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return InMemoryCrmCreditNoteStore.FormatNumber(next);
    }

    public Task<(CreditNote Note, SalesInvoice Invoice)> IssueAsync(
        Guid tenantId, Guid creditNoteId, DateOnly? asOf = null, CancellationToken cancellationToken = default) =>
        ChangeSettlementAsync(tenantId, creditNoteId, issue: true, asOf, cancellationToken);

    public Task<(CreditNote Note, SalesInvoice Invoice)> VoidAsync(
        Guid tenantId, Guid creditNoteId, DateOnly? asOf = null, CancellationToken cancellationToken = default) =>
        ChangeSettlementAsync(tenantId, creditNoteId, issue: false, asOf, cancellationToken);

    private async Task<(CreditNote Note, SalesInvoice Invoice)> ChangeSettlementAsync(
        Guid tenantId, Guid creditNoteId, bool issue, DateOnly? asOf, CancellationToken cancellationToken)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var connection = await _db.DataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        CreditNote note;
        await using (var selectNote = new NpgsqlCommand(
            SelectSql + " WHERE tenant_id=@tenant AND id=@id FOR UPDATE", connection, transaction))
        {
            selectNote.Parameters.AddWithValue("tenant", tenantId);
            selectNote.Parameters.AddWithValue("id", creditNoteId);
            await using var reader = await selectNote.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Credit note not found.");
            note = Read(reader);
        }

        SalesInvoice invoice;
        await using (var selectInvoice = new NpgsqlCommand(
            PostgresCrmInvoiceStore.SelectInvoiceSql + " WHERE tenant_id=@tenant AND id=@id FOR UPDATE",
            connection, transaction))
        {
            selectInvoice.Parameters.AddWithValue("tenant", tenantId);
            selectInvoice.Parameters.AddWithValue("id", note.InvoiceId);
            await using var reader = await selectInvoice.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Invoice not found.");
            invoice = PostgresCrmInvoiceStore.ReadInvoice(reader);
        }

        if (issue)
        {
            invoice.ApplyCredit(note.Amount, asOf ?? note.IssueDate);
            note.Issue();
        }
        else
        {
            if (note.Status != CreditNoteStatus.Issued)
                throw new InvalidOperationException("Only issued credit notes can be voided.");
            invoice.ReverseCredit(note.Amount, asOf ?? note.IssueDate);
            note.Void();
        }

        await using (var updateInvoice = new NpgsqlCommand(
            PostgresCrmInvoiceStore.UpdateInvoiceSql, connection, transaction))
        {
            PostgresCrmInvoiceStore.AddInvoiceParameters(updateInvoice, invoice);
            if (await updateInvoice.ExecuteNonQueryAsync(cancellationToken) == 0)
                throw new InvalidOperationException("Invoice disappeared while applying credit note.");
        }

        await using (var updateNote = new NpgsqlCommand(UpdateSql, connection, transaction))
        {
            AddParameters(updateNote, note);
            if (await updateNote.ExecuteNonQueryAsync(cancellationToken) == 0)
                throw new InvalidOperationException("Credit note disappeared while applying adjustment.");
        }

        await transaction.CommitAsync(cancellationToken);
        return (note, invoice);
    }

    private const string SelectSql = """
SELECT id,tenant_id,invoice_id,account_id,credit_note_number,issue_date,amount,reason,notes,status,created_at_utc,updated_at_utc
FROM businessos_crm.credit_notes
""";

    private const string InsertSql = """
INSERT INTO businessos_crm.credit_notes(
 id,tenant_id,invoice_id,account_id,credit_note_number,issue_date,amount,reason,notes,status,created_at_utc,updated_at_utc)
VALUES(
 @id,@tenant,@invoice,@account,@number,@issue,@amount,@reason,@notes,@status,@created,@updated);
""";

    private const string UpdateSql = """
UPDATE businessos_crm.credit_notes SET
 issue_date=@issue,amount=@amount,reason=@reason,notes=@notes,status=@status,updated_at_utc=@updated
WHERE tenant_id=@tenant AND id=@id;
""";

    private static void AddParameters(NpgsqlCommand command, CreditNote note)
    {
        command.Parameters.AddWithValue("id", note.Id);
        command.Parameters.AddWithValue("tenant", note.TenantId);
        command.Parameters.AddWithValue("invoice", note.InvoiceId);
        command.Parameters.AddWithValue("account", note.AccountId);
        command.Parameters.AddWithValue("number", note.CreditNoteNumber);
        command.Parameters.AddWithValue("issue", NpgsqlDbType.Date, note.IssueDate);
        command.Parameters.AddWithValue("amount", note.Amount);
        command.Parameters.AddWithValue("reason", note.Reason);
        command.Parameters.Add("notes", NpgsqlDbType.Text).Value = note.Notes ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("status", (int)note.Status);
        command.Parameters.AddWithValue("created", note.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", note.UpdatedAtUtc);
    }

    private static CreditNote Read(NpgsqlDataReader reader) =>
        CreditNote.Restore(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(4), reader.GetGuid(2), reader.GetGuid(3),
            reader.GetFieldValue<DateOnly>(5), reader.GetDecimal(6), reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8), (CreditNoteStatus)reader.GetInt32(9),
            reader.GetFieldValue<DateTimeOffset>(10), reader.GetFieldValue<DateTimeOffset>(11));
}
