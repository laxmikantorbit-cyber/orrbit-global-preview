namespace BusinessOS.Crm;

public interface ILeadRepository
{
    Task AddAsync(Lead lead, CancellationToken cancellationToken = default);
    Task SaveAsync(Lead lead, CancellationToken cancellationToken = default);
    Task<Lead?> GetAsync(Guid tenantId, Guid leadId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Lead>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryLeadRepository : ILeadRepository
{
    private readonly Dictionary<Guid, Lead> _items = [];
    private readonly object _gate = new();

    public InMemoryLeadRepository(IEnumerable<Lead>? seed = null)
    {
        if (seed is null) return;
        foreach (var lead in seed) AddInternal(lead);
    }

    public Task AddAsync(Lead lead, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lead);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) AddInternal(lead);
        return Task.CompletedTask;
    }

    public Task SaveAsync(Lead lead, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lead);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_items.ContainsKey(lead.Id)) throw new InvalidOperationException("Lead does not exist.");
            _items[lead.Id] = lead;
        }
        return Task.CompletedTask;
    }
    public Task<Lead?> GetAsync(Guid tenantId, Guid leadId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var lead = _items.GetValueOrDefault(leadId);
            return Task.FromResult(lead?.TenantId == tenantId ? lead : null);
        }
    }

    public Task<IReadOnlyList<Lead>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<Lead>>(
                _items.Values.Where(x => x.TenantId == tenantId).ToArray());
    }

    private void AddInternal(Lead lead)
    {
        if (_items.ContainsKey(lead.Id))
            throw new InvalidOperationException("Lead id already exists.");
        _items.Add(lead.Id, lead);
    }
}
