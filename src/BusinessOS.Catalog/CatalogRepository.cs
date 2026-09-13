namespace BusinessOS.Catalog;

public interface ICatalogRepository
{
    Task AddProductAsync(Product product, CancellationToken cancellationToken = default);
    Task AddPlanAsync(CommercialPlan plan, CancellationToken cancellationToken = default);
    Task<Product?> GetProductAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default);
    Task<CommercialPlan?> GetPlanAsync(Guid tenantId, Guid planId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> ListProductsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommercialPlan>> ListPlansAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryCatalogRepository : ICatalogRepository
{
    private readonly Dictionary<Guid, Product> _products = [];
    private readonly Dictionary<Guid, CommercialPlan> _plans = [];
    private readonly object _gate = new();

    public InMemoryCatalogRepository(
        IEnumerable<Product>? products = null,
        IEnumerable<CommercialPlan>? plans = null)
    {
        if (products is not null)
            foreach (var product in products) AddProductInternal(product);
        if (plans is not null)
            foreach (var plan in plans) AddPlanInternal(plan);
    }
    public Task AddProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) AddProductInternal(product);
        return Task.CompletedTask;
    }

    public Task AddPlanAsync(CommercialPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) AddPlanInternal(plan);
        return Task.CompletedTask;
    }

    public Task<Product?> GetProductAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var product = _products.GetValueOrDefault(productId);
            return Task.FromResult(product?.TenantId == tenantId ? product : null);
        }
    }

    public Task<CommercialPlan?> GetPlanAsync(Guid tenantId, Guid planId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var plan = _plans.GetValueOrDefault(planId);
            return Task.FromResult(plan?.TenantId == tenantId ? plan : null);
        }
    }

    public Task<IReadOnlyList<Product>> ListProductsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<Product>>(
                _products.Values.Where(x => x.TenantId == tenantId).ToArray());
    }

    public Task<IReadOnlyList<CommercialPlan>> ListPlansAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<CommercialPlan>>(
                _plans.Values.Where(x => x.TenantId == tenantId && x.ProductId == productId).ToArray());
    }

    private void AddProductInternal(Product product)
    {
        if (_products.ContainsKey(product.Id))
            throw new InvalidOperationException("Product id already exists.");
        if (_products.Values.Any(x => x.TenantId == product.TenantId && x.Code == product.Code))
            throw new InvalidOperationException("Product code already exists for tenant.");
        _products.Add(product.Id, product);
    }

    private void AddPlanInternal(CommercialPlan plan)
    {
        if (_plans.ContainsKey(plan.Id))
            throw new InvalidOperationException("Plan id already exists.");
        if (!_products.TryGetValue(plan.ProductId, out var product) || product.TenantId != plan.TenantId)
            throw new InvalidOperationException("Plan product must exist in the same tenant.");
        if (_plans.Values.Any(x => x.TenantId == plan.TenantId && x.ProductId == plan.ProductId && x.Code == plan.Code))
            throw new InvalidOperationException("Plan code already exists for product.");
        _plans.Add(plan.Id, plan);
    }
}
