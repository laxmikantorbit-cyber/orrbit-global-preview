namespace BusinessOS.Payments;

public enum PaymentStatus
{
    Pending = 0,
    Failed = 1,
    Captured = 2
}

public sealed record PaymentWebhookMessage(
    string EventId,
    string PaymentId,
    string OrderId,
    PaymentStatus Status,
    long AmountPaise,
    string Currency);

public sealed record PaymentRecord(
    string PaymentId,
    string OrderId,
    PaymentStatus Status,
    long AmountPaise,
    string Currency);

public sealed record PaymentProcessResult(
    bool Accepted,
    bool Duplicate,
    PaymentRecord Payment);
