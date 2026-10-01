using BusinessOS.Api.Crm;
using BusinessOS.Sales;

namespace BusinessOS.Api.Tests;

public sealed class CrmInvoicePaymentStoreTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Tenant_Wide_Payment_List_Returns_All_Tenant_Payments_Newest_First()
    {
        var store = new InMemoryCrmInvoiceStore();
        var first = Invoice("INV-900001");
        var second = Invoice("INV-900002");
        first.ChangeStatus(SalesInvoiceStatus.Sent);
        second.ChangeStatus(SalesInvoiceStatus.Sent);
        await store.AddAsync(first);
        await store.AddAsync(second);

        var older = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        var newer = older.AddHours(2);
        await store.RecordPaymentAsync(TenantId, first.Id, 100m, "UPI", "P1", null, older, null);
        await store.RecordPaymentAsync(TenantId, second.Id, 200m, "Bank Transfer", "P2", null, newer, null);

        var payments = await store.ListPaymentsAsync(TenantId);

        Assert.Equal(2, payments.Count);
        Assert.Equal(second.Id, payments[0].InvoiceId);
        Assert.Equal(200m, payments[0].Amount);
        Assert.Equal(first.Id, payments[1].InvoiceId);
    }

    [Fact]
    public async Task Tenant_Wide_Payment_List_Does_Not_Leak_Other_Tenants()
    {
        var store = new InMemoryCrmInvoiceStore();
        var invoice = Invoice("INV-900003");
        invoice.ChangeStatus(SalesInvoiceStatus.Sent);
        await store.AddAsync(invoice);
        await store.RecordPaymentAsync(TenantId, invoice.Id, 100m, "Cash", null, null, DateTimeOffset.UtcNow, null);

        var otherTenant = await store.ListPaymentsAsync(Guid.NewGuid());

        Assert.Empty(otherTenant);
    }

    private static SalesInvoice Invoice(string number) => new(
        Guid.NewGuid(),
        TenantId,
        number,
        Guid.NewGuid(),
        "Reporting QA",
        [new SalesDocumentLine(Guid.NewGuid(), null, "Service", 1m, 1000m, 0m)],
        "INR",
        new DateOnly(2026, 10, 1),
        new DateOnly(2026, 10, 31));
}
