namespace Orrbit.RepairDesktopLicenseClient;

public sealed record DesktopActivationCodeRequest(
    string ActivationCode,
    string DeviceFingerprint,
    string? DeviceName,
    string? AppVersion,
    SignedLicenseLease? CurrentLease);

public sealed record DesktopDeviceLicenseResponse(
    Guid TenantId,
    Guid OrganisationId,
    Guid SubscriptionId,
    Guid LicenseId,
    string ProductCode,
    string DeviceFingerprint,
    string? DeviceName,
    string Status,
    string RenewalStatus,
    bool Allowed,
    string Reason,
    int ActiveDesktopDevices,
    int DesktopDeviceLimit,
    DateOnly StartsOn,
    DateOnly ValidUntil,
    DateTimeOffset? LeaseValidUntil,
    SignedLicenseLease? Lease,
    string PublicKeyBase64,
    EntitlementSnapshot Entitlements);

public sealed record SignedLicenseLease(
    string Algorithm,
    string PayloadBase64,
    string SignatureBase64);

public sealed record EntitlementSnapshot(
    int DesktopSystems,
    int Locations,
    int WebAdminSeats,
    int FieldStaffSeats,
    bool MultiLocationCloud);
