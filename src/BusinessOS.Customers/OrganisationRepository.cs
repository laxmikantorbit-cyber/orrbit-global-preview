namespace BusinessOS.Customers;

public interface IOrganisationRepository
{
    Task AddAsync(Organisation organisation, CancellationToken cancellationToken = default);
    Task UpdateAsync(Organisation organisation, CancellationToken cancellationToken = default);
    Task<Organisation?> GetAsync(Guid tenantId, Guid organisationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Organisation>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Organisation>> ListByRoleAsync(
        Guid tenantId,
        OrganisationRole role,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryOrganisationRepository : IOrganisationRepository
{
    private readonly Dictionary<Guid, Organisation> _items = [];
    private readonly object _gate = new();

    public InMemoryOrganisationRepository(IEnumerable<Organisation>? seed = null)
    {
        if (seed is null) return;
        foreach (var organisation in seed) AddInternal(organisation);
    }
    public Task AddAsync(Organisation organisation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organisation);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) AddInternal(organisation);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Organisation organisation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(organisation);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_items.TryGetValue(organisation.Id, out var existing) || existing.TenantId != organisation.TenantId)
                throw new InvalidOperationException("Organisation does not exist.");
            _items[organisation.Id] = organisation;
        }
        return Task.CompletedTask;
    }

    public Task<Organisation?> GetAsync(
        Guid tenantId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var found = _items.GetValueOrDefault(organisationId);
            return Task.FromResult(found?.TenantId == tenantId ? found : null);
        }
    }

    public Task<IReadOnlyList<Organisation>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<Organisation>>(
                _items.Values.Where(x => x.TenantId == tenantId).ToArray());
    }
    public Task<IReadOnlyList<Organisation>> ListByRoleAsync(
        Guid tenantId,
        OrganisationRole role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<Organisation>>(
                _items.Values
                    .Where(x => x.TenantId == tenantId && x.HasRole(role))
                    .ToArray());
    }

    private void AddInternal(Organisation organisation)
    {
        if (_items.ContainsKey(organisation.Id))
            throw new InvalidOperationException("Organisation id already exists.");
        _items.Add(organisation.Id, organisation);
    }
}
