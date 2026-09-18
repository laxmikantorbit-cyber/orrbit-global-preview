using BusinessOS.Customers;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Customers;

public sealed class PostgresOrganisationRepository : IOrganisationRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string? _runtimeRole;

    public PostgresOrganisationRepository(string connectionString, string? runtimeRole)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _runtimeRole = runtimeRole;
    }

    public async Task AddAsync(Organisation organisation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organisation);
        await SaveAsync(organisation, true, cancellationToken);
    }

    public async Task UpdateAsync(Organisation organisation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organisation);
        await SaveAsync(organisation, false, cancellationToken);
    }
    public async Task<Organisation?> GetAsync(
        Guid tenantId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        var items = await LoadAsync(tenantId, organisationId, null, cancellationToken);
        return items.SingleOrDefault();
    }

    public Task<IReadOnlyList<Organisation>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        LoadAsync(tenantId, null, null, cancellationToken);

    public Task<IReadOnlyList<Organisation>> ListByRoleAsync(
        Guid tenantId,
        OrganisationRole role,
        CancellationToken cancellationToken = default) =>
        LoadAsync(tenantId, null, role, cancellationToken);

    private async Task SaveAsync(
        Organisation organisation,
        bool insertOnly,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, organisation.TenantId, cancellationToken);
        var sql = insertOnly
            ? """
              INSERT INTO organisations(id,tenant_id,name,legal_name,gstin,display_code,status)
              VALUES(@id,@tenant_id,@name,@legal_name,@gstin,@display_code,@status)
              """
            : """
              UPDATE organisations
              SET name=@name,legal_name=@legal_name,gstin=@gstin,display_code=@display_code,status=@status
              WHERE tenant_id=@tenant_id AND id=@id
              """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("id", organisation.Id);
            command.Parameters.AddWithValue("tenant_id", organisation.TenantId);
            command.Parameters.AddWithValue("name", organisation.Name);
            AddNullableText(command, "legal_name", organisation.LegalName);
            AddNullableText(command, "gstin", organisation.Gstin);
            AddNullableText(command, "display_code", organisation.DisplayCode);
            command.Parameters.AddWithValue("status", (short)organisation.Status);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (!insertOnly && affected == 0)
                throw new InvalidOperationException("Organisation does not exist.");
        }

        if (!insertOnly)
            await DeleteChildrenAsync(connection, transaction, organisation, cancellationToken);
        await InsertChildrenAsync(connection, transaction, organisation, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    private async Task<IReadOnlyList<Organisation>> LoadAsync(
        Guid tenantId,
        Guid? organisationId,
        OrganisationRole? role,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        const string sql = """
            SELECT o.id,o.name,o.legal_name,o.gstin,o.display_code,o.status
            FROM organisations o
            WHERE o.tenant_id=@tenant_id
              AND (@organisation_id IS NULL OR o.id=@organisation_id)
              AND (@role_code IS NULL OR EXISTS(
                  SELECT 1 FROM organisation_roles r
                  WHERE r.tenant_id=o.tenant_id
                    AND r.organisation_id=o.id
                    AND r.role_code=@role_code))
            ORDER BY o.name,o.id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableUuid(command, "organisation_id", organisationId);
        AddNullableSmallint(command, "role_code", role is null ? null : (short)role.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var organisations = new List<Organisation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            organisations.Add(new Organisation(
                reader.GetGuid(0),
                tenantId,
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                (OrganisationStatus)reader.GetInt16(5)));
        }
        await reader.DisposeAsync();

        foreach (var organisation in organisations)
        {
            await LoadRolesAsync(connection, transaction, organisation, cancellationToken);
            await LoadContactsAsync(connection, transaction, organisation, cancellationToken);
            await LoadAddressesAsync(connection, transaction, organisation, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return organisations;
    }

    private static async Task LoadRolesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Organisation organisation,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT role_code FROM organisation_roles WHERE tenant_id=@tenant_id AND organisation_id=@organisation_id ORDER BY role_code";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", organisation.TenantId);
        command.Parameters.AddWithValue("organisation_id", organisation.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            organisation.AddRole((OrganisationRole)reader.GetInt16(0));
    }

    private static async Task LoadContactsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Organisation organisation,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id,name,email,phone,is_primary,designation
            FROM organisation_contacts
            WHERE tenant_id=@tenant_id AND organisation_id=@organisation_id
            ORDER BY is_primary DESC,name,id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", organisation.TenantId);
        command.Parameters.AddWithValue("organisation_id", organisation.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            organisation.AddContact(new ContactPerson(
                reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
    }

    private static async Task LoadAddressesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Organisation organisation,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id,line1,line2,city,state,postal_code,country_code,is_primary,state_code
            FROM organisation_addresses
            WHERE tenant_id=@tenant_id AND organisation_id=@organisation_id
            ORDER BY is_primary DESC,id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", organisation.TenantId);
        command.Parameters.AddWithValue("organisation_id", organisation.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            organisation.AddAddress(new OrganisationAddress(
                reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetString(6), reader.GetBoolean(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
    }
    private static async Task DeleteChildrenAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Organisation organisation,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM organisation_roles WHERE tenant_id=@tenant_id AND organisation_id=@organisation_id;
            DELETE FROM organisation_contacts WHERE tenant_id=@tenant_id AND organisation_id=@organisation_id;
            DELETE FROM organisation_addresses WHERE tenant_id=@tenant_id AND organisation_id=@organisation_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", organisation.TenantId);
        command.Parameters.AddWithValue("organisation_id", organisation.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertChildrenAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Organisation organisation,
        CancellationToken cancellationToken)
    {
        foreach (var role in organisation.Roles)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO organisation_roles(tenant_id,organisation_id,role_code) VALUES(@tenant_id,@organisation_id,@role_code)",
                connection, transaction);
            command.Parameters.AddWithValue("tenant_id", organisation.TenantId);
            command.Parameters.AddWithValue("organisation_id", organisation.Id);
            command.Parameters.AddWithValue("role_code", (short)role);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var contact in organisation.Contacts)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO organisation_contacts
                (id,tenant_id,organisation_id,name,email,phone,is_primary,designation)
                VALUES(@id,@tenant_id,@organisation_id,@name,@email,@phone,@is_primary,@designation)
                """, connection, transaction);
            command.Parameters.AddWithValue("id", contact.Id);
            command.Parameters.AddWithValue("tenant_id", organisation.TenantId);
            command.Parameters.AddWithValue("organisation_id", organisation.Id);
            command.Parameters.AddWithValue("name", contact.Name);
            AddNullableText(command, "email", contact.Email);
            AddNullableText(command, "phone", contact.Phone);
            command.Parameters.AddWithValue("is_primary", contact.IsPrimary);
            AddNullableText(command, "designation", contact.Designation);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var address in organisation.Addresses)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO organisation_addresses
                (id,tenant_id,organisation_id,line1,line2,city,state,postal_code,country_code,is_primary,state_code)
                VALUES(@id,@tenant_id,@organisation_id,@line1,@line2,@city,@state,@postal_code,@country_code,@is_primary,@state_code)
                """, connection, transaction);
            command.Parameters.AddWithValue("id", address.Id);
            command.Parameters.AddWithValue("tenant_id", organisation.TenantId);
            command.Parameters.AddWithValue("organisation_id", organisation.Id);
            command.Parameters.AddWithValue("line1", address.Line1);
            AddNullableText(command, "line2", address.Line2);
            command.Parameters.AddWithValue("city", address.City);
            command.Parameters.AddWithValue("state", address.State);
            command.Parameters.AddWithValue("postal_code", address.PostalCode);
            command.Parameters.AddWithValue("country_code", address.CountryCode);
            command.Parameters.AddWithValue("is_primary", address.IsPrimary);
            AddNullableText(command, "state_code", address.StateCode);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task SetTenantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant_id, true)", connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value is null ? DBNull.Value : value.Value;
    }

    private static void AddNullableSmallint(NpgsqlCommand command, string name, short? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Smallint);
        parameter.Value = value is null ? DBNull.Value : value.Value;
    }
}
