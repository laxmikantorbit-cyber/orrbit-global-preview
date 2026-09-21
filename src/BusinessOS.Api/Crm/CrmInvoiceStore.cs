using System.Text.Json;
using BusinessOS.Sales;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public interface ICrmInvoiceStore
{
    Task AddAsync(SalesInvoice invoice, CancellationToken cancellationToken = default);
    Task SaveAsync(SalesInvoice invoice, CancellationToken cancellationToken = default);
    Task<SalesInvoice?> GetAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SalesInvoice>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<string> NextInvoiceNumberAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SalesInvoicePayment>> ListPaymentsAsync(
        Guid tenantId, Guid invoiceId, CancellationToken cancellationToken = default);
    Task<(SalesInvoice Invoice, SalesInvoicePayment Payment)> RecordPaymentAsync(
        Guid tenantId,
        Guid invoiceId,
        decimal amount,
        string method,
        string? reference,
        string? notes,
        DateTimeOffset receivedAtUtc,
        Guid? receivedByUserId,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryCrmInvoiceStore : ICrmInvoiceStore
{
    private readonly Dictionary<Guid, SalesInvoice> _invoices = [];
    private readonly Dictionary<Guid, List<SalesInvoicePayment>> _payments = [];
    private readonly object _gate = new();

    public Task AddAsync(SalesInvoice invoice, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_invoices.ContainsKey(invoice.Id))
                throw new InvalidOperationException("Invoice already exists.");
            if (_invoices.Values.Any(x =>
                    x.TenantId == invoice.TenantId &&
                    x.InvoiceNumber.Equals(invoice.InvoiceNumber, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Invoice number already exists.");
            if (invoice.SourceDocumentId.HasValue &&
                _invoices.Values.Any(x =>
                    x.TenantId == invoice.TenantId &&
                    x.SourceDocumentId == invoice.SourceDocumentId))
                throw new InvalidOperationException("Source sales document is already invoiced.");

            _invoices.Add(invoice.Id, invoice);
            _payments[invoice.Id] = [];
        }

        return Task.CompletedTask;
    }

    public Task SaveAsync(SalesInvoice invoice, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_invoices.ContainsKey(invoice.Id))
                throw new InvalidOperationException("Invoice does not exist.");
            _invoices[invoice.Id] = invoice;
        }

        return Task.CompletedTask;
    }

    public Task<SalesInvoice?> GetAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var invoice = _invoices.GetValueOrDefault(invoiceId);
            return Task.FromResult(invoice?.TenantId == tenantId ? invoice : null);
        }
    }

    public Task<IReadOnlyList<SalesInvoice>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<SalesInvoice>>(
                _invoices.Values
                    .Where(x => x.TenantId == tenantId)
                    .OrderByDescending(x => x.IssueDate)
                    .ThenByDescending(x => x.InvoiceNumber)
                    .ToArray());
    }

    public Task<string> NextInvoiceNumberAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var next = _invoices.Values
                .Where(x => x.TenantId == tenantId)
                .Select(x => ParseSequence(x.InvoiceNumber, "INV-"))
                .DefaultIfEmpty(0)
                .Max() + 1;
            return Task.FromResult(FormatInvoiceNumber(next));
        }
    }

    public Task<IReadOnlyList<SalesInvoicePayment>> ListPaymentsAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var invoice = _invoices.GetValueOrDefault(invoiceId);
            if (invoice?.TenantId != tenantId)
                return Task.FromResult<IReadOnlyList<SalesInvoicePayment>>([]);

            return Task.FromResult<IReadOnlyList<SalesInvoicePayment>>(
                (_payments.GetValueOrDefault(invoiceId) ?? [])
                    .OrderByDescending(x => x.ReceivedAtUtc)
                    .ThenByDescending(x => x.PaymentNumber)
                    .ToArray());
        }
    }

    public Task<(SalesInvoice Invoice, SalesInvoicePayment Payment)> RecordPaymentAsync(
        Guid tenantId,
        Guid invoiceId,
        decimal amount,
        string method,
        string? reference,
        string? notes,
        DateTimeOffset receivedAtUtc,
        Guid? receivedByUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var invoice = _invoices.GetValueOrDefault(invoiceId);
            if (invoice?.TenantId != tenantId)
                throw new InvalidOperationException("Invoice not found.");

            var normalizedMethod = NormalizeMethod(method);
            invoice.RecordPayment(amount, DateOnly.FromDateTime(receivedAtUtc.UtcDateTime));

            var next = _payments.Values
                .SelectMany(x => x)
                .Where(x => x.TenantId == tenantId)
                .Select(x => ParseSequence(x.PaymentNumber, "PAY-"))
                .DefaultIfEmpty(0)
                .Max() + 1;

            var payment = new SalesInvoicePayment(
                Guid.NewGuid(),
                tenantId,
                invoiceId,
                FormatPaymentNumber(next),
                Money(amount),
                normalizedMethod,
                Clean(reference),
                Clean(notes),
                receivedAtUtc.ToUniversalTime(),
                receivedByUserId,
                DateTimeOffset.UtcNow);

            _payments.GetValueOrDefault(invoiceId)!.Add(payment);
            _invoices[invoiceId] = invoice;
            return Task.FromResult((invoice, payment));
        }
    }

    internal static string FormatInvoiceNumber(int sequence) => $"INV-{sequence:000000}";
    internal static string FormatPaymentNumber(int sequence) => $"PAY-{sequence:000000}";

    private static int ParseSequence(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(value[prefix.Length..], out var sequence)
            ? sequence
            : 0;

    internal static string NormalizeMethod(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Payment method is required.", nameof(value));
        return value.Trim();
    }

    internal static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    internal static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class PostgresCrmInvoiceStore : ICrmInvoiceStore
{
    private readonly CrmPostgresDatabase _db;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PostgresCrmInvoiceStore(CrmPostgresDatabase db) => _db = db;

    public async Task AddAsync(SalesInvoice invoice, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        const string sql = """
INSERT INTO businessos_crm.invoices(
 id,tenant_id,account_id,opportunity_id,source_document_id,invoice_number,subject,status,
 currency_code,issue_date,due_date,discount_percent,amount_paid,notes,terms,lines,
 created_at_utc,updated_at_utc)
VALUES(
 @id,@tenant,@account,@opportunity,@source,@number,@subject,@status,
 @currency,@issue,@due,@discount,@paid,@notes,@terms,CAST(@lines AS jsonb),
 @created,@updated);
""";

        await using var command = _db.DataSource.CreateCommand(sql);
        AddInvoiceParameters(command, invoice);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(SalesInvoice invoice, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(UpdateInvoiceSql);
        AddInvoiceParameters(command, invoice);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Invoice does not exist.");
    }

    public async Task<SalesInvoice?> GetAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(
            SelectInvoiceSql + " WHERE tenant_id=@tenant AND id=@id LIMIT 1");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", invoiceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadInvoice(reader) : null;
    }

    public async Task<IReadOnlyList<SalesInvoice>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(
            SelectInvoiceSql + " WHERE tenant_id=@tenant ORDER BY issue_date DESC, invoice_number DESC");
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var invoices = new List<SalesInvoice>();
        while (await reader.ReadAsync(cancellationToken))
            invoices.Add(ReadInvoice(reader));
        return invoices;
    }

    public async Task<string> NextInvoiceNumberAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
SELECT COALESCE(MAX(
 CASE WHEN invoice_number ~ '^INV-[0-9]{6}$'
 THEN RIGHT(invoice_number,6)::integer ELSE 0 END),0) + 1
FROM businessos_crm.invoices
WHERE tenant_id=@tenant;
""");
        command.Parameters.AddWithValue("tenant", tenantId);
        var next = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return InMemoryCrmInvoiceStore.FormatInvoiceNumber(next);
    }

    public async Task<IReadOnlyList<SalesInvoicePayment>> ListPaymentsAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
SELECT id,tenant_id,invoice_id,payment_number,amount,method,reference,notes,
       received_at_utc,received_by_user_id,created_at_utc
FROM businessos_crm.invoice_payments
WHERE tenant_id=@tenant AND invoice_id=@invoice
ORDER BY received_at_utc DESC, payment_number DESC;
""");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("invoice", invoiceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var payments = new List<SalesInvoicePayment>();
        while (await reader.ReadAsync(cancellationToken))
            payments.Add(ReadPayment(reader));
        return payments;
    }

    public async Task<(SalesInvoice Invoice, SalesInvoicePayment Payment)> RecordPaymentAsync(
        Guid tenantId,
        Guid invoiceId,
        decimal amount,
        string method,
        string? reference,
        string? notes,
        DateTimeOffset receivedAtUtc,
        Guid? receivedByUserId,
        CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        var normalizedMethod = InMemoryCrmInvoiceStore.NormalizeMethod(method);

        await using var connection = await _db.DataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        SalesInvoice invoice;
        await using (var select = new NpgsqlCommand(
            SelectInvoiceSql + " WHERE tenant_id=@tenant AND id=@id FOR UPDATE", connection, transaction))
        {
            select.Parameters.AddWithValue("tenant", tenantId);
            select.Parameters.AddWithValue("id", invoiceId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Invoice not found.");
            invoice = ReadInvoice(reader);
        }

        invoice.RecordPayment(
            amount,
            DateOnly.FromDateTime(receivedAtUtc.UtcDateTime));

        int next;
        await using (var sequence = new NpgsqlCommand("""
SELECT COALESCE(MAX(
 CASE WHEN payment_number ~ '^PAY-[0-9]{6}$'
 THEN RIGHT(payment_number,6)::integer ELSE 0 END),0) + 1
FROM businessos_crm.invoice_payments
WHERE tenant_id=@tenant;
""", connection, transaction))
        {
            sequence.Parameters.AddWithValue("tenant", tenantId);
            next = Convert.ToInt32(await sequence.ExecuteScalarAsync(cancellationToken));
        }

        var payment = new SalesInvoicePayment(
            Guid.NewGuid(),
            tenantId,
            invoiceId,
            InMemoryCrmInvoiceStore.FormatPaymentNumber(next),
            InMemoryCrmInvoiceStore.Money(amount),
            normalizedMethod,
            InMemoryCrmInvoiceStore.Clean(reference),
            InMemoryCrmInvoiceStore.Clean(notes),
            receivedAtUtc.ToUniversalTime(),
            receivedByUserId,
            DateTimeOffset.UtcNow);

        await using (var insert = new NpgsqlCommand("""
INSERT INTO businessos_crm.invoice_payments(
 id,tenant_id,invoice_id,payment_number,amount,method,reference,notes,
 received_at_utc,received_by_user_id,created_at_utc)
VALUES(
 @id,@tenant,@invoice,@number,@amount,@method,@reference,@notes,
 @received,@receivedBy,@created);
""", connection, transaction))
        {
            AddPaymentParameters(insert, payment);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var update = new NpgsqlCommand(UpdateInvoiceSql, connection, transaction))
        {
            AddInvoiceParameters(update, invoice);
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
                throw new InvalidOperationException("Invoice disappeared while recording payment.");
        }

        await transaction.CommitAsync(cancellationToken);
        return (invoice, payment);
    }

    private const string SelectInvoiceSql = """
SELECT id,tenant_id,account_id,opportunity_id,source_document_id,invoice_number,subject,status,
       currency_code,issue_date,due_date,discount_percent,amount_paid,notes,terms,lines::text,
       created_at_utc,updated_at_utc
FROM businessos_crm.invoices
""";

    private const string UpdateInvoiceSql = """
UPDATE businessos_crm.invoices SET
 account_id=@account,
 opportunity_id=@opportunity,
 subject=@subject,
 status=@status,
 currency_code=@currency,
 issue_date=@issue,
 due_date=@due,
 discount_percent=@discount,
 amount_paid=@paid,
 notes=@notes,
 terms=@terms,
 lines=CAST(@lines AS jsonb),
 updated_at_utc=@updated
WHERE tenant_id=@tenant AND id=@id;
""";

    private static void AddInvoiceParameters(NpgsqlCommand command, SalesInvoice invoice)
    {
        command.Parameters.AddWithValue("id", invoice.Id);
        command.Parameters.AddWithValue("tenant", invoice.TenantId);
        command.Parameters.AddWithValue("account", invoice.AccountId);
        Nullable(command, "opportunity", NpgsqlDbType.Uuid, invoice.OpportunityId);
        Nullable(command, "source", NpgsqlDbType.Uuid, invoice.SourceDocumentId);
        command.Parameters.AddWithValue("number", invoice.InvoiceNumber);
        command.Parameters.AddWithValue("subject", invoice.Subject);
        command.Parameters.AddWithValue("status", (int)invoice.Status);
        command.Parameters.AddWithValue("currency", invoice.CurrencyCode);
        command.Parameters.AddWithValue("issue", NpgsqlDbType.Date, invoice.IssueDate);
        command.Parameters.AddWithValue("due", NpgsqlDbType.Date, invoice.DueDate);
        command.Parameters.AddWithValue("discount", invoice.DiscountPercent);
        command.Parameters.AddWithValue("paid", invoice.AmountPaid);
        Nullable(command, "notes", NpgsqlDbType.Text, invoice.Notes);
        Nullable(command, "terms", NpgsqlDbType.Text, invoice.Terms);
        command.Parameters.AddWithValue("lines", JsonSerializer.Serialize(invoice.Lines, JsonOptions));
        command.Parameters.AddWithValue("created", invoice.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", invoice.UpdatedAtUtc);
    }

    private static void AddPaymentParameters(NpgsqlCommand command, SalesInvoicePayment payment)
    {
        command.Parameters.AddWithValue("id", payment.Id);
        command.Parameters.AddWithValue("tenant", payment.TenantId);
        command.Parameters.AddWithValue("invoice", payment.InvoiceId);
        command.Parameters.AddWithValue("number", payment.PaymentNumber);
        command.Parameters.AddWithValue("amount", payment.Amount);
        command.Parameters.AddWithValue("method", payment.Method);
        Nullable(command, "reference", NpgsqlDbType.Text, payment.Reference);
        Nullable(command, "notes", NpgsqlDbType.Text, payment.Notes);
        command.Parameters.AddWithValue("received", payment.ReceivedAtUtc);
        Nullable(command, "receivedBy", NpgsqlDbType.Uuid, payment.ReceivedByUserId);
        command.Parameters.AddWithValue("created", payment.CreatedAtUtc);
    }

    private static SalesInvoice ReadInvoice(NpgsqlDataReader reader)
    {
        var lines = JsonSerializer.Deserialize<SalesDocumentLine[]>(reader.GetString(15), JsonOptions) ?? [];
        return SalesInvoice.Restore(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(5),
            reader.GetGuid(2),
            reader.GetString(6),
            lines,
            reader.GetString(8),
            reader.GetFieldValue<DateOnly>(9),
            reader.GetFieldValue<DateOnly>(10),
            reader.GetDecimal(11),
            reader.GetDecimal(12),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            (SalesInvoiceStatus)reader.GetInt32(7),
            reader.GetFieldValue<DateTimeOffset>(16),
            reader.GetFieldValue<DateTimeOffset>(17));
    }

    private static SalesInvoicePayment ReadPayment(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetDecimal(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.GetFieldValue<DateTimeOffset>(10));

    private static void Nullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
}
