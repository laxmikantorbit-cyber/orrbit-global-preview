namespace BusinessOS.Catalog;

public sealed class PlanVersion
{
    public PlanVersion(
        Guid id,
        Guid tenantId,
        Guid planId,
        int versionNumber,
        BillingRule billing,
        EntitlementProfile entitlements,
        DateTimeOffset effectiveFromUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Plan version id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (planId == Guid.Empty) throw new ArgumentException("Plan id is required.", nameof(planId));
        if (versionNumber <= 0) throw new ArgumentOutOfRangeException(nameof(versionNumber));

        ValidateBilling(billing);
        ValidateEntitlements(entitlements);

        Id = id;
        TenantId = tenantId;
        PlanId = planId;
        VersionNumber = versionNumber;
        Billing = NormalizeBilling(billing);
        Entitlements = NormalizeEntitlements(entitlements);
        EffectiveFromUtc = effectiveFromUtc.ToUniversalTime();
    }
    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid PlanId { get; }
    public int VersionNumber { get; }
    public BillingRule Billing { get; }
    public EntitlementProfile Entitlements { get; }
    public DateTimeOffset EffectiveFromUtc { get; }

    private static void ValidateBilling(BillingRule billing)
    {
        ArgumentNullException.ThrowIfNull(billing);
        if (billing.Amount < 0) throw new ArgumentOutOfRangeException(nameof(billing));
        if (string.IsNullOrWhiteSpace(billing.CurrencyCode) || billing.CurrencyCode.Trim().Length != 3)
            throw new ArgumentException("Currency code must be three characters.", nameof(billing));
        if (!Enum.IsDefined(billing.Cycle)) throw new ArgumentOutOfRangeException(nameof(billing));
        if (billing.TermMonths is <= 0) throw new ArgumentOutOfRangeException(nameof(billing));
    }

    private static void ValidateEntitlements(EntitlementProfile entitlements)
    {
        ArgumentNullException.ThrowIfNull(entitlements);
        if (entitlements.DesktopDeviceLimit < 0 ||
            entitlements.LocationLimit < 0 ||
            entitlements.WebAdminSeats < 0 ||
            entitlements.FieldStaffSeats < 0)
            throw new ArgumentOutOfRangeException(nameof(entitlements));
    }

    private static BillingRule NormalizeBilling(BillingRule billing) =>
        billing with { CurrencyCode = billing.CurrencyCode.Trim().ToUpperInvariant() };

    private static EntitlementProfile NormalizeEntitlements(EntitlementProfile entitlements)
    {
        var features = new HashSet<string>(
            entitlements.Features
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant()),
            StringComparer.Ordinal);

        return entitlements with { Features = features };
    }
}
