namespace BusinessOS.Sales;

public sealed class Opportunity
{
    public Opportunity(
        Guid id,
        Guid tenantId,
        Guid organisationId,
        string title,
        OpportunityForecast forecast,
        Guid? originatingLeadId = null,
        Guid? ownerUserId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Opportunity id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (organisationId == Guid.Empty) throw new ArgumentException("Organisation id is required.", nameof(organisationId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Opportunity title is required.", nameof(title));
        ValidateForecast(forecast);

        Id = id;
        TenantId = tenantId;
        OrganisationId = organisationId;
        Title = title.Trim();
        Forecast = Normalize(forecast);
        OriginatingLeadId = originatingLeadId;
        OwnerUserId = ownerUserId;
        Stage = OpportunityStage.Discovery;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid OrganisationId { get; }
    public Guid? OriginatingLeadId { get; }
    public Guid? OwnerUserId { get; private set; }
    public string Title { get; private set; }
    public OpportunityStage Stage { get; private set; }
    public OpportunityForecast Forecast { get; private set; }
    public string? LossReason { get; private set; }

    public void MoveTo(OpportunityStage stage)
    {
        EnsureOpen();
        if (stage is OpportunityStage.Won or OpportunityStage.Lost)
            throw new InvalidOperationException("Use close methods for terminal stages.");
        if (!Enum.IsDefined(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        Stage = stage;
    }

    public void UpdateForecast(OpportunityForecast forecast)
    {
        EnsureOpen();
        ValidateForecast(forecast);
        Forecast = Normalize(forecast);
    }

    public void AssignOwner(Guid? ownerUserId)
    {
        EnsureOpen();
        OwnerUserId = ownerUserId;
    }

    public void MarkWon()
    {
        EnsureOpen();
        if (Forecast.EstimatedValue <= 0)
            throw new InvalidOperationException("Won opportunity must have a positive value.");
        Stage = OpportunityStage.Won;
        LossReason = null;
    }

    public void MarkLost(string reason)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Loss reason is required.", nameof(reason));
        Stage = OpportunityStage.Lost;
        LossReason = reason.Trim();
    }

    private void EnsureOpen()
    {
        if (Stage is OpportunityStage.Won or OpportunityStage.Lost)
            throw new InvalidOperationException("Closed opportunity cannot be changed.");
    }

    private static void ValidateForecast(OpportunityForecast forecast)
    {
        ArgumentNullException.ThrowIfNull(forecast);
        if (forecast.EstimatedValue < 0) throw new ArgumentOutOfRangeException(nameof(forecast));
        if (forecast.ProbabilityPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(forecast));
        if (string.IsNullOrWhiteSpace(forecast.CurrencyCode) || forecast.CurrencyCode.Trim().Length != 3)
            throw new ArgumentException("Currency code must be three characters.", nameof(forecast));
    }

    private static OpportunityForecast Normalize(OpportunityForecast forecast) =>
        forecast with { CurrencyCode = forecast.CurrencyCode.Trim().ToUpperInvariant() };
}
