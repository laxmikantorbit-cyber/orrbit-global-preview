using System.Text.Json;

namespace BusinessOS.Api.Payments;

internal sealed record RazorpaySubscriptionWebhook(
    string EventName,
    string ProviderSubscriptionId,
    string Status);

internal static class RazorpaySubscriptionWebhookParser
{
    public static RazorpaySubscriptionWebhook? TryParse(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            throw new ArgumentException("Webhook body is required.");

        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;
        var eventName = OptionalString(root, "event");
        if (string.IsNullOrWhiteSpace(eventName) ||
            !eventName.StartsWith("subscription.", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!TrySubscriptionEntity(root, out var entity))
            throw new ArgumentException("Subscription webhook does not contain subscription entity.");
        var id = RequiredString(entity, "id");
        var status = RequiredString(entity, "status");
        return new RazorpaySubscriptionWebhook(eventName, id, status);
    }

    private static bool TrySubscriptionEntity(
        JsonElement root,
        out JsonElement entity)
    {
        if (root.TryGetProperty("payload", out var payload) &&
            payload.TryGetProperty("subscription", out var subscription) &&
            subscription.TryGetProperty("entity", out entity))
            return true;

        if (root.TryGetProperty("subscription", out var direct))
        {
            entity = direct;
            return true;
        }

        entity = default;
        return false;
    }
    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name)
        ?? throw new ArgumentException(
            $"Webhook subscription field '{name}' is required.");

    private static string? OptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString())
                ? null : value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }
}
