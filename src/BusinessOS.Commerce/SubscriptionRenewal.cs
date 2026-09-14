namespace BusinessOS.Commerce;

public sealed record SubscriptionRenewal(
    Guid Id,
    Guid SubscriptionId,
    Guid OrderId,
    DateTimeOffset PaidAtUtc,
    DateOnly? PreviousValidUntil,
    DateOnly NewValidUntil,
    int TermMonths,
    Guid PlanVersionId);
