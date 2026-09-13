namespace BusinessOS.Crm;

public enum LeadStatus
{
    New = 1,
    Contacted = 2,
    Qualified = 3,
    Unqualified = 4,
    Converted = 5
}

public sealed record LeadAttribution(
    string? LeadSource,
    Guid? OriginalPartnerId,
    Guid? SellingPartnerId,
    Guid? ServicePartnerId,
    Guid? AccountOwnerUserId,
    Guid? RenewalOwnerUserId);
