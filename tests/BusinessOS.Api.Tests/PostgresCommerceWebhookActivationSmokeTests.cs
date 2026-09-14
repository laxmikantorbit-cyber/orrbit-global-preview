using BusinessOS.Api.Commerce;
using BusinessOS.Application;
using BusinessOS.Licensing;
using BusinessOS.Payments;
using Npgsql;

namespace BusinessOS.Api.Tests;

public sealed class PostgresCommerceWebhookActivationSmokeTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlanA = Guid.Parse("73333333-3333-3333-3333-333333333331");
    private static readonly Guid PlanVersionA = Guid.Parse("74444444-4444-4444-4444-444444444441");

    [Fact]
    public async Task Postgres_Activates_Pending_Order_From_Captured_Webhook_Payment()
    {
        var cs = Environment.GetEnvironmentVariable("BUSINESSOS_COMMERCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
            return;

        using var signer = new LeaseSigner();
        await using var store = new PostgresCommerceActivationStore(
            cs, new PaymentSubscriptionActivationService(), signer);

        var suffix = Guid.NewGuid().ToString("N");
        var quoteId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await SeedPendingOrderAsync(
            cs, quoteId, orderId, webAdminSeats: 10);

        var payment = CapturedPayment(
            $"pay_webhook_initial_{suffix}",
            orderId,
            new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

        var activation = await store.ActivateCapturedInitialOrderAsync(
            TenantA,
            payment,
            "ORRBIT-REPAIR");
        var duplicate = await store.ActivateCapturedInitialOrderAsync(
            TenantA,
            payment,
            "ORRBIT-REPAIR");

        Assert.NotNull(activation);
        Assert.NotNull(duplicate);
        Assert.Equal(activation!.SubscriptionId, duplicate!.SubscriptionId);
        Assert.Equal(new DateOnly(2027, 9, 13), activation.ValidUntil);
        Assert.Equal(10, activation.Entitlements.WebAdminSeats);
    }

    [Fact]
    public async Task Postgres_Activates_Pending_Renewal_Order_From_Captured_Webhook_Payment()
    {
        var cs = Environment.GetEnvironmentVariable("BUSINESSOS_COMMERCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
            return;

        using var signer = new LeaseSigner();
        await using var store = new PostgresCommerceActivationStore(
            cs, new PaymentSubscriptionActivationService(), signer);
        var suffix = Guid.NewGuid().ToString("N");
        var activation = await store.ActivateInitialPurchaseAsync(
            TenantA,
            InitialRequest($"pay_webhook_base_{suffix}"));

        var quoteId = Guid.NewGuid();
        var renewalOrderId = Guid.NewGuid();
        await SeedPendingOrderAsync(
            cs, quoteId, renewalOrderId, webAdminSeats: 20);
        var payment = CapturedPayment(
            $"pay_webhook_renewal_{suffix}",
            renewalOrderId,
            new DateTimeOffset(2027, 8, 1, 10, 0, 0, TimeSpan.Zero));

        var renewal = await store.ActivateCapturedRenewalOrderAsync(
            TenantA,
            activation.SubscriptionId,
            payment);

        var duplicate = await store.ActivateCapturedRenewalOrderAsync(
            TenantA,
            activation.SubscriptionId,
            payment);
        var current = await store.FindActivationAsync(TenantA, activation.SubscriptionId);

        Assert.NotNull(renewal);
        Assert.NotNull(duplicate);
        Assert.NotNull(current);
        Assert.Equal(renewal!.RenewalId, duplicate!.RenewalId);
        Assert.Equal(new DateOnly(2028, 9, 13), renewal.NewValidUntil);
        Assert.Equal(20, current!.Entitlements.WebAdminSeats);
    }

    private static InitialActivationRequest InitialRequest(string paymentId) => new(
        OrgA,
        "ORRBIT-REPAIR",
        PlanA,
        PlanVersionA,
        1,
        100m,
        "INR",
        12,
        1,
        1,
        10,
        5,
        true,
        paymentId,
        new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));

    private static PaymentRecord CapturedPayment(
        string paymentId,
        Guid orderId,
        DateTimeOffset capturedAtUtc)
        => new(
            paymentId,
            orderId.ToString(),
            PaymentStatus.Captured,
            10000,
            "INR",
            capturedAtUtc);

    private static async Task SeedPendingOrderAsync(
        string connectionString,
        Guid quoteId,
        Guid orderId,
        int webAdminSeats)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var tenantCommand = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant_id, true)",
            connection,
            transaction))
        {
            tenantCommand.Parameters.AddWithValue("tenant_id", TenantA.ToString());
            await tenantCommand.ExecuteNonQueryAsync();
        }

        var entitlementJson =
            $$"""
            {"desktopSystems":1,"locations":1,"webAdminSeats":{{webAdminSeats}},"fieldStaffSeats":5,"multiLocationCloud":true}
            """;
        const string quoteSql = """
            INSERT INTO commerce_quotes(
                id,tenant_id,organisation_id,plan_id,plan_version_id,
                plan_version_number,amount,currency_code,billing_cycle,term_months,
                entitlement_snapshot,created_at_utc,valid_until_utc,status)
            VALUES(
                @quote_id,@tenant_id,@organisation_id,@plan_id,@plan_version_id,
                1,100,'INR',3,12,@entitlement_snapshot::jsonb,now(),now()+interval '7 days',2)
            """;
        await using (var quoteCommand = new NpgsqlCommand(
            quoteSql,
            connection,
            transaction))
        {
            quoteCommand.Parameters.AddWithValue("quote_id", quoteId);
            quoteCommand.Parameters.AddWithValue("tenant_id", TenantA);
            quoteCommand.Parameters.AddWithValue("organisation_id", OrgA);
            quoteCommand.Parameters.AddWithValue("plan_id", PlanA);
            quoteCommand.Parameters.AddWithValue("plan_version_id", PlanVersionA);
            quoteCommand.Parameters.AddWithValue("entitlement_snapshot", entitlementJson);
            await quoteCommand.ExecuteNonQueryAsync();
        }
        const string orderSql = """
            INSERT INTO commerce_orders(
                id,tenant_id,organisation_id,quote_id,plan_id,plan_version_id,
                amount,currency_code,status,payment_id,paid_at_utc)
            VALUES(
                @order_id,@tenant_id,@organisation_id,@quote_id,@plan_id,@plan_version_id,
                100,'INR',1,NULL,NULL)
            """;
        await using (var orderCommand = new NpgsqlCommand(
            orderSql,
            connection,
            transaction))
        {
            orderCommand.Parameters.AddWithValue("order_id", orderId);
            orderCommand.Parameters.AddWithValue("tenant_id", TenantA);
            orderCommand.Parameters.AddWithValue("organisation_id", OrgA);
            orderCommand.Parameters.AddWithValue("quote_id", quoteId);
            orderCommand.Parameters.AddWithValue("plan_id", PlanA);
            orderCommand.Parameters.AddWithValue("plan_version_id", PlanVersionA);
            await orderCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}
