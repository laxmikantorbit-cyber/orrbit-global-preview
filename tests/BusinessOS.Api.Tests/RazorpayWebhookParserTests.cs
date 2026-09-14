using BusinessOS.Api.Payments;
using BusinessOS.Payments;

namespace BusinessOS.Api.Tests;

public sealed class RazorpayWebhookParserTests
{
    [Fact]
    public void Captured_Webhook_Can_Parse_Without_Notes()
    {
        var webhook = RazorpayWebhookParser.Parse("""
            {
              "id":"evt_1",
              "event":"payment.captured",
              "payload":{"payment":{"entity":{
                "id":"pay_note_free_1",
                "order_id":"order_note_free_1",
                "amount":10000,
                "currency":"INR",
                "captured":true,
                "created_at":1789413600
              }}}
            }
            """);

        Assert.Null(webhook.TenantId);
        Assert.Null(webhook.SubscriptionId);
        Assert.Null(webhook.ProductCode);
        Assert.Equal("order_note_free_1", webhook.ProviderOrderId);
        Assert.Equal("order_note_free_1", webhook.Message.OrderId);
        Assert.Equal(PaymentStatus.Captured, webhook.Message.Status);
    }

    [Fact]
    public void Internal_Order_Note_Takes_Priority_When_Present()
    {
        var commerceOrderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var webhook = RazorpayWebhookParser.Parse(
            "{\"event\":\"payment.captured\",\"payload\":{\"payment\":{\"entity\":{" +
            "\"id\":\"pay_note_1\",\"order_id\":\"order_provider_1\"," +
            "\"amount\":10000,\"currency\":\"INR\",\"captured\":true," +
            "\"created_at\":1789413600,\"notes\":{" +
            $"\"tenantId\":\"{tenantId}\"," +
            $"\"commerceOrderId\":\"{commerceOrderId}\"," +
            "\"productCode\":\"ORRBIT-REPAIR\"}}}}}");

        Assert.Equal(tenantId, webhook.TenantId);
        Assert.Equal("ORRBIT-REPAIR", webhook.ProductCode);
        Assert.Equal("order_provider_1", webhook.ProviderOrderId);
        Assert.Equal(commerceOrderId.ToString(), webhook.Message.OrderId);
    }
}
