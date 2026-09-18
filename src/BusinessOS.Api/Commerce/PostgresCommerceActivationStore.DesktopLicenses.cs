using BusinessOS.Licensing;
using Npgsql;

namespace BusinessOS.Api.Commerce;

public sealed partial class PostgresCommerceActivationStore
{
    public async Task<DesktopDeviceLicenseResponse?> ActivateDesktopDeviceAsync(
        Guid tenantId,
        Guid subscriptionId,
        DesktopDeviceActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fingerprint = NormalizeDeviceFingerprint(request.DeviceFingerprint);
        var now = DateTimeOffset.UtcNow;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        var persisted = await LoadSubscriptionAsync(
            connection, transaction, tenantId, subscriptionId,
            forUpdate: true, cancellationToken);
        if (persisted is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var existing = await LoadDesktopDeviceAsync(
            connection, transaction, tenantId, subscriptionId,
            fingerprint, cancellationToken);
        if (existing is null)
        {
            var activeCount = await CountActiveDesktopDevicesAsync(
                connection, transaction, tenantId, subscriptionId,
                cancellationToken);
            if (activeCount >= persisted.Subscription.Entitlements.DesktopSystems)
                throw new InvalidOperationException(
                    "No desktop device entitlement is available.");
            existing = new DesktopDeviceActivationSnapshot(
                Guid.NewGuid(), fingerprint, request.DeviceName?.Trim(),
                request.AppVersion?.Trim(), true, now, now);
            await InsertDesktopDeviceAsync(
                connection, transaction, tenantId, subscriptionId,
                existing, cancellationToken);
        }
        else if (!existing.Active)
        {
            var activeCount = await CountActiveDesktopDevicesAsync(
                connection, transaction, tenantId, subscriptionId,
                cancellationToken);
            if (activeCount >= persisted.Subscription.Entitlements.DesktopSystems)
                throw new InvalidOperationException(
                    "No desktop device entitlement is available.");
            existing = existing with
            {
                DeviceName = request.DeviceName?.Trim() ?? existing.DeviceName,
                AppVersion = request.AppVersion?.Trim() ?? existing.AppVersion,
                Active = true,
                ActivatedAtUtc = now,
                LastValidatedAtUtc = now
            };
            await UpdateDesktopDeviceStateAsync(
                connection, transaction, tenantId, subscriptionId,
                existing, cancellationToken);
        }
        else
        {
            existing = existing with
            {
                DeviceName = request.DeviceName?.Trim() ?? existing.DeviceName,
                AppVersion = request.AppVersion?.Trim() ?? existing.AppVersion,
                LastValidatedAtUtc = now
            };
            await UpdateDesktopDeviceValidationAsync(
                connection, transaction, tenantId, subscriptionId,
                fingerprint, existing, cancellationToken);
        }

        await InsertDeviceEventAsync(
            connection, transaction, tenantId, subscriptionId, fingerprint, null,
            "activate", "device_activated", existing.DeviceName, existing.AppVersion,
            now, cancellationToken);
        var response = await ToDesktopResponseAsync(
            connection, transaction, tenantId, persisted,
            existing, true, "device_activated", now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<DesktopDeviceLicenseResponse?> ValidateDesktopDeviceAsync(
        Guid tenantId,
        Guid subscriptionId,
        DesktopDeviceValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fingerprint = NormalizeDeviceFingerprint(request.DeviceFingerprint);
        var now = DateTimeOffset.UtcNow;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
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

        var existing = await LoadDesktopDeviceAsync(
            connection, transaction, tenantId, subscriptionId,
            fingerprint, cancellationToken);
        if (existing is null || !existing.Active)
        {
            var inactive = new DesktopDeviceActivationSnapshot(
                Guid.Empty, fingerprint, null, null, false,
                DateTimeOffset.MinValue, null);
            await InsertDeviceEventAsync(
                connection, transaction, tenantId, subscriptionId, fingerprint, null,
                "validate", "device_not_activated", null, null,
                now, cancellationToken);
            var inactiveResponse = await ToDesktopResponseAsync(
                connection, transaction, tenantId, persisted,
                inactive, false, "device_not_activated",
                now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return inactiveResponse;
        }

        existing = existing with { LastValidatedAtUtc = now };
        await UpdateDesktopDeviceValidationAsync(
            connection, transaction, tenantId, subscriptionId,
            fingerprint, existing, cancellationToken);
        var validToday = DateOnly.FromDateTime(now.UtcDateTime) <=
            persisted.Subscription.ValidUntil!.Value;
        await InsertDeviceEventAsync(
            connection, transaction, tenantId, subscriptionId, fingerprint, null,
            "validate", validToday ? "license_valid" : "subscription_expired",
            existing.DeviceName, existing.AppVersion, now, cancellationToken);
        var response = await ToDesktopResponseAsync(
            connection, transaction, tenantId, persisted, existing,
            validToday, validToday ? "license_valid" : "subscription_expired",
            now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<IReadOnlyList<DesktopDeviceActivationSnapshot>> ListDesktopDevicesAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        const string sql = """
            SELECT id,device_fingerprint,device_name,app_version,active,
                   activated_at_utc,last_validated_at_utc
            FROM commerce_desktop_device_activations
            WHERE tenant_id=@tenant_id AND subscription_id=@subscription_id
            ORDER BY active DESC, activated_at_utc DESC
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<DesktopDeviceActivationSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new DesktopDeviceActivationSnapshot(
                reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4), reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)));
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    public async Task<bool> RevokeDesktopDeviceAsync(
        Guid tenantId,
        Guid subscriptionId,
        string deviceFingerprint,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = NormalizeDeviceFingerprint(deviceFingerprint);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        const string sql = """
            UPDATE commerce_desktop_device_activations
            SET active=false
            WHERE tenant_id=@tenant_id AND subscription_id=@subscription_id
              AND device_fingerprint=@device_fingerprint AND active=true
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("device_fingerprint", fingerprint);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 1)
            await InsertDeviceEventAsync(
                connection, transaction, tenantId, subscriptionId, fingerprint, null,
                "revoke", "device_revoked", null, null,
                DateTimeOffset.UtcNow, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return affected == 1;
    }

    public async Task<DesktopDeviceLicenseResponse?> ReplaceDesktopDeviceAsync(
        Guid tenantId,
        Guid subscriptionId,
        DesktopDeviceReplaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var oldFingerprint = NormalizeDeviceFingerprint(request.OldDeviceFingerprint);
        var newFingerprint = NormalizeDeviceFingerprint(request.NewDeviceFingerprint);
        if (string.Equals(oldFingerprint, newFingerprint, StringComparison.Ordinal))
            throw new ArgumentException("Old and new device fingerprints must be different.");
        var now = DateTimeOffset.UtcNow;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, tenantId, cancellationToken);
        var persisted = await LoadSubscriptionAsync(
            connection, transaction, tenantId, subscriptionId, true, cancellationToken);
        if (persisted is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        var oldDevice = await LoadDesktopDeviceAsync(
            connection, transaction, tenantId, subscriptionId, oldFingerprint, cancellationToken);
        if (oldDevice is null || !oldDevice.Active)
            throw new InvalidOperationException("Old device is not active.");
        var newDevice = await LoadDesktopDeviceAsync(
            connection, transaction, tenantId, subscriptionId, newFingerprint, cancellationToken);
        if (newDevice?.Active == true)
            throw new InvalidOperationException("New device is already active.");
        var newDeviceExisted = newDevice is not null;
        await SetDesktopDeviceActiveAsync(
            connection, transaction, tenantId, subscriptionId, oldFingerprint, false, cancellationToken);
        newDevice = newDevice is null
            ? new DesktopDeviceActivationSnapshot(
                Guid.NewGuid(), newFingerprint, request.DeviceName?.Trim(), request.AppVersion?.Trim(), true, now, now)
            : newDevice with
            {
                DeviceName = request.DeviceName?.Trim() ?? newDevice.DeviceName,
                AppVersion = request.AppVersion?.Trim() ?? newDevice.AppVersion,
                Active = true,
                ActivatedAtUtc = now,
                LastValidatedAtUtc = now
            };
        if (newDeviceExisted)
            await UpdateDesktopDeviceStateAsync(connection, transaction, tenantId, subscriptionId, newDevice, cancellationToken);
        else
            await InsertDesktopDeviceAsync(connection, transaction, tenantId, subscriptionId, newDevice, cancellationToken);
        await InsertDeviceEventAsync(
            connection, transaction, tenantId, subscriptionId, newFingerprint,
            oldFingerprint, "replace", "device_replaced",
            newDevice.DeviceName, newDevice.AppVersion, now, cancellationToken);
        var response = await ToDesktopResponseAsync(
            connection, transaction, tenantId, persisted, newDevice,
            true, "device_replaced", now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
    private static async Task<DesktopDeviceActivationSnapshot?> LoadDesktopDeviceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid subscriptionId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id,device_fingerprint,device_name,app_version,active,
                   activated_at_utc,last_validated_at_utc
            FROM commerce_desktop_device_activations
            WHERE tenant_id=@tenant_id AND subscription_id=@subscription_id
              AND device_fingerprint=@device_fingerprint
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("device_fingerprint", fingerprint);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new DesktopDeviceActivationSnapshot(
                reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4), reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6))
            : null;
    }

    private static async Task<int> CountActiveDesktopDevicesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT count(*)
            FROM commerce_desktop_device_activations
            WHERE tenant_id=@tenant_id AND subscription_id=@subscription_id
              AND active=true
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    private static async Task InsertDesktopDeviceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid subscriptionId,
        DesktopDeviceActivationSnapshot device,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO commerce_desktop_device_activations(
                id,tenant_id,subscription_id,device_fingerprint,device_name,
                app_version,active,activated_at_utc,last_validated_at_utc)
            VALUES(
                @id,@tenant_id,@subscription_id,@device_fingerprint,@device_name,
                @app_version,@active,@activated_at_utc,@last_validated_at_utc)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddDesktopDeviceParameters(command, tenantId, subscriptionId, device);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateDesktopDeviceValidationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid subscriptionId,
        string fingerprint,
        DesktopDeviceActivationSnapshot device,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE commerce_desktop_device_activations
            SET device_name=@device_name,
                app_version=@app_version,
                last_validated_at_utc=@last_validated_at_utc
            WHERE tenant_id=@tenant_id AND subscription_id=@subscription_id
              AND device_fingerprint=@device_fingerprint
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("device_name", (object?)device.DeviceName ?? DBNull.Value);
        command.Parameters.AddWithValue("app_version", (object?)device.AppVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("last_validated_at_utc", device.LastValidatedAtUtc ?? DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("device_fingerprint", fingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateDesktopDeviceStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid subscriptionId,
        DesktopDeviceActivationSnapshot device,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE commerce_desktop_device_activations
            SET device_name=@device_name, app_version=@app_version, active=@active,
                activated_at_utc=@activated_at_utc, last_validated_at_utc=@last_validated_at_utc
            WHERE tenant_id=@tenant_id AND subscription_id=@subscription_id
              AND device_fingerprint=@device_fingerprint
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddDesktopDeviceParameters(command, tenantId, subscriptionId, device);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetDesktopDeviceActiveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid subscriptionId,
        string fingerprint,
        bool active,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE commerce_desktop_device_activations
            SET active=@active
            WHERE tenant_id=@tenant_id AND subscription_id=@subscription_id
              AND device_fingerprint=@device_fingerprint
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("active", active);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("device_fingerprint", fingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static void AddDesktopDeviceParameters(
        NpgsqlCommand command,
        Guid tenantId,
        Guid subscriptionId,
        DesktopDeviceActivationSnapshot device)
    {
        command.Parameters.AddWithValue("id", device.Id);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("device_fingerprint", device.DeviceFingerprint);
        command.Parameters.AddWithValue("device_name", (object?)device.DeviceName ?? DBNull.Value);
        command.Parameters.AddWithValue("app_version", (object?)device.AppVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("active", device.Active);
        command.Parameters.AddWithValue("activated_at_utc", device.ActivatedAtUtc);
        command.Parameters.AddWithValue("last_validated_at_utc", (object?)device.LastValidatedAtUtc ?? DBNull.Value);
    }

    private async Task<DesktopDeviceLicenseResponse> ToDesktopResponseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        PersistedActivation persisted,
        DesktopDeviceActivationSnapshot device,
        bool allowed,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var state = ToStateSnapshot(persisted);
        var entitlement = EntitlementStatusEvaluator.Evaluate(
            state,
            DateOnly.FromDateTime(now.UtcDateTime));
        var activeDevices = await CountActiveDesktopDevicesAsync(
            connection, transaction, tenantId, state.SubscriptionId,
            cancellationToken);
        var lease = allowed ? CreateLease(state, device.DeviceFingerprint, now) : null;
        var leasePayload = lease is null
            ? null
            : LeaseSigner.Verify(lease, _signer.ExportPublicKey(), device.DeviceFingerprint, now);
        return new DesktopDeviceLicenseResponse(
            state.TenantId,
            state.OrganisationId,
            state.SubscriptionId,
            state.LicenseId,
            state.ProductCode,
            device.DeviceFingerprint,
            device.DeviceName,
            entitlement.Status,
            entitlement.RenewalStatus,
            allowed,
            reason,
            activeDevices,
            state.Entitlements.DesktopSystems,
            state.StartsOn,
            state.ValidUntil,
            leasePayload?.LeaseValidUntil,
            lease,
            Convert.ToBase64String(_signer.ExportPublicKey()),
            state.Entitlements);
    }

    private SignedLicenseLease CreateLease(
        SubscriptionStateSnapshot state,
        string deviceFingerprint,
        DateTimeOffset now)
    {
        if (DateOnly.FromDateTime(now.UtcDateTime) > state.ValidUntil)
            throw new InvalidOperationException("Subscription has expired.");
        var subscriptionEnd = new DateTimeOffset(
            state.ValidUntil.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
        var leaseEnd = now.AddDays(7);
        if (leaseEnd > subscriptionEnd)
            leaseEnd = subscriptionEnd;
        return _signer.Sign(new LicenseLeasePayload(
            state.LicenseId,
            state.ProductCode,
            deviceFingerprint,
            now,
            leaseEnd,
            state.ValidUntil,
            state.Entitlements));
    }

    private static string NormalizeDeviceFingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Device fingerprint is required.");
        var normalized = value.Trim();
        if (normalized.Length > 256)
            throw new ArgumentException("Device fingerprint must be 256 characters or fewer.");
        return normalized;
    }
}
