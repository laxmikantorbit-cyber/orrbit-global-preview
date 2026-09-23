using BusinessOS.Api.Crm;
using BusinessOS.Crm;

namespace BusinessOS.Api.Tests;

public sealed class CrmKnowledgeStoreTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OwnerId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task InMemory_Knowledge_Store_Persists_And_Scopes_Categories()
    {
        var store = new InMemoryCrmKnowledgeStore();
        var category = new CrmKnowledgeCategory(Guid.NewGuid(), TenantA, "Support", 20);

        await store.SaveCategoryAsync(category);
        category.Update("Support Guides", 10, true);
        await store.SaveCategoryAsync(category);

        var saved = Assert.Single(await store.ListCategoriesAsync(TenantA));
        Assert.Equal("Support Guides", saved.Name);
        Assert.Equal(10, saved.SortOrder);
        Assert.Empty(await store.ListCategoriesAsync(TenantB));
    }

    [Fact]
    public async Task InMemory_Knowledge_Store_Persists_Article_Lifecycle_And_Rejects_Duplicate()
    {
        var store = new InMemoryCrmKnowledgeStore();
        var category = new CrmKnowledgeCategory(Guid.NewGuid(), TenantA, "Sales");
        await store.SaveCategoryAsync(category);
        var article = new CrmKnowledgeArticle(
            Guid.NewGuid(), TenantA, "Follow-up guide", "Call within the planned window.",
            category.Id, OwnerId, CrmKnowledgeVisibility.Private);

        await store.AddArticleAsync(article);
        article.Publish();
        await store.SaveArticleAsync(article);

        var saved = Assert.Single(await store.ListArticlesAsync(TenantA));
        Assert.Equal(CrmKnowledgeArticleStatus.Published, saved.Status);
        Assert.Equal(CrmKnowledgeVisibility.Private, saved.Visibility);
        Assert.Equal(OwnerId, saved.OwnerUserId);
        Assert.Empty(await store.ListArticlesAsync(TenantB));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddArticleAsync(article));
    }
}
