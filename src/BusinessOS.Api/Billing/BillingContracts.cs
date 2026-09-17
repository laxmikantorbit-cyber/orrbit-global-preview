namespace BusinessOS.Api.Billing;

public sealed record BillingInvoiceCreateRequest(
    string? BuyerStateCode = null,
    string? SellerStateCode = null);

public sealed record BillingPartySnapshot(
    string LegalName,
    string? Gstin,
    string? StateCode,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

public sealed record BillingTaxBreakdown(
    decimal GstRate,
    decimal TaxableAmount,
    decimal CgstAmount,
    decimal SgstAmount,
    decimal IgstAmount,
    decimal TotalTax,
    decimal GrossAmount,
    string SupplyType);

public sealed record BillingInvoice(
    Guid Id,
    Guid TenantId,
    Guid OrganisationId,
    Guid OrderId,
    Guid? SubscriptionId,
    string InvoiceNumber,
    string InvoiceType,
    DateTimeOffset IssuedAtUtc,
    string ProductCode,
    string Description,
    string CurrencyCode,
    BillingPartySnapshot Seller,
    BillingPartySnapshot Buyer,
    BillingTaxBreakdown Tax,
    string PaymentId,
    DateTimeOffset PaidAtUtc,
    string FinancialYear,
    string DocumentMode,
    bool TaxDocumentValid);

public sealed record BillingReceipt(
    Guid InvoiceId,
    string InvoiceNumber,
    Guid OrderId,
    string PaymentId,
    DateTimeOffset PaidAtUtc,
    decimal AmountReceived,
    string CurrencyCode,
    string ReceiptFor,
    string DocumentMode);

public sealed record BillingInvoiceDraft(
    Guid TenantId,
    Guid OrganisationId,
    Guid OrderId,
    Guid? SubscriptionId,
    string InvoiceType,
    DateTimeOffset IssuedAtUtc,
    string ProductCode,
    string Description,
    string CurrencyCode,
    BillingPartySnapshot Seller,
    BillingPartySnapshot Buyer,
    BillingTaxBreakdown Tax,
    string PaymentId,
    DateTimeOffset PaidAtUtc,
    string FinancialYear,
    string DocumentMode,
    bool TaxDocumentValid);

public sealed class BillingOptions
{
    public string InvoicePrefix { get; set; } = "INV";
    public decimal DefaultGstRate { get; set; } = 18m;
    public string SellerLegalName { get; set; } = "oRRbit™ Slickteq Softech Pvt. Ltd. (FreeTesting)";
    public string? SellerGstin { get; set; }
    public string? SellerStateCode { get; set; } = "22";
    public string? SellerAddressLine1 { get; set; }
    public string? SellerCity { get; set; }
    public string? SellerState { get; set; }
    public string? SellerPostalCode { get; set; }
    public string CountryCode { get; set; } = "IN";
}
