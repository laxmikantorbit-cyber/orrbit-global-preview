using BusinessOS.Commerce;

namespace BusinessOS.Api.Commerce;

public static class EntitlementStatusEvaluator
{
    public const int GracePeriodDays = 7;

    public static EntitlementStatusResponse Evaluate(
        SubscriptionStateSnapshot state,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(state);
        var cancelled = state.SubscriptionStatus == SubscriptionStatus.Cancelled;
        var autoRenewEnabled = !cancelled;

        if (today < state.StartsOn)
            return Response(
                state,
                "NotStarted",
                cancelled ? "cancelled_at_period_end" : "not_due",
                autoRenewEnabled,
                cancelled,
                null);

        if (today <= state.ValidUntil)
            return Response(
                state,
                "Active",
                cancelled ? "cancelled_at_period_end" : "current_term_active",
                autoRenewEnabled,
                cancelled,
                null);

        if (cancelled)
            return Response(
                state,
                "Expired",
                "cancelled",
                false,
                true,
                null);

        var graceEndsOn = state.ValidUntil.AddDays(GracePeriodDays);
        if (today <= graceEndsOn)
            return Response(
                state,
                "Grace",
                "payment_pending",
                true,
                false,
                graceEndsOn);

        return Response(
            state,
            "Expired",
            "renewal_required",
            true,
            false,
            graceEndsOn);
    }

    private static EntitlementStatusResponse Response(
        SubscriptionStateSnapshot state,
        string status,
        string renewalStatus,
        bool autoRenewEnabled,
        bool cancelAtPeriodEnd,
        DateOnly? graceEndsOn) =>
        new(
            state.TenantId,
            state.OrganisationId,
            state.SubscriptionId,
            state.LicenseId,
            state.ProductCode,
            state.PlanId,
            state.PlanVersionId,
            state.StartsOn,
            state.ValidUntil,
            state.Entitlements,
            status,
            renewalStatus,
            autoRenewEnabled,
            cancelAtPeriodEnd,
            graceEndsOn);
}
