using Npgsql;

namespace BusinessOS.Api.Commerce;

public sealed partial class PostgresCommerceActivationStore
{
    public async Task<LicenseActivationCodeResponse?> GetOrCreateDesktopActivationCodeAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        var persisted = await LoadSubscriptionAsync(
            connection, transaction, tenantId, subscriptionId,
            forUpdate: false, cancellationToken);
        if (persisted is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var existing = await LoadActivationCodeAsync(
            connection, transaction, tenantId, subscriptionId, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        var code = GenerateActivationCode();
        var response = new LicenseActivationCodeResponse(
            tenantId, persisted.Subscription.OrganisationId, subscriptionId,
            persisted.LicenseId, persisted.ProductCode, code, DateTimeOffset.UtcNow);
        await InsertActivationCodeAsync(
            connection, transaction, response, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<DesktopDeviceLicenseResponse?> ActivateDesktopDeviceWithCodeAsync(
        DesktopActivationCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var route = await ResolveActivationCodeAsync(
            request.ActivationCode, cancellationToken);
        if (route is null) return null;
        return await ActivateDesktopDeviceAsync(
            route.TenantId, route.SubscriptionId,
            new DesktopDeviceActivationRequest(
                request.DeviceFingerprint, request.DeviceName, request.AppVersion),
            cancellationToken);
    }

    public async Task<DesktopDeviceLicenseResponse?> ValidateDesktopDeviceWithCodeAsync(
        DesktopActivationCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var route = await ResolveActivationCodeAsync(
            request.ActivationCode, cancellationToken);
        if (route is null) return null;

        return await ValidateDesktopDeviceAsync(
            route.TenantId, route.SubscriptionId,
            new DesktopDeviceValidationRequest(
                request.DeviceFingerprint, request.CurrentLease),
            cancellationToken);
    }

    private async Task<LicenseActivationCodeResponse?> LoadActivationCodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT tenant_id,organisation_id,subscription_id,license_id,
                   product_code,activation_code,created_at_utc
            FROM commerce_license_activation_codes
            WHERE tenant_id=@tenant_id AND subscription_id=@subscription_id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new LicenseActivationCodeResponse(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetGuid(3), reader.GetString(4), reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6))
            : null;
    }

    private async Task InsertActivationCodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LicenseActivationCodeResponse response,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO commerce_license_activation_codes(
                tenant_id,organisation_id,subscription_id,license_id,
                product_code,activation_code,created_at_utc)
            VALUES(
                @tenant_id,@organisation_id,@subscription_id,@license_id,
                @product_code,@activation_code,@created_at_utc)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", response.TenantId);
        command.Parameters.AddWithValue("organisation_id", response.OrganisationId);
        command.Parameters.AddWithValue("subscription_id", response.SubscriptionId);
        command.Parameters.AddWithValue("license_id", response.LicenseId);
        command.Parameters.AddWithValue("product_code", response.ProductCode);
        command.Parameters.AddWithValue(
            "activation_code",
            NormalizeActivationCode(response.ActivationCode));
        command.Parameters.AddWithValue("created_at_utc", response.CreatedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<DesktopActivationRoute?> ResolveActivationCodeAsync(
        string value,
        CancellationToken cancellationToken)
    {
        var code = NormalizeActivationCode(value);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT tenant_id,subscription_id
            FROM commerce_license_activation_codes
            WHERE activation_code=@activation_code
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("activation_code", code);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new DesktopActivationRoute(reader.GetGuid(0), reader.GetGuid(1))
            : null;
    }

    private static string GenerateActivationCode()
    {
        var raw = Guid.NewGuid().ToString("N").ToUpperInvariant();
        return $"ORR-{raw[..4]}-{raw[4..8]}-{raw[8..12]}-{raw[12..16]}";
    }

    private static string NormalizeActivationCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Activation code is required.");
        return value.Trim().Replace(" ", "").ToUpperInvariant();
    }

    private sealed record DesktopActivationRoute(Guid TenantId, Guid SubscriptionId);
}
