namespace BusinessOS.Identity;

public enum TenantStatus
{
    Active = 1,
    Suspended = 2,
    Closed = 3
}

public enum MembershipStatus
{
    Invited = 1,
    Active = 2,
    Suspended = 3,
    Revoked = 4
}

public sealed record Tenant(
    Guid Id,
    string Code,
    string Name,
    TenantStatus Status);

public sealed record UserIdentity(
    Guid Id,
    string Subject,
    string Email,
    string DisplayName,
    bool Active);

public sealed record TenantMembership(
    Guid Id,
    Guid UserId,
    Guid TenantId,
    string RoleCode,
    MembershipStatus Status);

public sealed record TenantAccess(
    Guid UserId,
    Guid TenantId,
    string RoleCode);
