using BusinessOS.Licensing;

namespace BusinessOS.Licensing.Tests;

public sealed class LicensingLifecycleTests
{
    private static readonly DateOnly PurchaseDate = new(2026, 9, 10);
    private static readonly DateTimeOffset ActivationTime = new(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Verified_Purchase_Creates_One_Year_Term()
    {
        using var signer = new LeaseSigner();
        var license = CreateLicense(signer);

        Assert.Equal(PurchaseDate, license.StartsOn);
        Assert.Equal(new DateOnly(2027, 9, 9), license.ValidUntil);
    }

    [Fact]
    public void First_Device_Activates_And_Signed_Lease_Verifies_Offline()
    {
        using var signer = new LeaseSigner();
        var license = CreateLicense(signer);
        var token = license.Activate("DEVICE-A", ActivationTime);

        var payload = LeaseSigner.Verify(token, license.ExportPublicKey(), "DEVICE-A", ActivationTime.AddDays(3));
        Assert.NotNull(payload);
        Assert.Equal(license.LicenseId, payload!.LicenseId);
    }
    [Fact]
    public void Second_Device_Is_Blocked_When_Only_One_System_Was_Purchased()
    {
        using var signer = new LeaseSigner();
        var license = CreateLicense(signer);
        license.Activate("DEVICE-A", ActivationTime);

        Assert.Throws<InvalidOperationException>(() =>
            license.Activate("DEVICE-B", ActivationTime.AddMinutes(1)));
    }

    [Fact]
    public void Same_Device_Reinstall_Does_Not_Consume_Another_Seat()
    {
        using var signer = new LeaseSigner();
        var license = CreateLicense(signer);
        license.Activate("DEVICE-A", ActivationTime);
        var secondLease = license.Activate("DEVICE-A", ActivationTime.AddHours(2));

        Assert.Single(license.Activations, x => x.Active);
        Assert.NotNull(LeaseSigner.Verify(secondLease, license.ExportPublicKey(), "DEVICE-A", ActivationTime.AddDays(1)));
    }
    [Fact]
    public void Device_Replacement_Preserves_Subscription_Expiry()
    {
        using var signer = new LeaseSigner();
        var license = CreateLicense(signer);
        license.Activate("DEVICE-A", ActivationTime);
        var originalExpiry = license.ValidUntil;

        var token = license.ReplaceDevice("DEVICE-A", "DEVICE-B", ActivationTime.AddMonths(3));

        Assert.Equal(originalExpiry, license.ValidUntil);
        Assert.DoesNotContain(license.Activations, x => x.Active && x.DeviceFingerprint == "DEVICE-A");
        Assert.Contains(license.Activations, x => x.Active && x.DeviceFingerprint == "DEVICE-B");
        Assert.NotNull(LeaseSigner.Verify(token, license.ExportPublicKey(), "DEVICE-B", ActivationTime.AddMonths(3).AddDays(1)));
    }

    [Fact]
    public void Additional_System_Entitlement_Allows_Second_Device()
    {
        using var signer = new LeaseSigner();
        var license = CreateLicense(signer);
        license.Activate("DEVICE-A", ActivationTime);
        license.AddDesktopSystems(1);
        license.Activate("DEVICE-B", ActivationTime.AddMinutes(5));

        Assert.Equal(2, license.Activations.Count(x => x.Active));
    }
    [Fact]
    public void Renewal_Before_Expiry_Extends_From_Existing_Expiry()
    {
        using var signer = new LeaseSigner();
        var license = CreateLicense(signer);

        license.Renew(new DateOnly(2027, 9, 1));

        Assert.Equal(new DateOnly(2028, 9, 9), license.ValidUntil);
    }

    [Fact]
    public void Renewal_After_Expiry_Starts_A_New_One_Year_Term()
    {
        using var signer = new LeaseSigner();
        var license = CreateLicense(signer);

        license.Renew(new DateOnly(2027, 10, 20));

        Assert.Equal(new DateOnly(2027, 10, 20), license.StartsOn);
        Assert.Equal(new DateOnly(2028, 10, 19), license.ValidUntil);
    }
    [Fact]
    public void Offline_Lease_Expires_After_Configured_Seven_Day_Window()
    {
        using var signer = new LeaseSigner();
        var license = CreateLicense(signer);
        var token = license.Activate("DEVICE-A", ActivationTime);
        var publicKey = license.ExportPublicKey();

        Assert.NotNull(LeaseSigner.Verify(token, publicKey, "DEVICE-A", ActivationTime.AddDays(6)));
        Assert.Null(LeaseSigner.Verify(token, publicKey, "DEVICE-A", ActivationTime.AddDays(8)));
    }

    [Fact]
    public void Tampered_Lease_Is_Rejected()
    {
        using var signer = new LeaseSigner();
        var license = CreateLicense(signer);
        var token = license.Activate("DEVICE-A", ActivationTime);
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token.PayloadBase64));
        var changed = json.Replace("DEVICE-A", "DEVICE-X", StringComparison.Ordinal);
        var tampered = token with { PayloadBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(changed)) };

        Assert.Null(LeaseSigner.Verify(tampered, license.ExportPublicKey(), "DEVICE-X", ActivationTime.AddDays(1)));
    }
    private static LicenseEngine CreateLicense(LeaseSigner signer)
    {
        var entitlements = new EntitlementSnapshot(
            DesktopSystems: 1,
            Locations: 1,
            WebAdminSeats: 0,
            FieldStaffSeats: 0,
            MultiLocationCloud: false);

        return LicenseEngine.FromVerifiedPurchase(
            "ORRBIT-REPAIR",
            PurchaseDate,
            entitlements,
            signer);
    }
}

