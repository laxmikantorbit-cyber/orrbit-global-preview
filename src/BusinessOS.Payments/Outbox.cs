namespace BusinessOS.Payments;

public sealed record OutboxMessage(
    Guid Id,
    string Type,
    string AggregateId,
    bool Processed,
    int Attempts,
    string? LastError);

public sealed class OutboxDispatcher
{
    private readonly List<OutboxMessage> _messages;

    public OutboxDispatcher(List<OutboxMessage> messages) => _messages = messages;

    public bool TryDispatch(Guid id, Func<OutboxMessage, bool> handler)
    {
        var index = _messages.FindIndex(x => x.Id == id);
        if (index < 0) throw new InvalidOperationException("Outbox message was not found.");

        var current = _messages[index];
        if (current.Processed) return true;

        var attempt = current with { Attempts = current.Attempts + 1 };
        _messages[index] = attempt;

        try
        {
            if (!handler(attempt))
            {
                _messages[index] = attempt with { LastError = "Handler returned false." };
                return false;
            }

            _messages[index] = attempt with { Processed = true, LastError = null };
            return true;
        }
        catch (Exception ex)
        {
            _messages[index] = attempt with { LastError = ex.Message };
            return false;
        }
    }
}
