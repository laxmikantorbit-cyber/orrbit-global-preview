using BusinessOS.Api.Commerce;
using BusinessOS.Customers;

namespace BusinessOS.Api.Billing;

public static class BillingCalculator
{
    public static BillingInvoiceDraft CreateDraft(
        CommerceBillingSource source,
        Organisation buyerOrganisation,
        BillingOptions options,
        BillingInvoiceCreateRequest request,
        string deploymentMode)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(buyerOrganisation);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(request);

        var sellerStateCode = NormalizeStateCode(
            request.SellerStateCode ?? options.SellerStateCode,
            "Seller state code");
        var buyerStateCode = NormalizeStateCode(
            request.BuyerStateCode ?? StateCodeFromGstin(buyerOrganisation.Gstin),
            "Buyer state code");
        var tax = CalculateInclusiveTax(
            source.GrossAmount, options.DefaultGstRate,
            sellerStateCode, buyerStateCode);

        var primaryAddress = buyerOrganisation.PrimaryAddress;
        var seller = new BillingPartySnapshot(
            options.SellerLegalName.Trim(),
            Clean(options.SellerGstin)?.ToUpperInvariant(),
            sellerStateCode,
            Clean(options.SellerAddressLine1),
            null,
            Clean(options.SellerCity),
            Clean(options.SellerState),
            Clean(options.SellerPostalCode),
            Clean(options.CountryCode)?.ToUpperInvariant() ?? "IN");
        var buyer = new BillingPartySnapshot(
            buyerOrganisation.LegalName ?? buyerOrganisation.Name,
            buyerOrganisation.Gstin,
            buyerStateCode,
            primaryAddress?.Line1,
            primaryAddress?.Line2,
            primaryAddress?.City,
            primaryAddress?.State,
            primaryAddress?.PostalCode,
            primaryAddress?.CountryCode ?? "IN");

        var financialYear = FinancialYear(source.PaidAtUtc);
        var invoiceType = source.IsRenewal ? "RenewalInvoice" : "TaxInvoice";
        var description = source.IsRenewal
            ? $"{source.ProductCode} subscription renewal"
            : $"{source.ProductCode} subscription";

        return new BillingInvoiceDraft(
            source.TenantId,
            source.OrganisationId,
            source.OrderId,
            source.SubscriptionId,
            invoiceType,
            source.PaidAtUtc,
            source.ProductCode,
            description,
            source.CurrencyCode,
            seller,
            buyer,
            tax,
            source.PaymentId,
            source.PaidAtUtc,
            financialYear,
            deploymentMode,
            LooksLikeGstin(seller.Gstin));
    }

    public static BillingTaxBreakdown CalculateInclusiveTax(
        decimal grossAmount,
        decimal gstRate,
        string sellerStateCode,
        string buyerStateCode)
    {
        if (grossAmount < 0) throw new ArgumentOutOfRangeException(nameof(grossAmount));
        if (gstRate < 0 || gstRate > 100) throw new ArgumentOutOfRangeException(nameof(gstRate));
        var taxable = gstRate == 0
            ? grossAmount
            : grossAmount / (1m + (gstRate / 100m));
        taxable = Money(taxable);
        var totalTax = Money(grossAmount - taxable);
        var intraState = string.Equals(
            sellerStateCode, buyerStateCode, StringComparison.Ordinal);
        var cgst = intraState ? Money(totalTax / 2m) : 0m;
        var sgst = intraState ? totalTax - cgst : 0m;
        var igst = intraState ? 0m : totalTax;

        return new BillingTaxBreakdown(
            gstRate,
            taxable,
            cgst,
            sgst,
            igst,
            totalTax,
            Money(grossAmount),
            intraState ? "IntraState" : "InterState");
    }

    public static string FinancialYear(DateTimeOffset date)
    {
        var localDate = date.UtcDateTime.Date;
        var startYear = localDate.Month >= 4 ? localDate.Year : localDate.Year - 1;
        var nextShort = (startYear + 1) % 100;
        return $"{startYear}-{nextShort:00}";
    }

    public static string? StateCodeFromGstin(string? gstin) =>
        LooksLikeGstin(gstin) ? gstin![..2] : null;

    private static string NormalizeStateCode(string? value, string field)
    {
        var normalized = Clean(value);
        if (normalized is null || normalized.Length != 2 ||
            !normalized.All(char.IsDigit))
            throw new ArgumentException($"{field} must be a two-digit GST state code.");
        return normalized;
    }

    private static bool LooksLikeGstin(string? value)
    {
        var normalized = Clean(value);
        return normalized is { Length: 15 } &&
               char.IsDigit(normalized[0]) &&
               char.IsDigit(normalized[1]);
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
