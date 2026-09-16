namespace Orrbit.RepairDesktopLicenseClient;

public sealed record DesktopLicenseSettings(
    string ApiBaseUrl,
    string ActivationCode,
    string ProductCode = "AI_REPAIR")
{
    public static DesktopLicenseSettings FromEnvironment() =>
        new(
            Environment.GetEnvironmentVariable("ORRBIT_LICENSE_API") ??
                "https://businessos-commerce-api-live.onrender.com",
            Environment.GetEnvironmentVariable("ORRBIT_ACTIVATION_CODE") ?? string.Empty,
            Environment.GetEnvironmentVariable("ORRBIT_PRODUCT_CODE") ?? "AI_REPAIR");

    public void ValidateForOnlineCall()
    {
        if (string.IsNullOrWhiteSpace(ApiBaseUrl))
            throw new InvalidOperationException("License API base URL is required.");
        if (string.IsNullOrWhiteSpace(ActivationCode))
            throw new InvalidOperationException("Activation code is required.");
    }
}
