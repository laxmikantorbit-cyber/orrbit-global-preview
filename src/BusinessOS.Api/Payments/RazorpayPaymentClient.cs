using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BusinessOS.Payments;

namespace BusinessOS.Api.Payments;

public sealed record RazorpayPaymentResult(
    string Id,
    string OrderId,
    long Amount,
    string Currency,
    string Status,
    bool Captured,
    DateTimeOffset CreatedAtUtc);

public interface IRazorpayPaymentClient
{
    Task<RazorpayPaymentResult> FetchPaymentAsync(
        string paymentId,
        CancellationToken cancellationToken = default);
}
public sealed class RazorpayHttpPaymentClient : IRazorpayPaymentClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public RazorpayHttpPaymentClient(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<RazorpayPaymentResult> FetchPaymentAsync(
        string paymentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
            throw new ArgumentException("Razorpay payment id is required.");
        var keyId = RequiredConfig("Payments:RazorpayKeyId");
        var keySecret = RequiredConfig("Payments:RazorpayKeySecret");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/v1/payments/{Uri.EscapeDataString(paymentId.Trim())}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{keyId}:{keySecret}")));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Razorpay payment fetch failed with {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        return Parse(document.RootElement);
    }

    private string RequiredConfig(string key) =>
        string.IsNullOrWhiteSpace(_configuration[key])
            ? throw new InvalidOperationException($"Configuration '{key}' is missing.")
            : _configuration[key]!;

    private static RazorpayPaymentResult Parse(JsonElement root)
    {
        var id = RequiredString(root, "id");
        var orderId = RequiredString(root, "order_id");
        var amount = RequiredInt64(root, "amount");
        var currency = RequiredString(root, "currency");
        var status = RequiredString(root, "status");
        var captured = OptionalBool(root, "captured")
            ?? string.Equals(status, "captured", StringComparison.OrdinalIgnoreCase);
        var createdAt = OptionalUnixSeconds(root, "captured_at")
            ?? OptionalUnixSeconds(root, "created_at")
            ?? throw new ArgumentException("Razorpay payment response timestamp is missing.");

        return new RazorpayPaymentResult(
            id,
            orderId,
            amount,
            currency,
            status,
            captured,
            createdAt);
    }

    public static PaymentWebhookMessage ToWebhookMessage(RazorpayPaymentResult payment) =>
        new(
            $"razorpay.fetch:{payment.Id}:{payment.Status}",
            payment.Id,
            payment.OrderId,
            ResolveStatus(payment),
            payment.Amount,
            payment.Currency,
            payment.Captured ? payment.CreatedAtUtc : null);

    private static PaymentStatus ResolveStatus(RazorpayPaymentResult payment)
    {
        if (payment.Captured || string.Equals(payment.Status, "captured", StringComparison.OrdinalIgnoreCase))
            return PaymentStatus.Captured;
        if (string.Equals(payment.Status, "failed", StringComparison.OrdinalIgnoreCase))
            return PaymentStatus.Failed;
        return PaymentStatus.Pending;
    }

    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name)
            ?? throw new ArgumentException($"Razorpay payment response field '{name}' is missing.");

    private static long RequiredInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var result))
            throw new ArgumentException($"Razorpay payment response field '{name}' is missing.");
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

    private static bool? OptionalBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

    private static DateTimeOffset? OptionalUnixSeconds(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;
        return value.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }
}
