namespace BusinessOS.Identity;

public interface IIdentityAccessRepository
{
    Task<UserIdentity?> FindUserBySubjectAsync(
        string subject,
        CancellationToken cancellationToken = default);

    Task<TenantAccess?> ResolveAccessAsync(
        string subject,
        string tenantCode,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryIdentityAccessRepository : IIdentityAccessRepository
{
    private readonly IdentityDirectory _directory;

    public InMemoryIdentityAccessRepository(IdentityDirectory directory) =>
        _directory = directory;

    public Task<UserIdentity?> FindUserBySubjectAsync(
        string subject,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_directory.FindBySubject(subject));

    public Task<TenantAccess?> ResolveAccessAsync(
        string subject,
        string tenantCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_directory.ResolveAccess(subject, tenantCode));
}
