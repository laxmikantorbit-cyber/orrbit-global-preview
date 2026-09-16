using Npgsql;

namespace BusinessOS.Api.Commerce;

public sealed partial class PostgresCommerceActivationStore
{
    public async Task<ProviderSubscriptionBinding?> FindProviderSubscriptionAsync(
        Guid tenantId,
        Guid subscriptionId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProviderValue(provider);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        const string sql = """
            SELECT tenant_id,subscription_id,provider,provider_subscription_id,
                   provider_plan_id,start_at_unix,total_count,status,
                   auto_renew_enabled,cancel_at_period_end,authorization_url,updated_at_utc
            FROM commerce_provider_subscription_routes
            WHERE tenant_id=@tenant_id AND subscription_id=@subscription_id
              AND provider=@provider
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("provider", normalizedProvider);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProviderBinding(reader) : null;
    }

    public async Task<ProviderSubscriptionBinding?> FindProviderSubscriptionRouteAsync(
        string provider,
        string providerSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProviderValue(provider);
        if (string.IsNullOrWhiteSpace(providerSubscriptionId)) return null;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        const string sql = """
            SELECT tenant_id,subscription_id,provider,provider_subscription_id,
                   provider_plan_id,start_at_unix,total_count,status,
                   auto_renew_enabled,cancel_at_period_end,authorization_url,updated_at_utc
            FROM commerce_provider_subscription_routes
            WHERE provider=@provider AND provider_subscription_id=@provider_subscription_id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider", normalizedProvider);
        command.Parameters.AddWithValue("provider_subscription_id", providerSubscriptionId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProviderBinding(reader) : null;
    }

    public async Task<ProviderSubscriptionBinding> RecordProviderSubscriptionAsync(
        ProviderSubscriptionBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ValidateProviderBinding(binding);
        var normalized = binding with
        {
            Provider = NormalizeProviderValue(binding.Provider),
            ProviderSubscriptionId = binding.ProviderSubscriptionId.Trim(),
            ProviderPlanId = binding.ProviderPlanId.Trim(),
            Status = binding.Status.Trim(),
            AuthorizationUrl = string.IsNullOrWhiteSpace(binding.AuthorizationUrl)
                ? null : binding.AuthorizationUrl.Trim()
        };
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantAsync(connection, transaction, normalized.TenantId, cancellationToken);
        var subscription = await LoadSubscriptionAsync(
            connection, transaction, normalized.TenantId, normalized.SubscriptionId,
            forUpdate: false, cancellationToken);
        if (subscription is null)
            throw new InvalidOperationException("Subscription was not found for provider binding.");

        const string sql = """
            INSERT INTO commerce_provider_subscription_routes(
                provider,provider_subscription_id,tenant_id,subscription_id,
                provider_plan_id,start_at_unix,total_count,status,
                auto_renew_enabled,cancel_at_period_end,
                authorization_url,created_at_utc,updated_at_utc)
            VALUES(
                @provider,@provider_subscription_id,@tenant_id,@subscription_id,
                @provider_plan_id,@start_at_unix,@total_count,@status,
                @auto_renew_enabled,@cancel_at_period_end,
                @authorization_url,@updated_at_utc,@updated_at_utc)
            ON CONFLICT (tenant_id,subscription_id,provider) DO UPDATE SET
                provider_subscription_id=EXCLUDED.provider_subscription_id,
                provider_plan_id=EXCLUDED.provider_plan_id,
                start_at_unix=EXCLUDED.start_at_unix,total_count=EXCLUDED.total_count,
                status=EXCLUDED.status,auto_renew_enabled=EXCLUDED.auto_renew_enabled,
                cancel_at_period_end=EXCLUDED.cancel_at_period_end,
                authorization_url=EXCLUDED.authorization_url,
                updated_at_utc=EXCLUDED.updated_at_utc
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("provider", normalized.Provider);
        command.Parameters.AddWithValue("provider_subscription_id", normalized.ProviderSubscriptionId);
        command.Parameters.AddWithValue("tenant_id", normalized.TenantId);
        command.Parameters.AddWithValue("subscription_id", normalized.SubscriptionId);
        command.Parameters.AddWithValue("provider_plan_id", normalized.ProviderPlanId);
        command.Parameters.AddWithValue("start_at_unix", normalized.StartAtUnix);
        command.Parameters.AddWithValue("total_count", normalized.TotalCount);
        command.Parameters.AddWithValue("status", normalized.Status);
        command.Parameters.AddWithValue("auto_renew_enabled", normalized.AutoRenewEnabled);
        command.Parameters.AddWithValue("cancel_at_period_end", normalized.CancelAtPeriodEnd);
        command.Parameters.AddWithValue(
            "authorization_url",
            (object?)normalized.AuthorizationUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("updated_at_utc", normalized.UpdatedAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return normalized;
    }

    public async Task<ProviderSubscriptionBinding?> UpdateProviderSubscriptionStateAsync(
        string provider,
        string providerSubscriptionId,
        string status,
        bool autoRenewEnabled,
        bool cancelAtPeriodEnd,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProviderValue(provider);
        if (string.IsNullOrWhiteSpace(providerSubscriptionId) || string.IsNullOrWhiteSpace(status))
            throw new ArgumentException("Provider subscription id and status are required.");
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await BusinessOS.Api.PostgresRuntimeRole.ApplyAsync(connection, _runtimeRole, cancellationToken);
        const string sql = """
            UPDATE commerce_provider_subscription_routes
            SET status=@status,auto_renew_enabled=@auto_renew_enabled,
                cancel_at_period_end=@cancel_at_period_end,updated_at_utc=@updated_at_utc
            WHERE provider=@provider AND provider_subscription_id=@provider_subscription_id
            RETURNING tenant_id,subscription_id,provider,provider_subscription_id,
                      provider_plan_id,start_at_unix,total_count,status,
                      auto_renew_enabled,cancel_at_period_end,authorization_url,updated_at_utc
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("status", status.Trim());
        command.Parameters.AddWithValue("auto_renew_enabled", autoRenewEnabled);
        command.Parameters.AddWithValue("cancel_at_period_end", cancelAtPeriodEnd);
        command.Parameters.AddWithValue("updated_at_utc", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("provider", normalizedProvider);
        command.Parameters.AddWithValue("provider_subscription_id", providerSubscriptionId.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProviderBinding(reader) : null;
    }

    private static ProviderSubscriptionBinding ReadProviderBinding(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetInt64(5), reader.GetInt32(6), reader.GetString(7),
            reader.GetBoolean(8), reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetFieldValue<DateTimeOffset>(11));

    private static void ValidateProviderBinding(ProviderSubscriptionBinding binding)
    {
        if (binding.TenantId == Guid.Empty || binding.SubscriptionId == Guid.Empty)
            throw new ArgumentException("Tenant and subscription ids are required.");
        if (string.IsNullOrWhiteSpace(binding.Provider) ||
            string.IsNullOrWhiteSpace(binding.ProviderSubscriptionId) ||
            string.IsNullOrWhiteSpace(binding.ProviderPlanId) ||
            string.IsNullOrWhiteSpace(binding.Status))
            throw new ArgumentException("Provider binding fields are required.");
        if (binding.StartAtUnix <= 0 || binding.TotalCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(binding),
                "Provider subscription schedule is invalid.");
    }

    private static string NormalizeProviderValue(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));
        return provider.Trim().ToLowerInvariant();
    }
}
