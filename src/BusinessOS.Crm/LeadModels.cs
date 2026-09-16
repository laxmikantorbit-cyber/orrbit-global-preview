namespace BusinessOS.Crm;

public enum LeadStatus
{
    New = 1,
    Contacted = 2,
    Qualified = 3,
    Unqualified = 4,
    Converted = 5
}

public enum LeadPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4
}

public enum CrmActivityType
{
    Created = 1,
    Note = 2,
    Call = 3,
    WhatsApp = 4,
    Email = 5,
    Meeting = 6,    StatusChanged = 7,
    AssignmentChanged = 8,
    FollowUpScheduled = 9,
    FollowUpCompleted = 10,
    TaskCreated = 11,
    TaskCompleted = 12,
    ProfileUpdated = 13,
    Converted = 14
}

public enum FollowUpChannel
{
    Call = 1,
    WhatsApp = 2,
    Email = 3,
    Meeting = 4,
    Other = 5
}

public enum CrmWorkStatus
{
    Open = 1,
    Completed = 2,
    Cancelled = 3
}

public sealed record LeadAttribution(
    string? LeadSource,
    Guid? OriginalPartnerId,
    Guid? SellingPartnerId,
    Guid? ServicePartnerId,
    Guid? AccountOwnerUserId,
    Guid? RenewalOwnerUserId);
