using BusinessOS.Identity;

namespace BusinessOS.Api.Tenancy;

public sealed class TenantContext
{
    public const string ItemKey = "BusinessOS.TenantAccess";
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContext(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public TenantAccess Access =>
        _httpContextAccessor.HttpContext?.Items[ItemKey] as TenantAccess
        ?? throw new InvalidOperationException("Tenant context is not available.");

    public Guid UserId => Access.UserId;
    public Guid TenantId => Access.TenantId;
    public string TenantCode => Access.TenantCode;
    public string RoleCode => Access.RoleCode;
}
