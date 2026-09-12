namespace BusinessOS.Payments;

public sealed class PaymentProcessor
{
    private readonly HashSet<string> _processedEventIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PaymentRecord> _payments = new(StringComparer.Ordinal);
    private readonly List<OutboxMessage> _outbox = [];

    public IReadOnlyCollection<PaymentRecord> Payments => _payments.Values;
    public IReadOnlyList<OutboxMessage> Outbox => _outbox.AsReadOnly();

    public PaymentProcessResult Process(PaymentWebhookMessage message)
    {
        Validate(message);

        if (_processedEventIds.Contains(message.EventId))
        {
            var prior = _payments[message.PaymentId];
            return new PaymentProcessResult(true, true, prior);
        }

        if (_payments.TryGetValue(message.PaymentId, out var existing))
        {
            EnsureSameCommercialIdentity(existing, message);
            var result = ProcessExisting(existing, message);
            _processedEventIds.Add(message.EventId);
            return result;
        }

        var created = new PaymentRecord(
            message.PaymentId,
            message.OrderId,
            message.Status,
            message.AmountPaise,
            message.Currency);
        _payments.Add(created.PaymentId, created);
        if (created.Status == PaymentStatus.Captured)
            EnqueueEntitlementUpdate(created);

        _processedEventIds.Add(message.EventId);
        return new PaymentProcessResult(true, false, created);
    }

    private PaymentProcessResult ProcessExisting(
        PaymentRecord existing,
        PaymentWebhookMessage message)
    {
        if (existing.Status == PaymentStatus.Captured)
            return new PaymentProcessResult(true, true, existing);

        var updated = existing with { Status = message.Status };
        _payments[existing.PaymentId] = updated;

        if (message.Status == PaymentStatus.Captured)
            EnqueueEntitlementUpdate(updated);

        var noChange = existing.Status == message.Status;
        return new PaymentProcessResult(true, noChange, updated);
    }

    private void EnqueueEntitlementUpdate(PaymentRecord payment)
    {
        if (_outbox.Any(x =>
                x.Type == "PaymentCaptured" &&
                x.AggregateId == payment.PaymentId))
            return;

        _outbox.Add(new OutboxMessage(
            Guid.NewGuid(),
            "PaymentCaptured",
            payment.PaymentId,
            false,
            0,
            null));
    }

    private static void EnsureSameCommercialIdentity(
        PaymentRecord existing,
        PaymentWebhookMessage incoming)
    {
        if (existing.OrderId != incoming.OrderId ||
            existing.AmountPaise != incoming.AmountPaise ||
            !string.Equals(existing.Currency, incoming.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Payment identity mismatch.");
    }

    private static void Validate(PaymentWebhookMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.EventId))
            throw new ArgumentException("Event id is required.");
        if (string.IsNullOrWhiteSpace(message.PaymentId))
            throw new ArgumentException("Payment id is required.");
        if (string.IsNullOrWhiteSpace(message.OrderId))
            throw new ArgumentException("Order id is required.");
        if (message.AmountPaise <= 0)
            throw new ArgumentOutOfRangeException(nameof(message.AmountPaise));
        if (string.IsNullOrWhiteSpace(message.Currency))
            throw new ArgumentException("Currency is required.");
    }
}
