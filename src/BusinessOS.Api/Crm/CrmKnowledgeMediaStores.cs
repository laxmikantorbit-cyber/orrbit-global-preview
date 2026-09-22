using BusinessOS.Crm;
using Npgsql;
using NpgsqlTypes;

namespace BusinessOS.Api.Crm;

public interface ICrmKnowledgeStore
{
    Task SaveCategoryAsync(CrmKnowledgeCategory category, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmKnowledgeCategory>> ListCategoriesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<CrmKnowledgeCategory?> GetCategoryAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task AddArticleAsync(CrmKnowledgeArticle article, CancellationToken cancellationToken = default);
    Task SaveArticleAsync(CrmKnowledgeArticle article, CancellationToken cancellationToken = default);
    Task<CrmKnowledgeArticle?> GetArticleAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmKnowledgeArticle>> ListArticlesAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public interface ICrmMediaStore
{
    Task AddAsync(CrmMediaAsset asset, CancellationToken cancellationToken = default);
    Task SaveAsync(CrmMediaAsset asset, CancellationToken cancellationToken = default);
    Task<CrmMediaAsset?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmMediaAsset>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryCrmKnowledgeStore : ICrmKnowledgeStore
{
    private readonly Dictionary<Guid, CrmKnowledgeCategory> _categories = [];
    private readonly Dictionary<Guid, CrmKnowledgeArticle> _articles = [];
    private readonly object _gate = new();

    public Task SaveCategoryAsync(CrmKnowledgeCategory category, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) _categories[category.Id] = category;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CrmKnowledgeCategory>> ListCategoriesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult<IReadOnlyList<CrmKnowledgeCategory>>(
            _categories.Values.Where(x => x.TenantId == tenantId).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToArray());
    }

    public Task<CrmKnowledgeCategory?> GetCategoryAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _categories.GetValueOrDefault(id);
            return Task.FromResult(item?.TenantId == tenantId ? item : null);
        }
    }

    public Task AddArticleAsync(CrmKnowledgeArticle article, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_articles.ContainsKey(article.Id)) throw new InvalidOperationException("Knowledge article already exists.");
            _articles.Add(article.Id, article);
        }
        return Task.CompletedTask;
    }

    public Task SaveArticleAsync(CrmKnowledgeArticle article, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_articles.ContainsKey(article.Id)) throw new InvalidOperationException("Knowledge article does not exist.");
            _articles[article.Id] = article;
        }
        return Task.CompletedTask;
    }

    public Task<CrmKnowledgeArticle?> GetArticleAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _articles.GetValueOrDefault(id);
            return Task.FromResult(item?.TenantId == tenantId ? item : null);
        }
    }

    public Task<IReadOnlyList<CrmKnowledgeArticle>> ListArticlesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult<IReadOnlyList<CrmKnowledgeArticle>>(
            _articles.Values.Where(x => x.TenantId == tenantId).OrderByDescending(x => x.UpdatedAtUtc).ToArray());
    }
}

public sealed class InMemoryCrmMediaStore : ICrmMediaStore
{
    private readonly Dictionary<Guid, CrmMediaAsset> _items = [];
    private readonly object _gate = new();

    public Task AddAsync(CrmMediaAsset asset, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_items.ContainsKey(asset.Id)) throw new InvalidOperationException("Media asset already exists.");
            _items.Add(asset.Id, asset);
        }
        return Task.CompletedTask;
    }
    public Task SaveAsync(CrmMediaAsset asset, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_items.ContainsKey(asset.Id)) throw new InvalidOperationException("Media asset does not exist.");
            _items[asset.Id] = asset;
        }
        return Task.CompletedTask;
    }
    public Task<CrmMediaAsset?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var item = _items.GetValueOrDefault(id);
            return Task.FromResult(item?.TenantId == tenantId ? item : null);
        }
    }
    public Task<IReadOnlyList<CrmMediaAsset>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult<IReadOnlyList<CrmMediaAsset>>(
            _items.Values.Where(x => x.TenantId == tenantId).OrderByDescending(x => x.UpdatedAtUtc).ToArray());
    }
}

public sealed class PostgresCrmKnowledgeStore : ICrmKnowledgeStore
{
    private readonly CrmPostgresDatabase _db;
    public PostgresCrmKnowledgeStore(CrmPostgresDatabase db) => _db = db;

    public async Task SaveCategoryAsync(CrmKnowledgeCategory category, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
INSERT INTO businessos_crm.knowledge_categories(id,tenant_id,name,sort_order,active)
VALUES(@id,@tenant,@name,@sort,@active)
ON CONFLICT(id) DO UPDATE SET name=EXCLUDED.name,sort_order=EXCLUDED.sort_order,active=EXCLUDED.active;
""");
        command.Parameters.AddWithValue("id", category.Id);
        command.Parameters.AddWithValue("tenant", category.TenantId);
        command.Parameters.AddWithValue("name", category.Name);
        command.Parameters.AddWithValue("sort", category.SortOrder);
        command.Parameters.AddWithValue("active", category.Active);
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new InvalidOperationException("Knowledge category name already exists.", ex); }
    }

    public async Task<IReadOnlyList<CrmKnowledgeCategory>> ListCategoriesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
SELECT id,tenant_id,name,sort_order,active
FROM businessos_crm.knowledge_categories WHERE tenant_id=@tenant ORDER BY sort_order,name;
""");
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CrmKnowledgeCategory>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new CrmKnowledgeCategory(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3), reader.GetBoolean(4)));
        return result;
    }

    public async Task<CrmKnowledgeCategory?> GetCategoryAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
SELECT id,tenant_id,name,sort_order,active
FROM businessos_crm.knowledge_categories WHERE tenant_id=@tenant AND id=@id LIMIT 1;
""");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CrmKnowledgeCategory(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3), reader.GetBoolean(4))
            : null;
    }

    public async Task AddArticleAsync(CrmKnowledgeArticle article, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
INSERT INTO businessos_crm.knowledge_articles(
 id,tenant_id,title,content,category_id,owner_user_id,visibility,status,created_at_utc,updated_at_utc)
VALUES(@id,@tenant,@title,@content,@category,@owner,@visibility,@status,@created,@updated);
""");
        AddArticleParameters(command, article);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveArticleAsync(CrmKnowledgeArticle article, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
UPDATE businessos_crm.knowledge_articles SET
 title=@title,content=@content,category_id=@category,owner_user_id=@owner,visibility=@visibility,
 status=@status,updated_at_utc=@updated
WHERE tenant_id=@tenant AND id=@id;
""");
        AddArticleParameters(command, article);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Knowledge article does not exist.");
    }

    public async Task<CrmKnowledgeArticle?> GetArticleAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(ArticleSelect + " WHERE tenant_id=@tenant AND id=@id LIMIT 1");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadArticle(reader) : null;
    }

    public async Task<IReadOnlyList<CrmKnowledgeArticle>> ListArticlesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(ArticleSelect + " WHERE tenant_id=@tenant ORDER BY updated_at_utc DESC,title");
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CrmKnowledgeArticle>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadArticle(reader));
        return result;
    }

    private const string ArticleSelect = """
SELECT id,tenant_id,title,content,category_id,owner_user_id,visibility,status,created_at_utc,updated_at_utc
FROM businessos_crm.knowledge_articles
""";

    private static void AddArticleParameters(NpgsqlCommand command, CrmKnowledgeArticle article)
    {
        command.Parameters.AddWithValue("id", article.Id);
        command.Parameters.AddWithValue("tenant", article.TenantId);
        command.Parameters.AddWithValue("title", article.Title);
        command.Parameters.AddWithValue("content", article.Content);
        command.Parameters.AddWithValue("category", article.CategoryId);
        command.Parameters.AddWithValue("owner", article.OwnerUserId);
        command.Parameters.AddWithValue("visibility", (int)article.Visibility);
        command.Parameters.AddWithValue("status", (int)article.Status);
        command.Parameters.AddWithValue("created", article.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", article.UpdatedAtUtc);
    }

    private static CrmKnowledgeArticle ReadArticle(NpgsqlDataReader reader) =>
        CrmKnowledgeArticle.Restore(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.GetGuid(4), reader.GetGuid(5), (CrmKnowledgeVisibility)reader.GetInt32(6),
            (CrmKnowledgeArticleStatus)reader.GetInt32(7),
            reader.GetFieldValue<DateTimeOffset>(8), reader.GetFieldValue<DateTimeOffset>(9));
}

public sealed class PostgresCrmMediaStore : ICrmMediaStore
{
    private readonly CrmPostgresDatabase _db;
    public PostgresCrmMediaStore(CrmPostgresDatabase db) => _db = db;

    public async Task AddAsync(CrmMediaAsset asset, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
INSERT INTO businessos_crm.media_assets(
 id,tenant_id,file_name,mime_type,size_bytes,purpose,storage_reference,uploaded_by_user_id,
 entity_type,entity_id,active,created_at_utc,updated_at_utc)
VALUES(@id,@tenant,@file,@mime,@size,@purpose,@storage,@uploader,@entityType,@entityId,@active,@created,@updated);
""");
        AddParameters(command, asset);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(CrmMediaAsset asset, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand("""
UPDATE businessos_crm.media_assets SET active=@active,updated_at_utc=@updated
WHERE tenant_id=@tenant AND id=@id;
""");
        AddParameters(command, asset);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Media asset does not exist.");
    }

    public async Task<CrmMediaAsset?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(SelectSql + " WHERE tenant_id=@tenant AND id=@id LIMIT 1");
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<CrmMediaAsset>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _db.EnsureReadyAsync(cancellationToken);
        await using var command = _db.DataSource.CreateCommand(
            SelectSql + " WHERE tenant_id=@tenant ORDER BY updated_at_utc DESC,file_name");
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CrmMediaAsset>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    private const string SelectSql = """
SELECT id,tenant_id,file_name,mime_type,size_bytes,purpose,storage_reference,uploaded_by_user_id,
       entity_type,entity_id,active,created_at_utc,updated_at_utc
FROM businessos_crm.media_assets
""";

    private static void AddParameters(NpgsqlCommand command, CrmMediaAsset asset)
    {
        command.Parameters.AddWithValue("id", asset.Id);
        command.Parameters.AddWithValue("tenant", asset.TenantId);
        command.Parameters.AddWithValue("file", asset.FileName);
        command.Parameters.AddWithValue("mime", asset.MimeType);
        command.Parameters.AddWithValue("size", asset.SizeBytes);
        command.Parameters.AddWithValue("purpose", asset.Purpose);
        command.Parameters.AddWithValue("storage", asset.StorageReference);
        command.Parameters.AddWithValue("uploader", asset.UploadedByUserId);
        command.Parameters.Add("entityType", NpgsqlDbType.Text).Value = asset.EntityType ?? (object)DBNull.Value;
        command.Parameters.Add("entityId", NpgsqlDbType.Uuid).Value = asset.EntityId ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("active", asset.Active);
        command.Parameters.AddWithValue("created", asset.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", asset.UpdatedAtUtc);
    }

    private static CrmMediaAsset Read(NpgsqlDataReader reader) =>
        CrmMediaAsset.Restore(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
            reader.GetString(5), reader.GetString(6), reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.GetBoolean(10), reader.GetFieldValue<DateTimeOffset>(11), reader.GetFieldValue<DateTimeOffset>(12));
}
