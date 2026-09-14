using BusinessOS.Catalog;
using BusinessOS.Licensing;

namespace BusinessOS.Commerce;

public enum QuoteStatus { Draft = 1, Accepted = 2, Cancelled = 3 }
public enum OrderStatus { PendingPayment = 1, Paid = 2, Activated = 3, Cancelled = 4 }
public enum SubscriptionStatus { Active = 1, Cancelled = 2 }

public sealed record CommercialSnapshot(
    Guid PlanId,
    Guid PlanVersionId,
    int PlanVersionNumber,
    BillingRule Billing,
    EntitlementProfile Entitlements)
{
    public static CommercialSnapshot From(PlanVersion version) =>
        new(version.PlanId, version.Id, version.VersionNumber,
            version.Billing,
            version.Entitlements with { Features = new HashSet<string>(version.Entitlements.Features, StringComparer.Ordinal) });

    public EntitlementSnapshot ToLicensingSnapshot() => new(
        Entitlements.DesktopDeviceLimit,
        Entitlements.LocationLimit,
        Entitlements.WebAdminSeats,
        Entitlements.FieldStaffSeats,
        Entitlements.MultiLocationCloud);
}
