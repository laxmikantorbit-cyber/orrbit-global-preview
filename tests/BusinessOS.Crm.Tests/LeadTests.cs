using BusinessOS.Crm;

namespace BusinessOS.Crm.Tests;

public sealed class LeadTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Lead_Requires_Organisation()
    {
        Assert.Throws<ArgumentException>(() =>
            new Lead(Guid.NewGuid(), TenantA, Guid.Empty, "Repair Software", Attribution()));
    }

    [Fact]
    public void Lead_Starts_New_And_Can_Be_Qualified()
    {
        var lead = NewLead(TenantA);
        lead.MarkContacted();
        lead.Qualify();
        Assert.Equal(LeadStatus.Qualified, lead.Status);
    }

    [Fact]
    public void Only_Qualified_Lead_Can_Convert()
    {
        var lead = NewLead(TenantA);
        Assert.Throws<InvalidOperationException>(() => lead.Convert());
        lead.Qualify();
        lead.Convert();
        Assert.Equal(LeadStatus.Converted, lead.Status);
    }

    [Fact]
    public void Unqualified_Lead_Requires_Reason_And_Closes()
    {
        var lead = NewLead(TenantA);
        Assert.Throws<ArgumentException>(() => lead.MarkUnqualified(" "));
        lead.MarkUnqualified("No budget");
        Assert.Equal("No budget", lead.UnqualifiedReason);
        Assert.Throws<InvalidOperationException>(() => lead.Qualify());
    }

    [Fact]
    public void Attribution_Dimensions_Remain_Separate()
    {
        var original = Guid.NewGuid();
        var selling = Guid.NewGuid();
        var lead = new Lead(Guid.NewGuid(), TenantA, Guid.NewGuid(), "Repair Software",
            new LeadAttribution("Website", original, selling, null, null, null));

        Assert.Equal("Website", lead.Attribution.LeadSource);
        Assert.Equal(original, lead.Attribution.OriginalPartnerId);
        Assert.Equal(selling, lead.Attribution.SellingPartnerId);
    }

    [Fact]
    public async Task Repository_Isolates_Tenants()
    {
        var a = NewLead(TenantA);
        var b = NewLead(TenantB);
        var repository = new InMemoryLeadRepository(new[] { a, b });

        Assert.Single(await repository.ListAsync(TenantA));
        Assert.Null(await repository.GetAsync(TenantA, b.Id));
    }

    [Fact]
    public async Task Duplicate_Lead_Id_Is_Rejected()
    {
        var id = Guid.NewGuid();
        var first = new Lead(id, TenantA, Guid.NewGuid(), "First", Attribution());
        var second = new Lead(id, TenantB, Guid.NewGuid(), "Second", Attribution());
        var repository = new InMemoryLeadRepository(new[] { first });

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddAsync(second));
    }

    [Fact]
    public void Lead_Value_Can_Be_Set_Cleared_And_Cannot_Be_Negative()
    {
        var lead = NewLead(TenantA);

        lead.SetEstimatedValue(29999m);
        Assert.Equal(29999m, lead.EstimatedValue);

        lead.SetEstimatedValue(null);
        Assert.Null(lead.EstimatedValue);

        Assert.Throws<ArgumentOutOfRangeException>(() => lead.SetEstimatedValue(-1m));
    }

    [Fact]
    public void Lead_Restore_Preserves_Estimated_Value()
    {
        var lead = Lead.Restore(
            Guid.NewGuid(), TenantA, Guid.NewGuid(), "Repair Software", Attribution(),
            null, null, null, null, null, LeadPriority.Normal, LeadStatus.New, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, [], 17700m);

        Assert.Equal(17700m, lead.EstimatedValue);
    }

    private static Lead NewLead(Guid tenantId) =>
        new(Guid.NewGuid(), tenantId, Guid.NewGuid(), "Repair Software", Attribution());

    private static LeadAttribution Attribution() =>
        new("Direct", null, null, null, null, null);
}
