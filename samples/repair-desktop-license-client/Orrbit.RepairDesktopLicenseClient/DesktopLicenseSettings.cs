namespace Orrbit.RepairDesktopLicenseClient;

public sealed record DesktopLicenseSettings(
    string ApiBaseUrl,
    string SubscriptionId,
    string ApiBearerToken,
    string ProductCode = "AI_REPAIR")
{
    public static DesktopLicenseSettings FromEnvironment() =>
        new(
            Environment.GetEnvironmentVariable("ORRBIT_LICENSE_API") ??
                "https://businessos-commerce-api-live.onrender.com",
            Environment.GetEnvironmentVariable("ORRBIT_SUBSCRIPTION_ID") ?? string.Empty,
            Environment.GetEnvironmentVariable("ORRBIT_LICENSE_TOKEN") ?? string.Empty,
            Environment.GetEnvironmentVariable("ORRBIT_PRODUCT_CODE") ?? "AI_REPAIR");

    public void ValidateForOnlineCall()
    {
        if (string.IsNullOrWhiteSpace(ApiBaseUrl))
            throw new InvalidOperationException("License API base URL is required.");
        if (string.IsNullOrWhiteSpace(SubscriptionId))
            throw new InvalidOperationException("Subscription id is required.");
        if (string.IsNullOrWhiteSpace(ApiBearerToken))
            throw new InvalidOperationException("Staging/API bearer token is required for protected desktop endpoints.");
    }
}
