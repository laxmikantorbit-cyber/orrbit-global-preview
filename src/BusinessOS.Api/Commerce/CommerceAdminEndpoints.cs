using BusinessOS.Api.Payments;
using BusinessOS.Api.Tenancy;

namespace BusinessOS.Api.Commerce;

public static class CommerceAdminEndpoints
{
    private const string RazorpayProvider = "razorpay";

    public static IEndpointRouteBuilder MapCommerceAdminEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/commerce/admin");

        group.MapGet("/status", async (
            int? take,
            TenantContext tenant,
            ICommerceActivationStore commerceStore,
            IPaymentEventStore paymentStore,
            CancellationToken cancellationToken) =>
        {
            var limit = Math.Clamp(take ?? 50, 1, 200);
            var snapshot = await commerceStore.GetAdminSnapshotAsync(
                tenant.TenantId, limit, cancellationToken);
            var providerOrderIds = snapshot.Orders
                .Select(x => x.ProviderOrderId ?? x.RazorpayOrderId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var payments = await paymentStore.ListPaymentsForProviderOrdersAsync(
                RazorpayProvider, providerOrderIds, limit, cancellationToken);
            var latestByOrder = payments
                .GroupBy(x => x.ProviderOrderId, StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => x.OrderByDescending(p => p.UpdatedAtUtc).First(),
                    StringComparer.Ordinal);

            var orders = snapshot.Orders
                .Select(order => ToStatusItem(order, latestByOrder, snapshot))
                .ToList();
            var counts = new CommerceAdminCounts(
                orders.Count(x => x.Order.OrderStatus == "PendingPayment"),
                payments.Count(x => x.Status == "Captured"),
                payments.Count(x => x.Status == "Failed"),
                snapshot.Activations.Count,
                snapshot.Renewals.Count,
                orders.Count(x => x.ReconciliationStatus is
                    "captured_pending_activation" or "payment_pending" or
                    "checkout_created_without_provider_order"));

            return Results.Ok(new CommerceAdminStatusResponse(
                snapshot.TenantId,
                snapshot.GeneratedAtUtc,
                counts,
                orders,
                payments,
                snapshot.Activations,
                snapshot.Renewals));
        });

        return app;
    }

    private static CommerceAdminOrderStatusItem ToStatusItem(
        CommerceAdminOrderSnapshot order,
        IReadOnlyDictionary<string, CommerceAdminPaymentItem> paymentsByOrder,
        CommerceAdminSnapshot snapshot)
    {
        var providerOrderId = order.ProviderOrderId ?? order.RazorpayOrderId;
        paymentsByOrder.TryGetValue(providerOrderId ?? string.Empty, out var payment);
        var activated = snapshot.Activations.Any(x => x.OrderId == order.CommerceOrderId);
        var renewed = snapshot.Renewals.Any(x => x.OrderId == order.CommerceOrderId);
        var reconciliation = ReconciliationStatus(order, payment, activated, renewed);
        return new CommerceAdminOrderStatusItem(
            order,
            payment?.Status,
            reconciliation);
    }

    private static string ReconciliationStatus(
        CommerceAdminOrderSnapshot order,
        CommerceAdminPaymentItem? payment,
        bool activated,
        bool renewed)
    {
        if (activated)
            return "activation_completed";
        if (renewed)
            return "renewal_completed";
        if (payment?.Status == "Captured")
            return "captured_pending_activation";
        if (payment?.Status == "Failed")
            return "payment_failed";
        if (payment?.Status == "Pending")
            return "payment_pending";
        if (string.IsNullOrWhiteSpace(order.ProviderOrderId) &&
            string.IsNullOrWhiteSpace(order.RazorpayOrderId))
            return "checkout_created_without_provider_order";
        return "awaiting_payment";
    }
}
