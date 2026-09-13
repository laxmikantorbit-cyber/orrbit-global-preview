namespace BusinessOS.Catalog;

public enum CatalogStatus
{
    Active = 1,
    Inactive = 2,
    Archived = 3
}

public enum BillingCycle
{
    OneTime = 1,
    Monthly = 2,
    Annual = 3
}

public sealed record BillingRule(
    decimal Amount,
    string CurrencyCode,
    BillingCycle Cycle,
    int? TermMonths);

public sealed record EntitlementProfile(
    int DesktopDeviceLimit,
    int LocationLimit,
    int WebAdminSeats,
    int FieldStaffSeats,
    bool MultiLocationCloud,
    IReadOnlySet<string> Features);
