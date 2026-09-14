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

public sealed record ErrorResponse(string Error);
