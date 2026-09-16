using BusinessOS.Commerce;
using BusinessOS.Licensing;

namespace BusinessOS.Api.Commerce;

public sealed record InitialActivationRequest(
    Guid OrganisationId,
    string ProductCode,
    Guid? PlanId,
    Guid? PlanVersionId,
    int PlanVersionNumber,
    decimal Amount,
    string CurrencyCode,
    int TermMonths,
    int DesktopDeviceLimit,
    int LocationLimit,
    int WebAdminSeats,
    int FieldStaffSeats,
    bool MultiLocationCloud,
    string PaymentId,
    DateTimeOffset CapturedAtUtc);

public sealed record RenewalActivationRequest(
    Guid? PlanVersionId,
    int PlanVersionNumber,
    decimal Amount,
    string CurrencyCode,
    int TermMonths,
    int DesktopDeviceLimit,
    int LocationLimit,
    int WebAdminSeats,
    int FieldStaffSeats,
    bool MultiLocationCloud,
    string PaymentId,
    DateTimeOffset CapturedAtUtc);

public sealed record CreateInitialCheckoutOrderRequest(
    Guid OrganisationId,
    string ProductCode,
    Guid? PlanId,
    Guid? PlanVersionId,
    int PlanVersionNumber,
    decimal Amount,
    string CurrencyCode,
    int TermMonths,
    int DesktopDeviceLimit,
    int LocationLimit,
    int WebAdminSeats,
    int FieldStaffSeats,
    bool MultiLocationCloud);

public sealed record CreateRenewalCheckoutOrderRequest(
    Guid? PlanVersionId,
    int PlanVersionNumber,
    decimal Amount,
    string CurrencyCode,
    int TermMonths,
    int DesktopDeviceLimit,
    int LocationLimit,
    int WebAdminSeats,
    int FieldStaffSeats,
    bool MultiLocationCloud);
public sealed record CheckoutOrderResponse(
    Guid TenantId,
    Guid OrganisationId,
    Guid QuoteId,
    Guid CommerceOrderId,
    Guid PlanId,
    Guid PlanVersionId,
    decimal Amount,
    string CurrencyCode,
    string ProductCode,
    Guid? SubscriptionId,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyDictionary<string, string> RazorpayNotes);

public sealed record ProviderOrderRoute(
    string Provider,
    string ProviderOrderId,
    Guid TenantId,
    Guid CommerceOrderId,
    string ProductCode,
    Guid? SubscriptionId);

public sealed record ProviderOrderStatus(
    ProviderOrderRoute Route,
    string Outcome,
    ActivationResponse? InitialActivation,
    RenewalResponse? RenewalActivation);

public sealed record CommerceAdminSnapshot(
    Guid TenantId,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<CommerceAdminOrderSnapshot> Orders,
    IReadOnlyList<ActivationResponse> Activations,
    IReadOnlyList<RenewalResponse> Renewals);

public sealed record CommerceAdminOrderSnapshot(
    Guid TenantId,
    Guid OrganisationId,
    Guid QuoteId,
    Guid CommerceOrderId,
    Guid PlanId,
    Guid PlanVersionId,
    decimal Amount,
    string CurrencyCode,
    string OrderStatus,
    string? PaymentId,
    DateTimeOffset? PaidAtUtc,
    string? RazorpayOrderId,
    string? Provider,
    string? ProviderOrderId,
    string? ProductCode,
    Guid? SubscriptionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record RazorpayCheckoutOrderResponse(
    Guid TenantId,
    Guid OrganisationId,
    Guid QuoteId,
    Guid CommerceOrderId,
    Guid PlanId,
    Guid PlanVersionId,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset ExpiresAtUtc,
    string RazorpayOrderId,
    string RazorpayKeyId,
    long RazorpayAmount,
    string Receipt,
    string RazorpayStatus,
    IReadOnlyDictionary<string, string> RazorpayNotes);

public sealed record ActivationResponse(
    Guid TenantId,
    Guid OrganisationId,
    Guid OrderId,
    Guid SubscriptionId,
    Guid LicenseId,
    string ProductCode,
    DateOnly StartsOn,
    DateOnly ValidUntil,
    EntitlementSnapshot Entitlements);

public sealed record RenewalResponse(
    Guid TenantId,
    Guid SubscriptionId,
    Guid RenewalId,
    Guid OrderId,
    Guid PlanVersionId,
    DateOnly PreviousValidUntil,
    DateOnly NewValidUntil,
    EntitlementSnapshot Entitlements);

public sealed record CommerceAdminStatusResponse(
    Guid TenantId,
    DateTimeOffset GeneratedAtUtc,
    CommerceAdminCounts Counts,
    IReadOnlyList<CommerceAdminOrderStatusItem> Orders,
    IReadOnlyList<CommerceAdminPaymentItem> Payments,
    IReadOnlyList<ActivationResponse> Activations,
    IReadOnlyList<RenewalResponse> Renewals);

public sealed record CommerceAdminCounts(
    int PendingOrders,
    int CapturedPayments,
    int FailedPayments,
    int ActiveSubscriptions,
    int Renewals,
    int NeedsReconciliation);

public sealed record CommerceAdminOrderStatusItem(
    CommerceAdminOrderSnapshot Order,
    string? PaymentStatus,
    string ReconciliationStatus);

public sealed record CommerceAdminPaymentItem(
    string Provider,
    string PaymentId,
    string ProviderOrderId,
    string Status,
    long AmountSubunits,
    string CurrencyCode,
    DateTimeOffset? CapturedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ErrorResponse(string Error);

public sealed record SubscriptionStateSnapshot(
    Guid TenantId,
    Guid OrganisationId,
    Guid SubscriptionId,
    Guid LicenseId,
    string ProductCode,
    Guid PlanId,
    Guid PlanVersionId,
    DateOnly StartsOn,
    DateOnly ValidUntil,
    EntitlementSnapshot Entitlements,
    SubscriptionStatus SubscriptionStatus);

public sealed record EntitlementStatusResponse(
    Guid TenantId,
    Guid OrganisationId,
    Guid SubscriptionId,
    Guid LicenseId,
    string ProductCode,
    Guid PlanId,
    Guid PlanVersionId,
    DateOnly StartsOn,
    DateOnly ValidUntil,
    EntitlementSnapshot Entitlements,
    string Status,
    string RenewalStatus,
    bool AutoRenewEnabled,
    bool CancelAtPeriodEnd,
    DateOnly? GraceEndsOn);

public sealed record DesktopDeviceActivationRequest(
    string DeviceFingerprint,
    string? DeviceName,
    string? AppVersion);

public sealed record DesktopDeviceValidationRequest(
    string DeviceFingerprint,
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

public sealed record DesktopDeviceActivationSnapshot(
    Guid Id,
    string DeviceFingerprint,
    string? DeviceName,
    string? AppVersion,
    bool Active,
    DateTimeOffset ActivatedAtUtc,
    DateTimeOffset? LastValidatedAtUtc);

public sealed record LicenseActivationCodeResponse(
    Guid TenantId,
    Guid OrganisationId,
    Guid SubscriptionId,
    Guid LicenseId,
    string ProductCode,
    string ActivationCode,
    DateTimeOffset CreatedAtUtc);

public sealed record DesktopActivationCodeRequest(
    string ActivationCode,
    string DeviceFingerprint,
    string? DeviceName,
    string? AppVersion,
    SignedLicenseLease? CurrentLease);
