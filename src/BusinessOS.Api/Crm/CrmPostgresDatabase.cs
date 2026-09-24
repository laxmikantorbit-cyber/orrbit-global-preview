using Npgsql;

namespace BusinessOS.Api.Crm;

public sealed class CrmPostgresDatabase : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly NpgsqlDataSource _bootstrapDataSource;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string? _runtimeRole;
    private readonly bool _allowSchemaBootstrap;
    private bool _ready;

    public CrmPostgresDatabase(
        string connectionString,
        string? runtimeRole = null,
        bool allowSchemaBootstrap = false)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("CRM connection string is required.", nameof(connectionString));
        _runtimeRole = string.IsNullOrWhiteSpace(runtimeRole) ? null : runtimeRole.Trim();
        _bootstrapDataSource = NpgsqlDataSource.Create(connectionString);
        _dataSource = NpgsqlDataSource.Create(
            BuildRuntimeConnectionString(connectionString, _runtimeRole));
        _allowSchemaBootstrap = allowSchemaBootstrap;
    }

    public NpgsqlDataSource DataSource => _dataSource;

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (_ready) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_ready) return;
            if (_allowSchemaBootstrap)
            {
                // SchemaSql is intentionally idempotent. Running it once per fresh process
                // applies additive migrations (for example new columns) even when the base
                // CRM schema already exists and runtime validation would otherwise succeed.
                await BootstrapAsync(cancellationToken);
            }

            await ValidateRuntimeAccessAsync(cancellationToken);
            _ready = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _bootstrapDataSource.DisposeAsync();
    }

    private async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _bootstrapDataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ResetAsync(connection, cancellationToken);
        await AssumeExistingSchemaOwnerRoleAsync(connection, cancellationToken);
        await using (var command = new NpgsqlCommand(SchemaSql, connection))
            await command.ExecuteNonQueryAsync(cancellationToken);

        if (_runtimeRole is null) return;
        var role = BusinessOS.Api.PostgresRuntimeRole.QuoteIdentifier(_runtimeRole);
        var grants = RuntimeGrantSql.Replace("__RUNTIME_ROLE__", role, StringComparison.Ordinal);
        await using var grantCommand = new NpgsqlCommand(grants, connection);
        await grantCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AssumeExistingSchemaOwnerRoleAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT owner_role.rolname,
       owner_role.rolname = current_user,
       pg_has_role(session_user, owner_role.oid, 'SET')
FROM pg_namespace schema_info
JOIN pg_roles owner_role ON owner_role.oid = schema_info.nspowner
WHERE schema_info.nspname = 'businessos_crm';
""";
        await using var probe = new NpgsqlCommand(sql, connection);
        await using var reader = await probe.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return;
        var ownerRole = reader.GetString(0);
        var alreadyOwner = reader.GetBoolean(1);
        var canSetOwnerRole = reader.GetBoolean(2);
        await reader.DisposeAsync();

        if (alreadyOwner) return;
        if (!canSetOwnerRole)
            throw new InvalidOperationException(
                $"CRM schema is owned by '{ownerRole}' and the bootstrap login cannot assume that role.");

        await using var setRole = new NpgsqlCommand(
            "SET ROLE " + BusinessOS.Api.PostgresRuntimeRole.QuoteIdentifier(ownerRole), connection);
        await setRole.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ValidateRuntimeAccessAsync(CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(RuntimeValidationSql);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static string BuildRuntimeConnectionString(string connectionString, string? runtimeRole)
    {
        if (runtimeRole is null) return connectionString;
        if (!(char.IsLetter(runtimeRole[0]) || runtimeRole[0] == '_') ||
            runtimeRole.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '$')))
            throw new ArgumentException("Postgres runtime role contains unsupported characters.", nameof(runtimeRole));

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var roleOption = "-c role=" + runtimeRole;
        builder.Options = string.IsNullOrWhiteSpace(builder.Options)
            ? roleOption
            : builder.Options + " " + roleOption;
        return builder.ConnectionString;
    }

    private const string RuntimeValidationSql =
        "SELECT count(*) FROM businessos_crm.team_members WHERE false;";

    private const string RuntimeGrantSql = """
REVOKE ALL ON SCHEMA businessos_crm FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA businessos_crm FROM PUBLIC;
GRANT USAGE ON SCHEMA businessos_crm TO __RUNTIME_ROLE__;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA businessos_crm TO __RUNTIME_ROLE__;
ALTER DEFAULT PRIVILEGES IN SCHEMA businessos_crm
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO __RUNTIME_ROLE__;
""";

    private const string SchemaSql = """
CREATE SCHEMA IF NOT EXISTS businessos_crm;
CREATE TABLE IF NOT EXISTS businessos_crm.leads (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    organisation_id uuid NOT NULL,
    title text NOT NULL,
    attribution jsonb NOT NULL,
    contact_name text NULL,
    mobile_number text NULL,
    email text NULL,
    product_interest text NULL,
    notes text NULL,
    status integer NOT NULL,
    priority integer NOT NULL,
    unqualified_reason text NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    last_contact_at_utc timestamptz NULL,
    next_follow_up_at_utc timestamptz NULL,
    tags jsonb NOT NULL DEFAULT '[]'::jsonb,
    estimated_value numeric(18,2) NULL
);
ALTER TABLE businessos_crm.leads ADD COLUMN IF NOT EXISTS estimated_value numeric(18,2) NULL;
CREATE INDEX IF NOT EXISTS ix_businessos_crm_leads_tenant ON businessos_crm.leads(tenant_id, created_at_utc DESC);
CREATE TABLE IF NOT EXISTS businessos_crm.activities (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    lead_id uuid NOT NULL,
    type integer NOT NULL,
    summary text NOT NULL,
    details text NULL,
    actor_user_id uuid NULL,
    occurred_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_activities_lead ON businessos_crm.activities(tenant_id, lead_id, occurred_at_utc DESC);
CREATE TABLE IF NOT EXISTS businessos_crm.follow_ups (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    lead_id uuid NOT NULL,
    due_at_utc timestamptz NOT NULL,
    channel integer NOT NULL,
    purpose text NOT NULL,
    owner_user_id uuid NULL,
    status integer NOT NULL,
    outcome text NULL,
    created_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_followups_tenant ON businessos_crm.follow_ups(tenant_id, status, due_at_utc);
CREATE TABLE IF NOT EXISTS businessos_crm.tasks (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    lead_id uuid NULL,
    title text NOT NULL,
    details text NULL,
    due_at_utc timestamptz NULL,
    priority integer NOT NULL,
    assignee_user_id uuid NULL,
    status integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_tasks_tenant ON businessos_crm.tasks(tenant_id, status, due_at_utc);
CREATE TABLE IF NOT EXISTS businessos_crm.accounts (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    legal_name text NULL,
    gstin text NULL,
    display_code text NULL,
    status integer NOT NULL,
    roles jsonb NOT NULL DEFAULT '[]'::jsonb,
    contacts jsonb NOT NULL DEFAULT '[]'::jsonb,
    addresses jsonb NOT NULL DEFAULT '[]'::jsonb,
    groups jsonb NOT NULL DEFAULT '[]'::jsonb
);
ALTER TABLE businessos_crm.accounts ADD COLUMN IF NOT EXISTS groups jsonb NOT NULL DEFAULT '[]'::jsonb;
CREATE INDEX IF NOT EXISTS ix_businessos_crm_accounts_tenant ON businessos_crm.accounts(tenant_id, name);
CREATE TABLE IF NOT EXISTS businessos_crm.opportunities (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    organisation_id uuid NOT NULL,
    originating_lead_id uuid NULL,
    owner_user_id uuid NULL,
    title text NOT NULL,
    stage integer NOT NULL,
    estimated_value numeric(18,2) NOT NULL,
    currency_code varchar(3) NOT NULL,
    probability_percent integer NOT NULL,
    expected_close_date date NULL,
    loss_reason text NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_opportunities_tenant ON businessos_crm.opportunities(tenant_id, stage);
CREATE TABLE IF NOT EXISTS businessos_crm.sales_documents (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    account_id uuid NOT NULL,
    opportunity_id uuid NULL,
    kind integer NOT NULL,
    document_number text NOT NULL,
    subject text NOT NULL,
    status integer NOT NULL,
    currency_code varchar(3) NOT NULL,
    issue_date date NOT NULL,
    expiry_date date NULL,
    discount_percent numeric(5,2) NOT NULL DEFAULT 0,
    notes text NULL,
    terms text NULL,
    lines jsonb NOT NULL DEFAULT '[]'::jsonb,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_sales_document_number
    ON businessos_crm.sales_documents(tenant_id, kind, document_number);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_sales_documents_tenant
    ON businessos_crm.sales_documents(tenant_id, kind, status, issue_date DESC);

CREATE TABLE IF NOT EXISTS businessos_crm.invoices (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    account_id uuid NOT NULL,
    opportunity_id uuid NULL,
    source_document_id uuid NULL,
    invoice_number text NOT NULL,
    subject text NOT NULL,
    status integer NOT NULL,
    currency_code varchar(3) NOT NULL,
    issue_date date NOT NULL,
    due_date date NOT NULL,
    discount_percent numeric(5,2) NOT NULL DEFAULT 0,
    amount_paid numeric(18,2) NOT NULL DEFAULT 0,
    amount_credited numeric(18,2) NOT NULL DEFAULT 0,
    notes text NULL,
    terms text NULL,
    lines jsonb NOT NULL DEFAULT '[]'::jsonb,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_invoice_number
    ON businessos_crm.invoices(tenant_id, invoice_number);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_invoice_source_document
    ON businessos_crm.invoices(tenant_id, source_document_id)
    WHERE source_document_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_businessos_crm_invoices_tenant
    ON businessos_crm.invoices(tenant_id, status, due_date DESC);

CREATE TABLE IF NOT EXISTS businessos_crm.invoice_payments (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    invoice_id uuid NOT NULL,
    payment_number text NOT NULL,
    amount numeric(18,2) NOT NULL,
    method text NOT NULL,
    reference text NULL,
    notes text NULL,
    received_at_utc timestamptz NOT NULL,
    received_by_user_id uuid NULL,
    created_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_invoice_payment_number
    ON businessos_crm.invoice_payments(tenant_id, payment_number);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_invoice_payments_invoice
    ON businessos_crm.invoice_payments(tenant_id, invoice_id, received_at_utc DESC);

ALTER TABLE businessos_crm.invoices
    ADD COLUMN IF NOT EXISTS amount_credited numeric(18,2) NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS businessos_crm.sales_items (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    code text NOT NULL,
    name text NOT NULL,
    description text NULL,
    default_rate numeric(18,2) NOT NULL DEFAULT 0,
    default_tax_percent numeric(5,2) NOT NULL DEFAULT 0,
    status integer NOT NULL,
    catalog_product_id uuid NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_sales_items_code
    ON businessos_crm.sales_items(tenant_id, upper(code));
CREATE INDEX IF NOT EXISTS ix_businessos_crm_sales_items_tenant
    ON businessos_crm.sales_items(tenant_id, status, name);

CREATE TABLE IF NOT EXISTS businessos_crm.credit_notes (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    invoice_id uuid NOT NULL,
    account_id uuid NOT NULL,
    credit_note_number text NOT NULL,
    issue_date date NOT NULL,
    amount numeric(18,2) NOT NULL,
    reason text NOT NULL,
    notes text NULL,
    status integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_credit_note_number
    ON businessos_crm.credit_notes(tenant_id, credit_note_number);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_credit_notes_invoice
    ON businessos_crm.credit_notes(tenant_id, invoice_id, issue_date DESC);

CREATE TABLE IF NOT EXISTS businessos_crm.business_records (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    module integer NOT NULL,
    account_id uuid NULL,
    title text NOT NULL,
    status text NOT NULL,
    amount numeric(18,2) NULL,
    category text NULL,
    priority text NULL,
    start_date date NULL,
    due_date date NULL,
    owner_user_id uuid NULL,
    description text NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_business_records_module
    ON businessos_crm.business_records(tenant_id, module, status, updated_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_business_records_account
    ON businessos_crm.business_records(tenant_id, account_id, module);

CREATE TABLE IF NOT EXISTS businessos_crm.estimate_requests (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    source text NOT NULL,
    requirement text NOT NULL,
    contact_name text NULL,
    mobile_number text NULL,
    email text NULL,
    expected_value numeric(18,2) NULL,
    assigned_user_id uuid NULL,
    business_company text NULL,
    notes text NULL,
    status integer NOT NULL,
    converted_lead_id uuid NULL,
    converted_estimate_id uuid NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
ALTER TABLE businessos_crm.estimate_requests ADD COLUMN IF NOT EXISTS business_company text NULL;
ALTER TABLE businessos_crm.estimate_requests ADD COLUMN IF NOT EXISTS notes text NULL;
CREATE INDEX IF NOT EXISTS ix_businessos_crm_estimate_requests_status
    ON businessos_crm.estimate_requests(tenant_id, status, updated_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_estimate_requests_contact
    ON businessos_crm.estimate_requests(tenant_id, lower(email), mobile_number);

CREATE TABLE IF NOT EXISTS businessos_crm.knowledge_categories (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    name text NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    active boolean NOT NULL DEFAULT true
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_knowledge_category_name
    ON businessos_crm.knowledge_categories(tenant_id, lower(name));

CREATE TABLE IF NOT EXISTS businessos_crm.knowledge_articles (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    title text NOT NULL,
    content text NOT NULL,
    category_id uuid NOT NULL,
    owner_user_id uuid NOT NULL,
    visibility integer NOT NULL,
    status integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_knowledge_articles_status
    ON businessos_crm.knowledge_articles(tenant_id, status, updated_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_knowledge_articles_category
    ON businessos_crm.knowledge_articles(tenant_id, category_id, status);

CREATE TABLE IF NOT EXISTS businessos_crm.media_assets (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    file_name text NOT NULL,
    mime_type text NOT NULL,
    size_bytes bigint NOT NULL,
    purpose text NOT NULL,
    storage_reference text NOT NULL,
    uploaded_by_user_id uuid NOT NULL,
    entity_type text NULL,
    entity_id uuid NULL,
    active boolean NOT NULL DEFAULT true,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_media_assets_active
    ON businessos_crm.media_assets(tenant_id, active, updated_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_businessos_crm_media_assets_entity
    ON businessos_crm.media_assets(tenant_id, entity_type, entity_id)
    WHERE entity_id IS NOT NULL;
CREATE TABLE IF NOT EXISTS businessos_crm.team_members (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    display_name text NOT NULL,
    email text NOT NULL,
    mobile_number text NULL,
    role integer NOT NULL,
    active boolean NOT NULL,
    created_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_businessos_crm_team_email ON businessos_crm.team_members(tenant_id, lower(email));
CREATE INDEX IF NOT EXISTS ix_businessos_crm_team_tenant ON businessos_crm.team_members(tenant_id, active, display_name);
""";
}
