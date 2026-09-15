using System.Text.Json;

namespace Orrbit.RepairDesktopLicenseClient;

public sealed class OfflineLeaseCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public OfflineLeaseCache(string productCode)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "oRRbit", productCode, "License");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "lease.json");
    }

    public async Task SaveAsync(DesktopDeviceLicenseResponse response, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        await File.WriteAllTextAsync(_path, json, cancellationToken);
    }

    public async Task<DesktopDeviceLicenseResponse?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        var json = await File.ReadAllTextAsync(_path, cancellationToken);
        return JsonSerializer.Deserialize<DesktopDeviceLicenseResponse>(json, JsonOptions);
    }

    public bool HasUsableOfflineLease(DateTimeOffset now)
    {
        var cached = LoadAsync().GetAwaiter().GetResult();
        return cached?.Allowed == true &&
            cached.LeaseValidUntil is not null &&
            cached.LeaseValidUntil.Value > now;
    }
}
