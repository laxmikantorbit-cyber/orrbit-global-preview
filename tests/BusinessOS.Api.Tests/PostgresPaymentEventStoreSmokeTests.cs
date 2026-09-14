using BusinessOS.Api.Payments;
using BusinessOS.Payments;

namespace BusinessOS.Api.Tests;

public sealed class PostgresPaymentEventStoreSmokeTests
{
    [Fact]
    public async Task Persisted_Event_Is_Duplicate_After_New_Store_Instance()
    {
        var cs = Environment.GetEnvironmentVariable("BUSINESSOS_COMMERCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
            return;

        var suffix = Guid.NewGuid().ToString("N");
        var paymentId = $"pay_pg_idem_{suffix}";
        var orderId = $"order_pg_idem_{suffix}";
        var message = Captured($"evt_pg_idem_{suffix}", paymentId, orderId);

        await using (var first = new PostgresPaymentEventStore(cs))
        {
            var initial = await first.ProcessAsync("razorpay", message);
            Assert.False(initial.Duplicate);
            Assert.Equal(PaymentStatus.Captured, initial.Payment.Status);
        }
        await using (var second = new PostgresPaymentEventStore(cs))
        {
            var duplicate = await second.ProcessAsync("razorpay", message);
            Assert.True(duplicate.Duplicate);
            Assert.Equal(paymentId, duplicate.Payment.PaymentId);
            Assert.Equal(orderId, duplicate.Payment.OrderId);
        }
    }

    [Fact]
    public async Task Pending_Payment_Can_Be_Upgraded_To_Captured()
    {
        var cs = Environment.GetEnvironmentVariable("BUSINESSOS_COMMERCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
            return;

        await using var store = new PostgresPaymentEventStore(cs);
        var suffix = Guid.NewGuid().ToString("N");
        var paymentId = $"pay_pg_upgrade_{suffix}";
        var orderId = $"order_pg_upgrade_{suffix}";
        var pending = await store.ProcessAsync("Razorpay", new PaymentWebhookMessage(
            $"evt_pg_pending_{suffix}",
            paymentId,
            orderId,
            PaymentStatus.Pending,
            10000,
            "INR"));
        var captured = await store.ProcessAsync("razorpay", Captured(
            $"evt_pg_captured_{suffix}", paymentId, orderId));

        Assert.True(pending.Accepted);
        Assert.False(pending.Duplicate);
        Assert.False(captured.Duplicate);
        Assert.Equal(PaymentStatus.Captured, captured.Payment.Status);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero), captured.Payment.CapturedAtUtc);
    }

    [Fact]
    public async Task Existing_Payment_Cannot_Be_Reused_For_Different_Order()
    {
        var cs = Environment.GetEnvironmentVariable("BUSINESSOS_COMMERCE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(cs))
            return;

        await using var store = new PostgresPaymentEventStore(cs);
        var suffix = Guid.NewGuid().ToString("N");
        var paymentId = $"pay_pg_mismatch_{suffix}";
        await store.ProcessAsync("razorpay", Captured(
            $"evt_pg_mismatch_a_{suffix}", paymentId, $"order_pg_a_{suffix}"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ProcessAsync(
            "razorpay",
            Captured($"evt_pg_mismatch_b_{suffix}", paymentId, $"order_pg_b_{suffix}")));
    }

    private static PaymentWebhookMessage Captured(
        string eventId,
        string paymentId,
        string orderId) => new(
        eventId,
        paymentId,
        orderId,
        PaymentStatus.Captured,
        10000,
        "INR",
        new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));
}
