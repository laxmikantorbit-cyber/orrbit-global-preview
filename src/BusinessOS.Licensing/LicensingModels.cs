namespace BusinessOS.Licensing;

public sealed record EntitlementSnapshot(int DesktopSystems, int Locations, int WebAdminSeats, int FieldStaffSeats, bool MultiLocationCloud);
public sealed record DeviceActivation(Guid Id, string DeviceFingerprint, bool Active, DateTimeOffset ActivatedAt);

public sealed record LicenseLeasePayload(
    Guid LicenseId,
    string ProductCode,
    string DeviceFingerprint,
    DateTimeOffset LeaseIssuedAt,
    DateTimeOffset LeaseValidUntil,
    DateOnly SubscriptionValidUntil,
    EntitlementSnapshot Entitlements);
public sealed record SignedLicenseLease(
    string Algorithm,
    string PayloadBase64,
    string SignatureBase64);
public enum WebSeatKind
{
    Admin = 1,
    FieldStaff = 2
}

public sealed record WebSeatAssignment(
    string UserId,
    WebSeatKind Kind,
    bool Active);
