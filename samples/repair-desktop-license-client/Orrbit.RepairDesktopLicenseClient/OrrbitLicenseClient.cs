using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Orrbit.RepairDesktopLicenseClient;

public sealed class OrrbitLicenseClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly DesktopLicenseSettings _settings;
    private readonly OfflineLeaseCache _cache;

    public OrrbitLicenseClient(DesktopLicenseSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress = new Uri(settings.ApiBaseUrl.TrimEnd('/') + "/");
        _cache = new OfflineLeaseCache(settings.ProductCode);
    }

    public async Task<DesktopDeviceLicenseResponse> ActivateAsync(
        string? deviceName = null,
        string? appVersion = null,
        CancellationToken cancellationToken = default)
    {
        _settings.ValidateForOnlineCall();
        var body = new DesktopDeviceActivationRequest(
            MachineFingerprint.Create(),
            deviceName ?? Environment.MachineName,
            appVersion);
        var response = await PostAsync<DesktopDeviceLicenseResponse>(
            $"api/desktop/licenses/{_settings.SubscriptionId}/activate",
            body,
            cancellationToken);
        if (response.Allowed)
            await _cache.SaveAsync(response, cancellationToken);
        return response;
    }

    public async Task<DesktopDeviceLicenseResponse> ValidateAsync(
        CancellationToken cancellationToken = default)
    {
        _settings.ValidateForOnlineCall();
        var cached = await _cache.LoadAsync(cancellationToken);
        var body = new DesktopDeviceValidationRequest(
            MachineFingerprint.Create(),
            cached?.Lease);
        var response = await PostAsync<DesktopDeviceLicenseResponse>(
            $"api/desktop/licenses/{_settings.SubscriptionId}/validate",
            body,
            cancellationToken);
        if (response.Allowed)
            await _cache.SaveAsync(response, cancellationToken);
        return response;
    }

    public async Task<bool> EnsureLicenseOrGraceAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var online = await ValidateAsync(cancellationToken);
            return online.Allowed;
        }
        catch
        {
            return _cache.HasUsableOfflineLease(DateTimeOffset.UtcNow);
        }
    }
    private async Task<T> PostAsync<T>(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", _settings.ApiBearerToken);
        request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"License API returned {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, JsonOptions)
            ?? throw new InvalidOperationException("License API returned empty response.");
    }
}
