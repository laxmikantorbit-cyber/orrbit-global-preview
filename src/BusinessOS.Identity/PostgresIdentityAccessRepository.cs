using Npgsql;

namespace BusinessOS.Identity;

public sealed class PostgresIdentityAccessRepository : IIdentityAccessRepository, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresIdentityAccessRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<UserIdentity?> FindUserBySubjectAsync(
        string subject,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            "SELECT id, subject, email, display_name, active " +
            "FROM user_identities WHERE subject = @subject LIMIT 1";

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("subject", subject);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new UserIdentity(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4));
    }

    public async Task<TenantAccess?> ResolveAccessAsync(
        string subject,
        string tenantCode,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            "SELECT u.id, t.id, t.code, m.role_code " +
            "FROM user_identities u " +
            "JOIN tenant_memberships m ON m.user_id = u.id " +
            "JOIN tenants t ON t.id = m.tenant_id " +
            "WHERE u.subject = @subject AND u.active = TRUE " +
            "AND t.code = @tenant_code AND t.status = @tenant_status " +
            "AND m.status = @membership_status LIMIT 1";

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("tenant_code", tenantCode);
        command.Parameters.AddWithValue("tenant_status", (int)TenantStatus.Active);
        command.Parameters.AddWithValue("membership_status", (int)MembershipStatus.Active);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new TenantAccess(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3));
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
