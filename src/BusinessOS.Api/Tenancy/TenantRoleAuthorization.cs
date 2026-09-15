namespace BusinessOS.Api.Tenancy;

public static class TenantRoleAuthorization
{
    private static readonly HashSet<string> CommerceAdminRoles = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Owner",
        "Admin",
        "FinanceAdmin",
        "BillingAdmin"
    };

    public static bool CanAccessCommerceAdmin(TenantContext tenant) =>
        tenant is not null && CommerceAdminRoles.Contains(tenant.RoleCode.Trim());

    public static IResult? ForbidUnlessCommerceAdmin(TenantContext tenant) =>
        CanAccessCommerceAdmin(tenant)
            ? null
            : Results.StatusCode(StatusCodes.Status403Forbidden);
}
