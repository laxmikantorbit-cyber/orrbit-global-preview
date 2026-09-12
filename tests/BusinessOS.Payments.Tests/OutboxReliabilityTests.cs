using BusinessOS.Payments;

namespace BusinessOS.Payments.Tests;

public sealed class OutboxReliabilityTests
{
    [Fact]
    public void Message_Can_Be_Retried_Until_Processed()
    {
        var item = new OutboxMessage(Guid.NewGuid(), "Test", "1", false, 0, null);
        var messages = new List<OutboxMessage> { item };
        var dispatcher = new OutboxDispatcher(messages);

        var first = dispatcher.TryDispatch(item.Id, NeedsRetry);
        var second = dispatcher.TryDispatch(item.Id, Completes);

        Assert.False(first);
        Assert.True(second);
        Assert.Equal(2, messages[0].Attempts);
        Assert.True(messages[0].Processed);
    }

    private static bool NeedsRetry(OutboxMessage message) => false;
    private static bool Completes(OutboxMessage message) => true;
}
