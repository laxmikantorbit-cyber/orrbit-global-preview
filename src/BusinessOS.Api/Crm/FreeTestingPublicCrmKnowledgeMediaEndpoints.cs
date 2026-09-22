using BusinessOS.Api.Commerce;
using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmKnowledgeMediaEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmKnowledgeMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/knowledge/categories", async (
            IConfiguration configuration, IHostEnvironment environment,
            ICrmKnowledgeStore store, CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var items = await store.ListCategoriesAsync(DemoTenantId, cancellationToken);
            return Results.Ok(new { categories = items.Select(ToCategoryResponse).ToArray() });
        });

        group.MapPost("/knowledge/categories", async (
            SaveCrmKnowledgeCategoryRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmKnowledgeStore store, ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (member.Role is not (CrmRoleCode.Owner or CrmRoleCode.Admin))
                return Forbidden("Owner or Admin role is required to change knowledge categories.");
            try
            {
                var category = request.Id.HasValue
                    ? await store.GetCategoryAsync(DemoTenantId, request.Id.Value, cancellationToken)
                    : null;
                category ??= new CrmKnowledgeCategory(
                    request.Id ?? Guid.NewGuid(), DemoTenantId, request.Name, request.SortOrder, request.Active);
                if (request.Id.HasValue)
                    category.Update(request.Name, request.SortOrder, request.Active);
                await store.SaveCategoryAsync(category, cancellationToken);
                await AuditAsync(management, member.Id, "KnowledgeCategorySaved", "KnowledgeCategory",
                    category.Id, category.Name, cancellationToken);
                return Results.Ok(ToCategoryResponse(category));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapGet("/knowledge/articles", async (
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmKnowledgeStore store, CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var items = await store.ListArticlesAsync(DemoTenantId, cancellationToken);
            if (member.Role is not (CrmRoleCode.Owner or CrmRoleCode.Admin))
                items = items.Where(x => x.Visibility == CrmKnowledgeVisibility.Team || x.OwnerUserId == member.Id).ToArray();
            return Results.Ok(new { articles = items.Select(ToArticleResponse).ToArray() });
        });

        group.MapPost("/knowledge/articles", async (
            CreateCrmKnowledgeArticleRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmKnowledgeStore store, ICrmTeamRepository team, ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var category = await store.GetCategoryAsync(DemoTenantId, request.CategoryId, cancellationToken);
            if (category is null || !category.Active)
                return Results.BadRequest(new ErrorResponse("An active knowledge category is required."));
            var ownerId = CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member)
                ? request.OwnerUserId ?? member.Id : member.Id;
            var owner = await team.GetAsync(DemoTenantId, ownerId, cancellationToken);
            if (owner is null || !owner.Active)
                return Results.BadRequest(new ErrorResponse("Article owner must be active."));
            try
            {
                var article = new CrmKnowledgeArticle(
                    Guid.NewGuid(), DemoTenantId, request.Title, request.Content, request.CategoryId,
                    ownerId, ParseVisibility(request.Visibility));
                await store.AddArticleAsync(article, cancellationToken);
                await AuditAsync(management, member.Id, "KnowledgeArticleCreated", "KnowledgeArticle",
                    article.Id, article.Title, cancellationToken);
                return Results.Ok(ToArticleResponse(article));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/knowledge/articles/{id:guid}/profile", async (
            Guid id, UpdateCrmKnowledgeArticleRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmKnowledgeStore store, ICrmTeamRepository team, ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var article = await store.GetArticleAsync(DemoTenantId, id, cancellationToken);
            if (article is null) return Results.NotFound(new ErrorResponse("Knowledge article not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CanEditArticle(member, article)) return Forbidden("Article is outside your CRM scope.");
            var category = await store.GetCategoryAsync(DemoTenantId, request.CategoryId, cancellationToken);
            if (category is null || !category.Active)
                return Results.BadRequest(new ErrorResponse("An active knowledge category is required."));
            var ownerId = CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member)
                ? request.OwnerUserId ?? article.OwnerUserId : member.Id;
            var owner = await team.GetAsync(DemoTenantId, ownerId, cancellationToken);
            if (owner is null || !owner.Active)
                return Results.BadRequest(new ErrorResponse("Article owner must be active."));
            try
            {
                article.Update(request.Title, request.Content, request.CategoryId, ownerId, ParseVisibility(request.Visibility));
                await store.SaveArticleAsync(article, cancellationToken);
                await AuditAsync(management, member.Id, "KnowledgeArticleUpdated", "KnowledgeArticle",
                    article.Id, article.Title, cancellationToken);
                return Results.Ok(ToArticleResponse(article));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/knowledge/articles/{id:guid}/publish", async (
            Guid id, IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmKnowledgeStore store, ICrmManagementStore management, CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var article = await store.GetArticleAsync(DemoTenantId, id, cancellationToken);
            if (article is null) return Results.NotFound(new ErrorResponse("Knowledge article not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CanEditArticle(member, article)) return Forbidden("Article is outside your CRM scope.");
            try
            {
                article.Publish();
                await store.SaveArticleAsync(article, cancellationToken);
                await AuditAsync(management, member.Id, "KnowledgeArticlePublished", "KnowledgeArticle",
                    article.Id, article.Title, cancellationToken);
                return Results.Ok(ToArticleResponse(article));
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new ErrorResponse(ex.Message)); }
        });

        group.MapPost("/knowledge/articles/{id:guid}/archive", async (
            Guid id, IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmKnowledgeStore store, ICrmManagementStore management, CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var article = await store.GetArticleAsync(DemoTenantId, id, cancellationToken);
            if (article is null) return Results.NotFound(new ErrorResponse("Knowledge article not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CanEditArticle(member, article)) return Forbidden("Article is outside your CRM scope.");
            try
            {
                article.Archive();
                await store.SaveArticleAsync(article, cancellationToken);
                await AuditAsync(management, member.Id, "KnowledgeArticleArchived", "KnowledgeArticle",
                    article.Id, article.Title, cancellationToken);
                return Results.Ok(ToArticleResponse(article));
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new ErrorResponse(ex.Message)); }
        });

        group.MapGet("/media-assets", async (
            string? entityType, Guid? entityId,
            IConfiguration configuration, IHostEnvironment environment,
            ICrmMediaStore store, CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var items = await store.ListAsync(DemoTenantId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(entityType))
                items = items.Where(x => string.Equals(x.EntityType, entityType.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entityId.HasValue)
                items = items.Where(x => x.EntityId == entityId.Value).ToArray();
            return Results.Ok(new { assets = items.Select(ToMediaResponse).ToArray() });
        });

        group.MapPost("/media-assets", async (
            RegisterCrmMediaAssetRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmMediaStore store, ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            try
            {
                var asset = new CrmMediaAsset(
                    Guid.NewGuid(), DemoTenantId, request.FileName, request.MimeType, request.SizeBytes,
                    request.Purpose, request.StorageReference, member.Id, request.EntityType, request.EntityId);
                await store.AddAsync(asset, cancellationToken);
                await AuditAsync(management, member.Id, "MediaAssetRegistered", "MediaAsset",
                    asset.Id, asset.FileName, cancellationToken);
                return Results.Ok(ToMediaResponse(asset));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/media-assets/{id:guid}/active", async (
            Guid id, ChangeCrmMediaAssetActiveRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmMediaStore store, ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var asset = await store.GetAsync(DemoTenantId, id, cancellationToken);
            if (asset is null) return Results.NotFound(new ErrorResponse("Media asset not found."));
            asset.SetActive(request.Active);
            await store.SaveAsync(asset, cancellationToken);
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            await AuditAsync(management, member.Id, request.Active ? "MediaAssetActivated" : "MediaAssetDeactivated",
                "MediaAsset", asset.Id, asset.FileName, cancellationToken);
            return Results.Ok(ToMediaResponse(asset));
        });

        return app;
    }

    private static bool CanEditArticle(CrmTeamMember member, CrmKnowledgeArticle article) =>
        member.Role is CrmRoleCode.Owner or CrmRoleCode.Admin || article.OwnerUserId == member.Id;

    private static CrmKnowledgeVisibility ParseVisibility(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return CrmKnowledgeVisibility.Team;
        if (!Enum.TryParse<CrmKnowledgeVisibility>(value, true, out var parsed))
            throw new ArgumentException("Visibility must be Team or Private.");
        return parsed;
    }

    private static CrmKnowledgeCategoryResponse ToCategoryResponse(CrmKnowledgeCategory item) =>
        new(item.Id, item.Name, item.SortOrder, item.Active);

    private static CrmKnowledgeArticleResponse ToArticleResponse(CrmKnowledgeArticle item) =>
        new(item.Id, item.Title, item.Content, item.CategoryId, item.OwnerUserId,
            item.Visibility.ToString(), item.Status.ToString(), item.CreatedAtUtc, item.UpdatedAtUtc);

    private static CrmMediaAssetResponse ToMediaResponse(CrmMediaAsset item) =>
        new(item.Id, item.FileName, item.MimeType, item.SizeBytes, item.Purpose, item.StorageReference,
            item.UploadedByUserId, item.EntityType, item.EntityId, item.Active, item.CreatedAtUtc, item.UpdatedAtUtc);

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);
    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
    private static IResult Forbidden(string message) =>
        Results.Json(new ErrorResponse(message), statusCode: StatusCodes.Status403Forbidden);

    private static Task AuditAsync(
        ICrmManagementStore management, Guid actorUserId, string action, string entityType,
        Guid entityId, string? detail, CancellationToken cancellationToken) =>
        management.AddAuditAsync(new CrmAuditEntry(
            Guid.NewGuid(), DemoTenantId, actorUserId, action, entityType,
            entityId.ToString(), detail, DateTimeOffset.UtcNow), cancellationToken);
}

public sealed record SaveCrmKnowledgeCategoryRequest(Guid? Id, string Name, int SortOrder, bool Active);
public sealed record CreateCrmKnowledgeArticleRequest(
    string Title, string Content, Guid CategoryId, Guid? OwnerUserId, string? Visibility);
public sealed record UpdateCrmKnowledgeArticleRequest(
    string Title, string Content, Guid CategoryId, Guid? OwnerUserId, string? Visibility);
public sealed record CrmKnowledgeCategoryResponse(Guid Id, string Name, int SortOrder, bool Active);
public sealed record CrmKnowledgeArticleResponse(
    Guid Id, string Title, string Content, Guid CategoryId, Guid OwnerUserId,
    string Visibility, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record RegisterCrmMediaAssetRequest(
    string FileName, string MimeType, long SizeBytes, string Purpose, string StorageReference,
    string? EntityType, Guid? EntityId);
public sealed record ChangeCrmMediaAssetActiveRequest(bool Active);
public sealed record CrmMediaAssetResponse(
    Guid Id, string FileName, string MimeType, long SizeBytes, string Purpose, string StorageReference,
    Guid UploadedByUserId, string? EntityType, Guid? EntityId, bool Active,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
