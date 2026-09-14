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
