using BusinessOS.Api;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.SoftwareDelivery;

public sealed class PostgresSoftwareReleaseStore : ISoftwareReleaseStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string? _runtimeRole;

    public PostgresSoftwareReleaseStore(string connectionString, string? runtimeRole)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _runtimeRole = runtimeRole;
    }

    public async Task<SoftwareReleaseRecord> AddAsync(
        Guid tenantId,
        SoftwareReleaseCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var item = SoftwareReleaseValidator.Normalize(tenantId, request);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        const string sql = """
            INSERT INTO software_releases(
                id,tenant_id,product_code,version,channel,platform,architecture,
                file_name,download_url,sha256,size_bytes,release_notes,
                published_at_utc,active,created_at_utc)
            VALUES(
                @id,@tenant_id,@product_code,@version,@channel,@platform,@architecture,
                @file_name,@download_url,@sha256,@size_bytes,@release_notes,
                @published_at_utc,true,@created_at_utc)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Bind(command, item);
        command.Parameters.AddWithValue("created_at_utc", DateTimeOffset.UtcNow);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return item;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException("This software release already exists.", ex);
        }
    }

    public async Task<SoftwareReleaseRecord?> FindLatestActiveAsync(
        Guid tenantId,
        string productCode,
        string channel,
        string platform,
        string architecture,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        const string sql = """
            SELECT id,tenant_id,product_code,version,channel,platform,architecture,
                   file_name,download_url,sha256,size_bytes,release_notes,published_at_utc,active
            FROM software_releases
            WHERE tenant_id=@tenant_id
              AND upper(product_code)=upper(@product_code)
              AND upper(channel)=upper(@channel)
              AND upper(platform)=upper(@platform)
              AND upper(architecture)=upper(@architecture)
              AND active
            ORDER BY published_at_utc DESC,version DESC
            LIMIT 1
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("product_code", productCode.Trim());
        command.Parameters.AddWithValue("channel", channel.Trim());
        command.Parameters.AddWithValue("platform", platform.Trim());
        command.Parameters.AddWithValue("architecture", architecture.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var item = await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return item;
    }
    public async Task<IReadOnlyList<SoftwareReleaseRecord>> ListAsync(
        Guid tenantId,
        string? productCode,
        int take,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        const string sql = """
            SELECT id,tenant_id,product_code,version,channel,platform,architecture,
                   file_name,download_url,sha256,size_bytes,release_notes,published_at_utc,active
            FROM software_releases
            WHERE tenant_id=@tenant_id
              AND (@product_code IS NULL OR upper(product_code)=upper(@product_code))
            ORDER BY published_at_utc DESC,version DESC
            LIMIT @take
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        AddNullableText(command, "product_code",
            string.IsNullOrWhiteSpace(productCode) ? null : productCode.Trim());
        command.Parameters.AddWithValue("take", take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<SoftwareReleaseRecord>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(Read(reader));
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return items;
    }
    public async Task<bool> DeactivateAsync(
        Guid tenantId,
        Guid releaseId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE software_releases
            SET active=false
            WHERE tenant_id=@tenant_id AND id=@id AND active
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", releaseId);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    private static SoftwareReleaseRecord Read(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.GetString(8), reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetInt64(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetFieldValue<DateTimeOffset>(12), reader.GetBoolean(13));
    private static void Bind(NpgsqlCommand command, SoftwareReleaseRecord item)
    {
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("tenant_id", item.TenantId);
        command.Parameters.AddWithValue("product_code", item.ProductCode);
        command.Parameters.AddWithValue("version", item.Version);
        command.Parameters.AddWithValue("channel", item.Channel);
        command.Parameters.AddWithValue("platform", item.Platform);
        command.Parameters.AddWithValue("architecture", item.Architecture);
        command.Parameters.AddWithValue("file_name", item.FileName);
        command.Parameters.AddWithValue("download_url", item.DownloadUrl);
        command.Parameters.AddWithValue("sha256", item.Sha256);
        var size = command.Parameters.Add("size_bytes", NpgsqlDbType.Bigint);
        size.Value = item.SizeBytes is null ? DBNull.Value : item.SizeBytes.Value;
        AddNullableText(command, "release_notes", item.ReleaseNotes);
        command.Parameters.AddWithValue("published_at_utc", item.PublishedAtUtc);
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    private static async Task SetTenantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant_id, true)",
            connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
