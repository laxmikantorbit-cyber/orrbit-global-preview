namespace BusinessOS.Licensing;

public sealed class LicenseEngine
{
    private readonly LeaseSigner _signer;
    private readonly List<DeviceActivation> _activations = [];

    public Guid LicenseId { get; }
    public string ProductCode { get; }
    public DateOnly StartsOn { get; private set; }
    public DateOnly ValidUntil { get; private set; }
    public EntitlementSnapshot Entitlements { get; private set; }
    public IReadOnlyList<DeviceActivation> Activations => _activations.AsReadOnly();

    private LicenseEngine(
        Guid licenseId,
        string productCode,
        DateOnly startsOn,
        DateOnly validUntil,
        EntitlementSnapshot entitlements,
        LeaseSigner signer)
    {
        if (licenseId == Guid.Empty)
            throw new ArgumentException("License id is required.", nameof(licenseId));
        LicenseId = licenseId;
        ProductCode = productCode;
        StartsOn = startsOn;
        ValidUntil = validUntil;
        Entitlements = entitlements;
        _signer = signer;
    }

    public static LicenseEngine FromVerifiedPurchase(
        string productCode,
        DateOnly purchaseDate,
        EntitlementSnapshot entitlements,
        LeaseSigner signer)
        => FromVerifiedSubscription(
            productCode,
            purchaseDate,
            purchaseDate.AddYears(1).AddDays(-1),
            entitlements,
            signer);

    public static LicenseEngine FromVerifiedSubscription(
        string productCode,
        DateOnly startsOn,
        DateOnly validUntil,
        EntitlementSnapshot entitlements,
        LeaseSigner signer)
    {
        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("Product code is required.");
        ValidateTerm(startsOn, validUntil);
        ValidateEntitlements(entitlements);
        ArgumentNullException.ThrowIfNull(signer);

        return new LicenseEngine(
            Guid.NewGuid(), productCode.Trim(), startsOn, validUntil, entitlements, signer);
    }

    public static LicenseEngine FromPersistedSubscription(
        Guid licenseId,
        string productCode,
        DateOnly startsOn,
        DateOnly validUntil,
        EntitlementSnapshot entitlements,
        LeaseSigner signer)
    {
        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("Product code is required.");
        ValidateTerm(startsOn, validUntil);
        ValidateEntitlements(entitlements);
        ArgumentNullException.ThrowIfNull(signer);
        return new LicenseEngine(
            licenseId, productCode.Trim(), startsOn, validUntil, entitlements, signer);
    }

    public SignedLicenseLease Activate(string deviceFingerprint, DateTimeOffset now)
    {
        EnsureActiveSubscription(now);
        if (string.IsNullOrWhiteSpace(deviceFingerprint))
            throw new ArgumentException("Device fingerprint is required.");
        if (_activations.Any(x => x.Active && x.DeviceFingerprint == deviceFingerprint))
            return CreateLease(deviceFingerprint, now);
        if (_activations.Count(x => x.Active) >= Entitlements.DesktopSystems)
            throw new InvalidOperationException("No desktop device entitlement is available.");

        _activations.Add(new DeviceActivation(
            Guid.NewGuid(), deviceFingerprint, Active: true, now));
        return CreateLease(deviceFingerprint, now);
    }

    public SignedLicenseLease ReplaceDevice(
        string oldFingerprint,
        string newFingerprint,
        DateTimeOffset now)
    {
        EnsureActiveSubscription(now);
        var index = _activations.FindLastIndex(
            x => x.Active && x.DeviceFingerprint == oldFingerprint);
        if (index < 0)
            throw new InvalidOperationException("Old device is not active.");

        _activations[index] = _activations[index] with { Active = false };
        return Activate(newFingerprint, now);
    }

    public bool DeactivateDevice(string deviceFingerprint)
    {
        if (string.IsNullOrWhiteSpace(deviceFingerprint))
            throw new ArgumentException("Device fingerprint is required.");
        var index = _activations.FindLastIndex(
            x => x.Active && x.DeviceFingerprint == deviceFingerprint);
        if (index < 0) return false;
        _activations[index] = _activations[index] with { Active = false };
        return true;
    }

    public void AddDesktopSystems(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        Entitlements = Entitlements with
        {
            DesktopSystems = Entitlements.DesktopSystems + quantity
        };
    }

    public void EnsureEntitlementsCanBeApplied(EntitlementSnapshot entitlements)
    {
        ValidateEntitlements(entitlements);
        var activeDesktopSystems = _activations.Count(x => x.Active);
        if (activeDesktopSystems > entitlements.DesktopSystems)
            throw new InvalidOperationException(
                "Active desktop devices exceed the new entitlement limit.");
    }

    public void SynchronizeSubscription(
        DateOnly startsOn,
        DateOnly validUntil,
        EntitlementSnapshot entitlements)
    {
        ValidateTerm(startsOn, validUntil);
        EnsureEntitlementsCanBeApplied(entitlements);
        StartsOn = startsOn;
        ValidUntil = validUntil;
        Entitlements = entitlements;
    }

    public void Renew(DateOnly paymentDate)
    {
        if (paymentDate <= ValidUntil)
        {
            ValidUntil = ValidUntil.AddYears(1);
            return;
        }

        StartsOn = paymentDate;
        ValidUntil = paymentDate.AddYears(1).AddDays(-1);
    }

    public byte[] ExportPublicKey()
    {
        return _signer.ExportPublicKey();
    }

    private SignedLicenseLease CreateLease(
        string deviceFingerprint,
        DateTimeOffset now)
    {
        var subscriptionEnd = new DateTimeOffset(
            ValidUntil.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
        var leaseEnd = now.AddDays(7);
        if (leaseEnd > subscriptionEnd)
            leaseEnd = subscriptionEnd;

        var payload = new LicenseLeasePayload(
            LicenseId,
            ProductCode,
            deviceFingerprint,
            now,
            leaseEnd,
            ValidUntil,
            Entitlements);
        return _signer.Sign(payload);
    }

    private void EnsureActiveSubscription(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (today < StartsOn)
            throw new InvalidOperationException("Subscription has not started.");
        if (today > ValidUntil)
            throw new InvalidOperationException("Subscription has expired.");
    }

    private static void ValidateTerm(DateOnly startsOn, DateOnly validUntil)
    {
        if (validUntil < startsOn)
            throw new ArgumentException("Subscription expiry cannot precede its start date.");
    }

    private static void ValidateEntitlements(EntitlementSnapshot entitlements)
    {
        ArgumentNullException.ThrowIfNull(entitlements);
        if (entitlements.DesktopSystems < 1)
            throw new ArgumentOutOfRangeException(nameof(entitlements));
    }
}
