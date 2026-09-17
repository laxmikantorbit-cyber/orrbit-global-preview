using BusinessOS.Api.Billing;
using BusinessOS.Api.Commerce;
using BusinessOS.Api.Customers;
using BusinessOS.Api.Tenancy;
using BusinessOS.Application;
using BusinessOS.Licensing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BusinessOS.Api.Tests;

public sealed class BillingTests
{
    [Fact]
    public void Inclusive_Gst_Splits_Cgst_Sgst_For_IntraState()
    {
        var tax = BillingCalculator.CalculateInclusiveTax(118m, 18m, "22", "22");
        Assert.Equal(100m, tax.TaxableAmount);
        Assert.Equal(9m, tax.CgstAmount);
        Assert.Equal(9m, tax.SgstAmount);
        Assert.Equal(0m, tax.IgstAmount);
        Assert.Equal(18m, tax.TotalTax);
        Assert.Equal("IntraState", tax.SupplyType);
    }

    [Fact]
    public void Inclusive_Gst_Uses_Igst_For_InterState()
    {
        var tax = BillingCalculator.CalculateInclusiveTax(118m, 18m, "22", "27");
        Assert.Equal(100m, tax.TaxableAmount);
        Assert.Equal(18m, tax.IgstAmount);
        Assert.Equal(0m, tax.CgstAmount + tax.SgstAmount);
        Assert.Equal("InterState", tax.SupplyType);
    }

    [Fact]
    public void Financial_Year_Changes_On_April_First()
    {
        Assert.Equal("2026-27", BillingCalculator.FinancialYear(
            new DateTimeOffset(2027, 3, 31, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal("2027-28", BillingCalculator.FinancialYear(
            new DateTimeOffset(2027, 4, 1, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Billing_Automation_Creates_Idempotent_Invoice_For_Paid_Order()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore commerce = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(), signer);
        var activation = await commerce.ActivateInitialPurchaseAsync(
            PocIdentitySeed.TenantAId,
            new InitialActivationRequest(
                CustomerStore.TenantACustomerId, "AI_REPAIR", Guid.NewGuid(), Guid.NewGuid(),
                1, 17700m, "INR", 12, 1, 1, 10, 10, true,
                "pay_billing_test", DateTimeOffset.UtcNow));
        var billing = new InMemoryBillingStore();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["BusinessOS:DeploymentMode"] = "FreeTesting" }).Build();
        var service = new BillingAutomationService(
            commerce, CustomerSeed.CreateRepository(), billing,
            Options.Create(new BillingOptions()), configuration);

        var first = await service.EnsureForOrderAsync(
            PocIdentitySeed.TenantAId, activation.OrderId);
        var second = await service.EnsureForOrderAsync(
            PocIdentitySeed.TenantAId, activation.OrderId);

        Assert.NotNull(first);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal("TaxInvoice", first.InvoiceType);
        Assert.Equal(17700m, first.Tax.GrossAmount);
        Assert.Equal(18m, first.Tax.GstRate);
        Assert.Equal("FreeTesting", first.DocumentMode);
        Assert.False(first.TaxDocumentValid);
        Assert.Single(await billing.ListInvoicesAsync(
            PocIdentitySeed.TenantAId, CustomerStore.TenantACustomerId,
            activation.SubscriptionId, 50));
    }
}
