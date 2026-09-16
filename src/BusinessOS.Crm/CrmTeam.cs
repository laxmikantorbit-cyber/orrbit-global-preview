namespace BusinessOS.Crm;

public enum CrmRoleCode
{
    Owner = 1,
    Admin = 2,
    SalesManager = 3,
    SalesExecutive = 4,
    Telecaller = 5,
    Support = 6,
    Viewer = 7
}

public enum CrmPermission
{
    ViewDashboard = 1,
    ViewLeads = 2,
    CreateLead = 3,
    EditLead = 4,
    AssignLead = 5,
    ManageFollowUps = 6,
    ManageTasks = 7,
    ManageAccounts = 8,
    ManageOpportunities = 9,
    ViewReports = 10,
    ManageTeam = 11,
    ExportData = 12
}

public sealed class CrmTeamMember
{
    public CrmTeamMember(Guid id, Guid tenantId, string displayName, string email,
        string? mobileNumber, CrmRoleCode role, bool active = true, DateTimeOffset? createdAtUtc = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Team member id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Display name is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required.", nameof(email));
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        Id=id; TenantId=tenantId; DisplayName=displayName.Trim(); Email=email.Trim().ToLowerInvariant();
        MobileNumber=Clean(mobileNumber); Role=role; Active=active; CreatedAtUtc=createdAtUtc??DateTimeOffset.UtcNow;
    }
    public Guid Id { get; }
    public Guid TenantId { get; }
    public string DisplayName { get; private set; }
    public string Email { get; private set; }
    public string? MobileNumber { get; private set; }
    public CrmRoleCode Role { get; private set; }
    public bool Active { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public void ChangeRole(CrmRoleCode role){if(!Enum.IsDefined(role))throw new ArgumentOutOfRangeException(nameof(role));Role=role;}
    public void SetActive(bool active)=>Active=active;
    public void UpdateProfile(string name,string email,string? mobile){if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(email))throw new ArgumentException("Name and email are required.");DisplayName=name.Trim();Email=email.Trim().ToLowerInvariant();MobileNumber=Clean(mobile);}
    private static string? Clean(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
}

public static class CrmRolePolicy
{
    private static readonly IReadOnlyDictionary<CrmRoleCode, CrmPermission[]> Matrix =
        new Dictionary<CrmRoleCode, CrmPermission[]>
        {
            [CrmRoleCode.Owner] = Enum.GetValues<CrmPermission>(),
            [CrmRoleCode.Admin] = Enum.GetValues<CrmPermission>(),
            [CrmRoleCode.SalesManager] = [CrmPermission.ViewDashboard,CrmPermission.ViewLeads,CrmPermission.CreateLead,CrmPermission.EditLead,CrmPermission.AssignLead,CrmPermission.ManageFollowUps,CrmPermission.ManageTasks,CrmPermission.ManageAccounts,CrmPermission.ManageOpportunities,CrmPermission.ViewReports,CrmPermission.ExportData],
            [CrmRoleCode.SalesExecutive] = [CrmPermission.ViewDashboard,CrmPermission.ViewLeads,CrmPermission.CreateLead,CrmPermission.EditLead,CrmPermission.ManageFollowUps,CrmPermission.ManageTasks,CrmPermission.ManageAccounts,CrmPermission.ManageOpportunities,CrmPermission.ViewReports],
            [CrmRoleCode.Telecaller] = [CrmPermission.ViewDashboard,CrmPermission.ViewLeads,CrmPermission.CreateLead,CrmPermission.EditLead,CrmPermission.ManageFollowUps,CrmPermission.ManageTasks],
            [CrmRoleCode.Support] = [CrmPermission.ViewDashboard,CrmPermission.ViewLeads,CrmPermission.EditLead,CrmPermission.ManageFollowUps,CrmPermission.ManageTasks,CrmPermission.ManageAccounts],
            [CrmRoleCode.Viewer] = [CrmPermission.ViewDashboard,CrmPermission.ViewLeads,CrmPermission.ViewReports]
        };
    public static IReadOnlyList<CrmPermission> Permissions(CrmRoleCode role)=>Matrix.GetValueOrDefault(role,[]);
    public static bool Allows(CrmRoleCode role,CrmPermission permission)=>Permissions(role).Contains(permission);
}

public interface ICrmTeamRepository
{
    Task AddAsync(CrmTeamMember member,CancellationToken cancellationToken=default);
    Task SaveAsync(CrmTeamMember member,CancellationToken cancellationToken=default);
    Task<CrmTeamMember?> GetAsync(Guid tenantId,Guid memberId,CancellationToken cancellationToken=default);
    Task<IReadOnlyList<CrmTeamMember>> ListAsync(Guid tenantId,CancellationToken cancellationToken=default);
}

public sealed class InMemoryCrmTeamRepository : ICrmTeamRepository
{
    private readonly Dictionary<Guid,CrmTeamMember> _items=[]; private readonly object _gate=new();
    public InMemoryCrmTeamRepository(IEnumerable<CrmTeamMember>? seed=null){if(seed is null)return;foreach(var x in seed)_items.Add(x.Id,x);}
    public Task AddAsync(CrmTeamMember x,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate){if(_items.ContainsKey(x.Id))throw new InvalidOperationException("Team member already exists.");if(_items.Values.Any(y=>y.TenantId==x.TenantId&&y.Email.Equals(x.Email,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Team member email already exists.");_items.Add(x.Id,x);}return Task.CompletedTask;}
    public Task SaveAsync(CrmTeamMember x,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate){if(!_items.ContainsKey(x.Id))throw new InvalidOperationException("Team member does not exist.");_items[x.Id]=x;}return Task.CompletedTask;}
    public Task<CrmTeamMember?> GetAsync(Guid tenantId,Guid id,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate){var x=_items.GetValueOrDefault(id);return Task.FromResult(x?.TenantId==tenantId?x:null);}}
    public Task<IReadOnlyList<CrmTeamMember>> ListAsync(Guid tenantId,CancellationToken ct=default){ct.ThrowIfCancellationRequested();lock(_gate)return Task.FromResult<IReadOnlyList<CrmTeamMember>>(_items.Values.Where(x=>x.TenantId==tenantId).OrderBy(x=>x.DisplayName).ToArray());}
}
