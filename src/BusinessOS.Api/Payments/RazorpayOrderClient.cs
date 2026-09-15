using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BusinessOS.Api.Payments;

public sealed record RazorpayOrderRequest(
    long Amount,
    string Currency,
    string Receipt,
    IReadOnlyDictionary<string, string> Notes);

public sealed record RazorpayOrderResult(
    string Id,
    long Amount,
    string Currency,
    string Receipt,
    string Status,
    IReadOnlyDictionary<string, string> Notes);

public interface IRazorpayOrderClient
{
    Task<RazorpayOrderResult> CreateOrderAsync(
        RazorpayOrderRequest request,
        CancellationToken cancellationToken = default);
}
public sealed class RazorpayHttpOrderClient : IRazorpayOrderClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public RazorpayHttpOrderClient(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<RazorpayOrderResult> CreateOrderAsync(
        RazorpayOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateOrderRequest(request);
        var keyId = RequiredConfig("Payments:RazorpayKeyId");
        var keySecret = RequiredConfig("Payments:RazorpayKeySecret");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/orders");
        var auth = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{keyId}:{keySecret}"));
        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", auth);
        httpRequest.Content = JsonContent.Create(new
        {
            amount = request.Amount,
            currency = request.Currency,
            receipt = request.Receipt,
            notes = request.Notes
        });

        using var response = await _httpClient.SendAsync(
            httpRequest,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Razorpay order creation failed with {(int)response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var id = RequiredString(root, "id");
        var amount = RequiredInt64(root, "amount");
        var currency = RequiredString(root, "currency");
        var receipt = OptionalString(root, "receipt") ?? request.Receipt;
        var status = OptionalString(root, "status") ?? "created";
        var notes = ParseNotes(root, request.Notes);

        return new RazorpayOrderResult(
            id,
            amount,
            currency,
            receipt,
            status,
            notes);
    }

    private string RequiredConfig(string key) =>
        string.IsNullOrWhiteSpace(_configuration[key])
            ? throw new InvalidOperationException($"Configuration '{key}' is missing.")
            : _configuration[key]!;

    internal static void ValidateOrderRequest(RazorpayOrderRequest request)
    {
        if (request.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Amount));
        if (string.IsNullOrWhiteSpace(request.Currency))
            throw new ArgumentException("Currency is required.");
        if (string.IsNullOrWhiteSpace(request.Receipt))
            throw new ArgumentException("Receipt is required.");
        if (request.Receipt.Length > 40)
            throw new ArgumentException("Razorpay receipt must be 40 characters or fewer.");
        if (request.Notes.Count == 0)
            throw new ArgumentException("Razorpay notes are required.");
    }

    private static string RequiredString(JsonElement element, string name)
        => OptionalString(element, name)
            ?? throw new ArgumentException($"Razorpay response field '{name}' is missing.");

    private static long RequiredInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var result))
            throw new ArgumentException($"Razorpay response field '{name}' is missing.");
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

    private static IReadOnlyDictionary<string, string> ParseNotes(
        JsonElement root,
        IReadOnlyDictionary<string, string> fallback)
    {
        if (!root.TryGetProperty("notes", out var notes) ||
            notes.ValueKind != JsonValueKind.Object)
            return fallback;

        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var note in notes.EnumerateObject())
        {
            if (note.Value.ValueKind == JsonValueKind.String)
            {
                var value = note.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    parsed[note.Name] = value;
            }
            else if (note.Value.ValueKind == JsonValueKind.Number)
            {
                parsed[note.Name] = note.Value.GetRawText();
            }
        }
        return parsed.Count == 0 ? fallback : parsed;
    }
}

public sealed class FreeTestingRazorpayOrderClient : IRazorpayOrderClient
{
    public Task<RazorpayOrderResult> CreateOrderAsync(
        RazorpayOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        RazorpayHttpOrderClient.ValidateOrderRequest(request);
        var suffix = Guid.NewGuid().ToString("N")[..24];
        return Task.FromResult(new RazorpayOrderResult(
            $"order_free_test_{suffix}",
            request.Amount,
            request.Currency,
            request.Receipt,
            "created",
            request.Notes));
    }
}

public sealed class FreeTestingAwareRazorpayOrderClient : IRazorpayOrderClient
{
    private readonly RazorpayHttpOrderClient _liveClient;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public FreeTestingAwareRazorpayOrderClient(
        HttpClient httpClient,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _liveClient = new RazorpayHttpOrderClient(httpClient, configuration);
        _configuration = configuration;
        _environment = environment;
    }

    public Task<RazorpayOrderResult> CreateOrderAsync(
        RazorpayOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (UseFreeTestingOrderSimulator())
            return new FreeTestingRazorpayOrderClient()
                .CreateOrderAsync(request, cancellationToken);

        return _liveClient.CreateOrderAsync(request, cancellationToken);
    }

    private bool UseFreeTestingOrderSimulator() =>
        !_environment.IsProduction() &&
        string.Equals(
            _configuration["BusinessOS:Payments:Mode"],
            "RazorpayTestPending",
            StringComparison.OrdinalIgnoreCase);
}
