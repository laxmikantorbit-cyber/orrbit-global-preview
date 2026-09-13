namespace BusinessOS.Sales;

public interface IOpportunityRepository
{
    Task AddAsync(Opportunity opportunity, CancellationToken cancellationToken = default);
    Task<Opportunity?> GetAsync(Guid tenantId, Guid opportunityId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Opportunity>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryOpportunityRepository : IOpportunityRepository
{
    private readonly Dictionary<Guid, Opportunity> _items = [];
    private readonly object _gate = new();

    public InMemoryOpportunityRepository(IEnumerable<Opportunity>? seed = null)
    {
        if (seed is null) return;
        foreach (var opportunity in seed) AddInternal(opportunity);
    }

    public Task AddAsync(Opportunity opportunity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(opportunity);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) AddInternal(opportunity);
        return Task.CompletedTask;
    }

    public Task<Opportunity?> GetAsync(
        Guid tenantId,
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var opportunity = _items.GetValueOrDefault(opportunityId);
            return Task.FromResult(opportunity?.TenantId == tenantId ? opportunity : null);
        }
    }

    public Task<IReadOnlyList<Opportunity>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<Opportunity>>(
                _items.Values.Where(x => x.TenantId == tenantId).ToArray());
    }

    private void AddInternal(Opportunity opportunity)
    {
        if (_items.ContainsKey(opportunity.Id))
            throw new InvalidOperationException("Opportunity id already exists.");
        _items.Add(opportunity.Id, opportunity);
    }
}
