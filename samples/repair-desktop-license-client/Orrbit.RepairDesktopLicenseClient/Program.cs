using Orrbit.RepairDesktopLicenseClient;

var settings = DesktopLicenseSettings.FromEnvironment();
Console.WriteLine("oRRbit Desktop License Client Smoke");
Console.WriteLine($"API: {settings.ApiBaseUrl}");
Console.WriteLine($"Subscription: {(string.IsNullOrWhiteSpace(settings.SubscriptionId) ? "not set" : settings.SubscriptionId)}");
Console.WriteLine($"Fingerprint: {MachineFingerprint.Create()}");

if (args.Contains("--offline-check"))
{
    var cache = new OfflineLeaseCache(settings.ProductCode);
    Console.WriteLine(cache.HasUsableOfflineLease(DateTimeOffset.UtcNow)
        ? "Offline lease is usable."
        : "No usable offline lease found.");
    return;
}

settings.ValidateForOnlineCall();
var client = new OrrbitLicenseClient(settings);
var result = args.Contains("--activate")
    ? await client.ActivateAsync(Environment.MachineName, "3.1.108.62")
    : await client.ValidateAsync();

Console.WriteLine($"Allowed: {result.Allowed}");
Console.WriteLine($"Reason: {result.Reason}");
Console.WriteLine($"Status: {result.Status}");
Console.WriteLine($"ValidUntil: {result.ValidUntil}");
Console.WriteLine($"LeaseValidUntil: {result.LeaseValidUntil}");
