namespace BusinessOS.Sales;

public enum OpportunityStage
{
    Discovery = 1,
    SolutionFit = 2,
    Proposal = 3,
    Negotiation = 4,
    Won = 5,
    Lost = 6
}

public sealed record OpportunityForecast(
    decimal EstimatedValue,
    string CurrencyCode,
    int ProbabilityPercent,
    DateOnly? ExpectedCloseDate);
