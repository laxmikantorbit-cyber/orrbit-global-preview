using BusinessOS.Crm;

namespace BusinessOS.Crm.Tests;

public sealed class CrmPhase4BTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Estimate_Request_Review_And_Conversion_Are_One_Way()
    {
        var request = new CrmEstimateRequest(
            Guid.NewGuid(), TenantId, "Website", "Repair software pricing",
            "Ajay", "9999999999", "AJAY@EXAMPLE.COM", 15000m, UserId);

        request.StartReview();
        Assert.Equal(CrmEstimateRequestStatus.Reviewing, request.Status);

        var leadId = Guid.NewGuid();
        request.MarkConverted(leadId);
        Assert.Equal(CrmEstimateRequestStatus.Converted, request.Status);
        Assert.Equal(leadId, request.ConvertedLeadId);

        Assert.Throws<InvalidOperationException>(() => request.MarkConverted(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() =>
            request.Update("WhatsApp", "Changed", null, null, null, null, null));
    }

    [Fact]
    public void Closed_Estimate_Request_Cannot_Be_Converted()
    {
        var request = new CrmEstimateRequest(Guid.NewGuid(), TenantId, "WhatsApp", "Need quote");
        request.Close();

        Assert.Equal(CrmEstimateRequestStatus.Closed, request.Status);
        Assert.Throws<InvalidOperationException>(() => request.MarkConverted(Guid.NewGuid()));
    }

    [Fact]
    public void Estimate_Request_Rejects_Negative_Value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CrmEstimateRequest(Guid.NewGuid(), TenantId, "Website", "Need quote", expectedValue: -1m));
    }

    [Fact]
    public void Knowledge_Article_Lifecycle_Is_Draft_Published_Archived()
    {
        var category = new CrmKnowledgeCategory(Guid.NewGuid(), TenantId, "Sales Training");
        var article = new CrmKnowledgeArticle(
            Guid.NewGuid(), TenantId, "Follow-up guide", "Call within the planned window.",
            category.Id, UserId);

        article.Publish();
        Assert.Equal(CrmKnowledgeArticleStatus.Published, article.Status);

        article.Archive();
        Assert.Equal(CrmKnowledgeArticleStatus.Archived, article.Status);
        Assert.Throws<InvalidOperationException>(() =>
            article.Update("Changed", "Changed", category.Id, UserId, CrmKnowledgeVisibility.Team));
    }

    [Fact]
    public void Knowledge_Category_Requires_Name()
    {
        Assert.Throws<ArgumentException>(() =>
            new CrmKnowledgeCategory(Guid.NewGuid(), TenantId, " "));
    }

    [Fact]
    public void Media_Asset_Requires_Usable_Metadata()
    {
        var asset = new CrmMediaAsset(
            Guid.NewGuid(), TenantId, "demo.pdf", "application/pdf", 2048,
            "Contract attachment", "crm/contracts/demo.pdf", UserId, "Contract", Guid.NewGuid());

        Assert.True(asset.Active);
        asset.SetActive(false);
        Assert.False(asset.Active);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CrmMediaAsset(Guid.NewGuid(), TenantId, "bad.txt", "text/plain", -1,
                "Test", "bad", UserId));
        Assert.Throws<ArgumentException>(() =>
            new CrmMediaAsset(Guid.NewGuid(), TenantId, "bad.txt", "text/plain", 1,
                "Test", "bad", UserId, null, Guid.NewGuid()));
    }
}
