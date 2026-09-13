using BusinessOS.Sales;

namespace BusinessOS.Sales.Tests;

public sealed class OpportunityTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Opportunity_Requires_Organisation()
    {
        Assert.Throws<ArgumentException>(() =>
            new Opportunity(Guid.NewGuid(), TenantA, Guid.Empty, "Deal", Forecast()));
    }

    [Fact]
    public void Forecast_Currency_Is_Normalized()
    {
        var opportunity = NewOpportunity(TenantA, new OpportunityForecast(1000, "inr", 50, null));
        Assert.Equal("INR", opportunity.Forecast.CurrencyCode);
    }

    [Fact]
    public void Forecast_Probability_Must_Be_Valid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NewOpportunity(TenantA, new OpportunityForecast(1000, "INR", 101, null)));
    }

    [Fact]
    public void Only_Open_Opportunity_Can_Change()
    {
        var opportunity = NewOpportunity(TenantA);
        opportunity.MarkLost("No budget");
        Assert.Throws<InvalidOperationException>(() => opportunity.MoveTo(OpportunityStage.Proposal));
    }

    [Fact]
    public void Lost_Opportunity_Requires_Reason()
    {
        var opportunity = NewOpportunity(TenantA);
        Assert.Throws<ArgumentException>(() => opportunity.MarkLost(" "));
        opportunity.MarkLost("Competitor");
        Assert.Equal("Competitor", opportunity.LossReason);
    }

    [Fact]
    public void Won_Opportunity_Must_Have_Positive_Value()
    {
        var opportunity = NewOpportunity(TenantA, new OpportunityForecast(0, "INR", 90, null));
        Assert.Throws<InvalidOperationException>(() => opportunity.MarkWon());
    }

    [Fact]
    public void Lead_And_Owner_Are_Separate_References()
    {
        var leadId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var opportunity = new Opportunity(
            Guid.NewGuid(), TenantA, Guid.NewGuid(), "Deal", Forecast(), leadId, ownerId);
        Assert.Equal(leadId, opportunity.OriginatingLeadId);
        Assert.Equal(ownerId, opportunity.OwnerUserId);
    }

    [Fact]
    public async Task Repository_Isolates_Tenants()
    {
        var a = NewOpportunity(TenantA);
        var b = NewOpportunity(TenantB);
        var repository = new InMemoryOpportunityRepository(new[] { a, b });

        Assert.Single(await repository.ListAsync(TenantA));
        Assert.Null(await repository.GetAsync(TenantA, b.Id));
    }

    [Fact]
    public async Task Duplicate_Opportunity_Id_Is_Rejected()
    {
        var id = Guid.NewGuid();
        var first = new Opportunity(id, TenantA, Guid.NewGuid(), "First", Forecast());
        var second = new Opportunity(id, TenantB, Guid.NewGuid(), "Second", Forecast());
        var repository = new InMemoryOpportunityRepository(new[] { first });

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddAsync(second));
    }

    private static Opportunity NewOpportunity(Guid tenantId, OpportunityForecast? forecast = null) =>
        new(Guid.NewGuid(), tenantId, Guid.NewGuid(), "Repair Software", forecast ?? Forecast());

    private static OpportunityForecast Forecast() =>
        new(30000m, "INR", 60, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
}
