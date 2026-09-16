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
            try
            {
                await ValidateRuntimeAccessAsync(cancellationToken);
            }
            catch (PostgresException ex) when (
                _allowSchemaBootstrap && ex.SqlState is "42P01" or "3F000")
            {
                await BootstrapAsync(cancellationToken);
                await ValidateRuntimeAccessAsync(cancellationToken);
            }
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
        await using (var command = new NpgsqlCommand(SchemaSql, connection))
            await command.ExecuteNonQueryAsync(cancellationToken);

        if (_runtimeRole is null) return;
        var role = BusinessOS.Api.PostgresRuntimeRole.QuoteIdentifier(_runtimeRole);
        var grants = RuntimeGrantSql.Replace("__RUNTIME_ROLE__", role, StringComparison.Ordinal);
        await using var grantCommand = new NpgsqlCommand(grants, connection);
        await grantCommand.ExecuteNonQueryAsync(cancellationToken);
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
    tags jsonb NOT NULL DEFAULT '[]'::jsonb
);
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
    addresses jsonb NOT NULL DEFAULT '[]'::jsonb
);
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
