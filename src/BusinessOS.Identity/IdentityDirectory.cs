namespace BusinessOS.Identity;

public sealed class IdentityDirectory
{
    private readonly Dictionary<Guid, Tenant> _tenants;
    private readonly Dictionary<string, Tenant> _tenantsByCode;
    private readonly Dictionary<Guid, UserIdentity> _users;
    private readonly List<TenantMembership> _memberships;
    private readonly Dictionary<string, UserIdentity> _usersBySubject;

    public IdentityDirectory(
        IEnumerable<Tenant> tenants,
        IEnumerable<UserIdentity> users,
        IEnumerable<TenantMembership> memberships)
    {
        _tenants = tenants.ToDictionary(x => x.Id);
        _users = users.ToDictionary(x => x.Id);
        _memberships = memberships.ToList();
        _tenantsByCode = new Dictionary<string, Tenant>(StringComparer.OrdinalIgnoreCase);
        _usersBySubject = new Dictionary<string, UserIdentity>(StringComparer.Ordinal);

        foreach (var tenant in _tenants.Values)
        {
            if (string.IsNullOrWhiteSpace(tenant.Code))
                throw new InvalidOperationException("Tenant code is required.");
            if (!_tenantsByCode.TryAdd(tenant.Code, tenant))
                throw new InvalidOperationException("Tenant code must be unique.");
        }

        foreach (var user in _users.Values)
        {
            if (string.IsNullOrWhiteSpace(user.Subject))
                throw new InvalidOperationException("OIDC subject is required.");
            if (!_usersBySubject.TryAdd(user.Subject, user))
                throw new InvalidOperationException("OIDC subject must be unique.");
        }

        ValidateMemberships();
    }

    public UserIdentity? FindBySubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;
        return _usersBySubject.GetValueOrDefault(subject);
    }

    public TenantAccess? ResolveAccess(string subject, string tenantCode)
    {
        var user = FindBySubject(subject);
        if (user is null) return null;
        if (string.IsNullOrWhiteSpace(tenantCode)) return null;
        if (!_tenantsByCode.TryGetValue(tenantCode, out var tenant)) return null;
        return ResolveAccess(user.Id, tenant.Id);
    }

    public TenantAccess? ResolveAccess(Guid userId, Guid tenantId)
    {
        if (!_users.TryGetValue(userId, out var user) || !user.Active) return null;
        if (!_tenants.TryGetValue(tenantId, out var tenant) || tenant.Status != TenantStatus.Active) return null;

        var membership = _memberships.SingleOrDefault(x =>
            x.UserId == userId && x.TenantId == tenantId && x.Status == MembershipStatus.Active);

        if (membership is null) return null;
        return new TenantAccess(userId, tenantId, tenant.Code, membership.RoleCode);
    }

    public IReadOnlyList<TenantMembership> GetActiveMemberships(Guid userId) =>
        _memberships
            .Where(x => x.UserId == userId && x.Status == MembershipStatus.Active)
            .Where(x => _tenants.TryGetValue(x.TenantId, out var tenant) && tenant.Status == TenantStatus.Active)
            .ToList()
            .AsReadOnly();

    private void ValidateMemberships()
    {
        var seen = new HashSet<(Guid UserId, Guid TenantId)>();
        foreach (var membership in _memberships)
        {
            if (!_users.ContainsKey(membership.UserId))
                throw new InvalidOperationException("Membership user does not exist.");
            if (!_tenants.ContainsKey(membership.TenantId))
                throw new InvalidOperationException("Membership tenant does not exist.");
            if (string.IsNullOrWhiteSpace(membership.RoleCode))
                throw new InvalidOperationException("Membership role is required.");
            if (!seen.Add((membership.UserId, membership.TenantId)))
                throw new InvalidOperationException("A user can have only one membership per tenant.");
        }
    }
}
