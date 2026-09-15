using BusinessOS.Api.Commerce;
using BusinessOS.Commerce;
using BusinessOS.Licensing;

namespace BusinessOS.Api.Tests;

public sealed class EntitlementStatusEvaluatorTests
{
    [Fact]
    public void Active_Term_Remains_Active()
    {
        var state = State(SubscriptionStatus.Active);

        var result = EntitlementStatusEvaluator.Evaluate(
            state, new DateOnly(2026, 9, 15));

        Assert.Equal("Active", result.Status);
        Assert.Equal("current_term_active", result.RenewalStatus);
        Assert.True(result.AutoRenewEnabled);
        Assert.False(result.CancelAtPeriodEnd);
        Assert.Null(result.GraceEndsOn);
    }

    [Fact]
    public void Expired_Uncancelled_Term_Enters_Seven_Day_Grace()
    {
        var state = State(SubscriptionStatus.Active) with
        {
            ValidUntil = new DateOnly(2026, 9, 14)
        };
        var result = EntitlementStatusEvaluator.Evaluate(
            state, new DateOnly(2026, 9, 15));

        Assert.Equal("Grace", result.Status);
        Assert.Equal("payment_pending", result.RenewalStatus);
        Assert.Equal(new DateOnly(2026, 9, 21), result.GraceEndsOn);
    }

    [Fact]
    public void Grace_Ends_After_Seven_Days()
    {
        var state = State(SubscriptionStatus.Active) with
        {
            ValidUntil = new DateOnly(2026, 9, 14)
        };

        var result = EntitlementStatusEvaluator.Evaluate(
            state, new DateOnly(2026, 9, 22));

        Assert.Equal("Expired", result.Status);
        Assert.Equal("renewal_required", result.RenewalStatus);
        Assert.Equal(new DateOnly(2026, 9, 21), result.GraceEndsOn);
    }

    [Fact]
    public void Cancel_At_Period_End_Keeps_Paid_Term_Active()
    {
        var state = State(SubscriptionStatus.Cancelled);

        var result = EntitlementStatusEvaluator.Evaluate(
            state, new DateOnly(2026, 9, 15));

        Assert.Equal("Active", result.Status);
        Assert.Equal("cancelled_at_period_end", result.RenewalStatus);
        Assert.False(result.AutoRenewEnabled);
        Assert.True(result.CancelAtPeriodEnd);
        Assert.Null(result.GraceEndsOn);
    }

    [Fact]
    public void Cancelled_Subscription_Expires_Without_Grace()
    {
        var state = State(SubscriptionStatus.Cancelled) with
        {
            ValidUntil = new DateOnly(2026, 9, 14)
        };

        var result = EntitlementStatusEvaluator.Evaluate(
            state, new DateOnly(2026, 9, 15));

        Assert.Equal("Expired", result.Status);
        Assert.Equal("cancelled", result.RenewalStatus);
        Assert.False(result.AutoRenewEnabled);
        Assert.True(result.CancelAtPeriodEnd);
        Assert.Null(result.GraceEndsOn);
    }

    private static SubscriptionStateSnapshot State(
        SubscriptionStatus status) =>
        new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "AI_REPAIR",
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            new DateOnly(2026, 9, 1),
            new DateOnly(2027, 8, 31),
            new EntitlementSnapshot(1, 1, 10, 10, true),
            status);
}
