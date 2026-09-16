using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BusinessOS.Api.Payments;

public sealed record RazorpaySubscriptionRequest(
    string PlanId,
    int TotalCount,
    int Quantity,
    bool CustomerNotify,
    long StartAtUnix,
    IReadOnlyDictionary<string, string> Notes);

public sealed record RazorpaySubscriptionResult(
    string Id,
    string PlanId,
    string Status,
    string? ShortUrl,
    long StartAtUnix,
    int TotalCount,
    IReadOnlyDictionary<string, string> Notes);

public sealed record RazorpaySubscriptionCancelResult(
    string Id,
    string Status);

public interface IRazorpaySubscriptionClient
{
    Task<RazorpaySubscriptionResult> CreateSubscriptionAsync(
        RazorpaySubscriptionRequest request,
        CancellationToken cancellationToken = default);

    Task<RazorpaySubscriptionCancelResult> CancelSubscriptionAsync(
        string subscriptionId,
        bool cancelAtCycleEnd,
        CancellationToken cancellationToken = default);
}

public sealed class RazorpayHttpSubscriptionClient : IRazorpaySubscriptionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public RazorpayHttpSubscriptionClient(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<RazorpaySubscriptionResult> CreateSubscriptionAsync(
        RazorpaySubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var keyId = RequiredConfig("Payments:RazorpayKeyId");
        var keySecret = RequiredConfig("Payments:RazorpayKeySecret");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/subscriptions");
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{keyId}:{keySecret}"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
        httpRequest.Content = JsonContent.Create(new
        {
            plan_id = request.PlanId,
            total_count = request.TotalCount,
            quantity = request.Quantity,
            customer_notify = request.CustomerNotify ? 1 : 0,
            start_at = request.StartAtUnix,
            notes = request.Notes
        });
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Razorpay subscription creation failed with {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        return new RazorpaySubscriptionResult(
            RequiredString(root, "id"),
            OptionalString(root, "plan_id") ?? request.PlanId,
            OptionalString(root, "status") ?? "created",
            OptionalString(root, "short_url"),
            OptionalInt64(root, "start_at") ?? request.StartAtUnix,
            OptionalInt32(root, "total_count") ?? request.TotalCount,
            ParseNotes(root, request.Notes));
    }

    public async Task<RazorpaySubscriptionCancelResult> CancelSubscriptionAsync(
        string subscriptionId,
        bool cancelAtCycleEnd,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
            throw new ArgumentException("Razorpay subscription id is required.");
        var keyId = RequiredConfig("Payments:RazorpayKeyId");
        var keySecret = RequiredConfig("Payments:RazorpayKeySecret");
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/subscriptions/{Uri.EscapeDataString(subscriptionId.Trim())}/cancel");
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{keyId}:{keySecret}"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
        httpRequest.Content = JsonContent.Create(new { cancel_at_cycle_end = cancelAtCycleEnd });
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Razorpay subscription cancellation failed with {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        return new RazorpaySubscriptionCancelResult(
            RequiredString(root, "id"),
            OptionalString(root, "status") ?? "cancelled");
    }

    private string RequiredConfig(string key) =>
        string.IsNullOrWhiteSpace(_configuration[key])
            ? throw new InvalidOperationException($"Configuration '{key}' is missing.")
            : _configuration[key]!;

    internal static void Validate(RazorpaySubscriptionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PlanId))
            throw new ArgumentException("Razorpay plan id is required.");
        if (request.TotalCount <= 0 || request.Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Total count and quantity must be positive.");
        if (request.StartAtUnix <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Start time is required.");
        if (request.Notes.Count == 0)
            throw new ArgumentException("Razorpay subscription notes are required.");
    }

    private static string RequiredString(JsonElement root, string name) =>
        OptionalString(root, name)
        ?? throw new ArgumentException($"Razorpay response field '{name}' is missing.");

    private static string? OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static long? OptionalInt64(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result : null;

    private static int? OptionalInt32(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result : null;

    private static IReadOnlyDictionary<string, string> ParseNotes(
        JsonElement root,
        IReadOnlyDictionary<string, string> fallback)
    {
        if (!root.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Object)
            return fallback;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in notes.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value)) result[property.Name] = value;
            }
            else if (property.Value.ValueKind == JsonValueKind.Number)
            {
                result[property.Name] = property.Value.GetRawText();
            }
        }
        return result.Count == 0 ? fallback : result;
    }
}

public sealed class FreeTestingRazorpaySubscriptionClient : IRazorpaySubscriptionClient
{
    public Task<RazorpaySubscriptionResult> CreateSubscriptionAsync(
        RazorpaySubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        RazorpayHttpSubscriptionClient.Validate(request);
        var suffix = Guid.NewGuid().ToString("N")[..24];
        return Task.FromResult(new RazorpaySubscriptionResult(
            $"sub_free_test_{suffix}",
            request.PlanId,
            "created",
            null,
            request.StartAtUnix,
            request.TotalCount,
            request.Notes));
    }

    public Task<RazorpaySubscriptionCancelResult> CancelSubscriptionAsync(
        string subscriptionId,
        bool cancelAtCycleEnd,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
            throw new ArgumentException("Razorpay subscription id is required.");
        return Task.FromResult(new RazorpaySubscriptionCancelResult(
            subscriptionId.Trim(),
            cancelAtCycleEnd ? "active" : "cancelled"));
    }
}

public sealed class FreeTestingAwareRazorpaySubscriptionClient : IRazorpaySubscriptionClient
{
    private readonly RazorpayHttpSubscriptionClient _liveClient;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public FreeTestingAwareRazorpaySubscriptionClient(
        HttpClient httpClient,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _liveClient = new RazorpayHttpSubscriptionClient(httpClient, configuration);
        _configuration = configuration;
        _environment = environment;
    }

    public Task<RazorpaySubscriptionResult> CreateSubscriptionAsync(
        RazorpaySubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (UseFreeTestingSimulator())
            return new FreeTestingRazorpaySubscriptionClient()
                .CreateSubscriptionAsync(request, cancellationToken);

        return _liveClient.CreateSubscriptionAsync(request, cancellationToken);
    }

    public Task<RazorpaySubscriptionCancelResult> CancelSubscriptionAsync(
        string subscriptionId,
        bool cancelAtCycleEnd,
        CancellationToken cancellationToken = default)
    {
        if (UseFreeTestingSimulator())
            return new FreeTestingRazorpaySubscriptionClient()
                .CancelSubscriptionAsync(subscriptionId, cancelAtCycleEnd, cancellationToken);

        return _liveClient.CancelSubscriptionAsync(
            subscriptionId, cancelAtCycleEnd, cancellationToken);
    }

    private bool UseFreeTestingSimulator() =>
        !_environment.IsProduction() &&
        string.Equals(
            _configuration["BusinessOS:Payments:Mode"],
            "RazorpayTestPending",
            StringComparison.OrdinalIgnoreCase);
}
