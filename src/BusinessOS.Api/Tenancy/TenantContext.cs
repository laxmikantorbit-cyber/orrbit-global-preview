namespace BusinessOS.Api.Tenancy;

public sealed class TenantContext
{
    public const string ItemKey = "BusinessOS.TenantId";
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string TenantId =>
        _httpContextAccessor.HttpContext?.Items[ItemKey] as string
        ?? throw new InvalidOperationException("Tenant context is not available.");
}
