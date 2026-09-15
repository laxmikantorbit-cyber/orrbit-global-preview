using System.Security.Cryptography;
using System.Text;

namespace Orrbit.RepairDesktopLicenseClient;

public static class MachineFingerprint
{
    public static string Create()
    {
        var parts = new[]
        {
            Environment.MachineName,
            Environment.UserDomainName,
            Environment.ProcessorCount.ToString(),
            Environment.OSVersion.VersionString,
            Environment.GetFolderPath(Environment.SpecialFolder.System)
        };

        var raw = string.Join("|", parts.Select(x => x?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw.ToUpperInvariant()));
        return "ORRBIT-" + Convert.ToHexString(hash)[..32];
    }
}
