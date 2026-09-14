using System.Text.Json;
using BusinessOS.Payments;

namespace BusinessOS.Api.Payments;

internal sealed record RazorpayPaymentWebhook(
    Guid TenantId,
    Guid? SubscriptionId,
    string? ProductCode,
    PaymentWebhookMessage Message);

internal static class RazorpayWebhookParser
{
    public static RazorpayPaymentWebhook Parse(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            throw new ArgumentException("Webhook body is required.");

        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;
        var eventName = OptionalString(root, "event") ?? "unknown";
        var payment = PaymentEntity(root);

        var paymentId = RequiredString(payment, "id");
        var internalOrderId = OptionalNote(payment, "commerceOrderId")
            ?? OptionalNote(payment, "internalOrderId")
            ?? RequiredString(payment, "order_id");
        var amount = RequiredInt64(payment, "amount");
        var currency = RequiredString(payment, "currency");
        var status = ResolveStatus(eventName, payment);
        var capturedAt = ResolveCapturedAtUtc(payment, status);
        var eventId = OptionalString(root, "id")
            ?? OptionalString(root, "event_id")
            ?? $"{eventName}:{paymentId}:{status}";

        var tenantIdText = OptionalNote(payment, "tenantId")
            ?? throw new ArgumentException("Webhook payment notes must include tenantId.");
        if (!Guid.TryParse(tenantIdText, out var tenantId) || tenantId == Guid.Empty)
            throw new ArgumentException("Webhook tenantId note is invalid.");

        var subscriptionId = ParseOptionalGuid(OptionalNote(payment, "subscriptionId"));
        var productCode = OptionalNote(payment, "productCode");

        return new RazorpayPaymentWebhook(
            tenantId,
            subscriptionId,
            productCode,
            new PaymentWebhookMessage(
                eventId,
                paymentId,
                internalOrderId,
                status,
                amount,
                currency,
                capturedAt));
    }

    private static JsonElement PaymentEntity(JsonElement root)
    {
        if (root.TryGetProperty("payload", out var payload) &&
            payload.TryGetProperty("payment", out var paymentPayload) &&
            paymentPayload.TryGetProperty("entity", out var entity))
            return entity;

        if (root.TryGetProperty("payment", out var directPayment))
            return directPayment;

        throw new ArgumentException("Webhook body does not contain payment entity.");
    }

    private static PaymentStatus ResolveStatus(string eventName, JsonElement payment)
    {
        var status = OptionalString(payment, "status");
        var captured = OptionalBool(payment, "captured");
        if (captured == true || string.Equals(status, "captured", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventName, "payment.captured", StringComparison.OrdinalIgnoreCase))
            return PaymentStatus.Captured;
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventName, "payment.failed", StringComparison.OrdinalIgnoreCase))
            return PaymentStatus.Failed;

        return PaymentStatus.Pending;
    }

    private static DateTimeOffset? ResolveCapturedAtUtc(
        JsonElement payment,
        PaymentStatus status)
    {
        if (status != PaymentStatus.Captured)
            return null;

        var capturedAt = OptionalUnixSeconds(payment, "captured_at")
            ?? OptionalUnixSeconds(payment, "created_at");
        if (capturedAt is null)
            throw new ArgumentException("Captured webhook must include captured_at or created_at.");

        return capturedAt.Value;
    }

    private static DateTimeOffset? OptionalUnixSeconds(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;
        return value.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    private static string RequiredString(JsonElement element, string name)
        => OptionalString(element, name)
            ?? throw new ArgumentException($"Webhook payment field '{name}' is required.");

    private static long RequiredInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var result))
            throw new ArgumentException($"Webhook payment field '{name}' is required.");
        return result;
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool? OptionalBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? OptionalNote(JsonElement payment, string name)
    {
        if (!payment.TryGetProperty("notes", out var notes) ||
            notes.ValueKind != JsonValueKind.Object ||
            !notes.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static Guid? ParseOptionalGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new ArgumentException("Webhook subscriptionId note is invalid.");
    }
}
