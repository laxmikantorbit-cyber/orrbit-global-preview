using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace BusinessOS.Api.Billing;

public sealed class PostgresBillingStore : IBillingStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;
    private readonly string? _runtimeRole;
    private readonly bool _allowSchemaBootstrap;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private volatile bool _schemaReady;

    public PostgresBillingStore(
        string connectionString,
        string? runtimeRole,
        bool allowSchemaBootstrap)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _runtimeRole = runtimeRole;
        _allowSchemaBootstrap = allowSchemaBootstrap;
    }

    public async Task<BillingInvoice> EnsureInvoiceAsync(
        BillingInvoiceDraft draft,
        string invoicePrefix,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var existing = await FindInvoiceByOrderAsync(
            draft.TenantId, draft.OrderId, cancellationToken);
        if (existing is not null) return existing;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(
            connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, draft.TenantId, cancellationToken);

        var sequence = await NextSequenceAsync(
            connection, transaction, draft.TenantId, draft.FinancialYear, cancellationToken);
        var invoice = InMemoryBillingStore.ToInvoice(
            draft,
            Guid.NewGuid(),
            InMemoryBillingStore.FormatInvoiceNumber(
                invoicePrefix, draft.FinancialYear, sequence));

        const string sql = """
            INSERT INTO billing_invoices(
                id,tenant_id,organisation_id,order_id,subscription_id,
                invoice_number,issued_at_utc,document_json)
            VALUES(@id,@tenant_id,@organisation_id,@order_id,@subscription_id,
                   @invoice_number,@issued_at_utc,@document_json::jsonb)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", invoice.Id);
        command.Parameters.AddWithValue("tenant_id", invoice.TenantId);
        command.Parameters.AddWithValue("organisation_id", invoice.OrganisationId);
        command.Parameters.AddWithValue("order_id", invoice.OrderId);
        command.Parameters.AddWithValue("subscription_id", (object?)invoice.SubscriptionId ?? DBNull.Value);
        command.Parameters.AddWithValue("invoice_number", invoice.InvoiceNumber);
        command.Parameters.AddWithValue("issued_at_utc", invoice.IssuedAtUtc);
        command.Parameters.AddWithValue("document_json", JsonSerializer.Serialize(invoice, JsonOptions));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return invoice;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await FindInvoiceByOrderAsync(
                draft.TenantId, draft.OrderId, cancellationToken)
                ?? throw new InvalidOperationException("Invoice uniqueness conflict could not be resolved.", ex);
        }
    }

    public Task<BillingInvoice?> FindInvoiceAsync(
        Guid tenantId, Guid invoiceId, CancellationToken cancellationToken = default) =>
        FindOneAsync(tenantId, "id", invoiceId, cancellationToken);

    public Task<BillingInvoice?> FindInvoiceByOrderAsync(
        Guid tenantId, Guid orderId, CancellationToken cancellationToken = default) =>
        FindOneAsync(tenantId, "order_id", orderId, cancellationToken);

    public async Task<IReadOnlyList<BillingInvoice>> ListInvoicesAsync(
        Guid tenantId,
        Guid? organisationId,
        Guid? subscriptionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(
            connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        const string sql = """
            SELECT document_json
            FROM billing_invoices
            WHERE tenant_id=@tenant_id
              AND (@organisation_id IS NULL OR organisation_id=@organisation_id)
              AND (@subscription_id IS NULL OR subscription_id=@subscription_id)
            ORDER BY issued_at_utc DESC,invoice_number DESC
            LIMIT @take
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("organisation_id", (object?)organisationId ?? DBNull.Value);
        command.Parameters.AddWithValue("subscription_id", (object?)subscriptionId ?? DBNull.Value);
        command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 200));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<BillingInvoice>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(Deserialize(reader.GetString(0)));
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    private async Task<BillingInvoice?> FindOneAsync(
        Guid tenantId,
        string column,
        Guid value,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        if (column is not ("id" or "order_id"))
            throw new ArgumentOutOfRangeException(nameof(column));
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(
            connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        var sql = $"SELECT document_json FROM billing_invoices WHERE tenant_id=@tenant_id AND {column}=@value LIMIT 1";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("value", value);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result is null || result is DBNull ? null : Deserialize((string)result);
    }

    private static async Task<int> NextSequenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        string financialYear,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO billing_invoice_sequences(tenant_id,financial_year,last_number)
            VALUES(@tenant_id,@financial_year,1)
            ON CONFLICT (tenant_id,financial_year)
            DO UPDATE SET last_number=billing_invoice_sequences.last_number+1
            RETURNING last_number
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("financial_year", financialYear);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private static async Task SetTenantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant_id, true)",
            connection,
            transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady) return;
        if (!_allowSchemaBootstrap)
        {
            _schemaReady = true;
            return;
        }
        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady) return;
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            const string ddl = """
                CREATE TABLE IF NOT EXISTS billing_invoice_sequences(
                    tenant_id uuid NOT NULL REFERENCES tenants(id),
                    financial_year text NOT NULL,
                    last_number integer NOT NULL CHECK(last_number>0),
                    PRIMARY KEY(tenant_id,financial_year));
                CREATE TABLE IF NOT EXISTS billing_invoices(
                    id uuid PRIMARY KEY,
                    tenant_id uuid NOT NULL REFERENCES tenants(id),
                    organisation_id uuid NOT NULL,
                    order_id uuid NOT NULL,
                    subscription_id uuid NULL,
                    invoice_number text NOT NULL,
                    issued_at_utc timestamptz NOT NULL,
                    document_json jsonb NOT NULL,
                    UNIQUE(tenant_id,order_id),
                    UNIQUE(tenant_id,invoice_number),
                    FOREIGN KEY(tenant_id,organisation_id) REFERENCES organisations(tenant_id,id),
                    FOREIGN KEY(tenant_id,order_id) REFERENCES commerce_orders(tenant_id,id),
                    FOREIGN KEY(tenant_id,subscription_id) REFERENCES commerce_subscriptions(tenant_id,id));
                ALTER TABLE billing_invoice_sequences ENABLE ROW LEVEL SECURITY;
                ALTER TABLE billing_invoice_sequences FORCE ROW LEVEL SECURITY;
                ALTER TABLE billing_invoices ENABLE ROW LEVEL SECURITY;
                ALTER TABLE billing_invoices FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS billing_sequence_tenant_policy ON billing_invoice_sequences;
                CREATE POLICY billing_sequence_tenant_policy ON billing_invoice_sequences
                  USING (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid)
                  WITH CHECK (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid);
                DROP POLICY IF EXISTS billing_invoice_tenant_policy ON billing_invoices;
                CREATE POLICY billing_invoice_tenant_policy ON billing_invoices
                  USING (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid)
                  WITH CHECK (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid);
                """;
            await using var command = new NpgsqlCommand(ddl, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(_runtimeRole))
            {
                if (!Regex.IsMatch(_runtimeRole, "^[A-Za-z_][A-Za-z0-9_]*$"))
                    throw new InvalidOperationException("Invalid PostgreSQL runtime role.");
                var grantSql = $"GRANT SELECT,INSERT,UPDATE,DELETE ON billing_invoice_sequences,billing_invoices TO {_runtimeRole}";
                await using var grant = new NpgsqlCommand(grantSql, connection);
                await grant.ExecuteNonQueryAsync(cancellationToken);
            }
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private static BillingInvoice Deserialize(string json) =>
        JsonSerializer.Deserialize<BillingInvoice>(json, JsonOptions)
        ?? throw new InvalidOperationException("Invalid billing invoice document.");

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
