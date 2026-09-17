using BusinessOS.Api.Commerce;
using BusinessOS.Api.Tenancy;
using BusinessOS.Customers;
using Microsoft.Extensions.Options;

namespace BusinessOS.Api.Billing;

public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/billing");

        group.MapPost("/admin/orders/{orderId:guid}/invoice", CreateInvoiceAsync);
        group.MapGet("/admin/orders/{orderId:guid}/invoice", FindByOrderAsync);
        group.MapGet("/invoices/{invoiceId:guid}", FindInvoiceAsync);
        group.MapGet("/invoices/{invoiceId:guid}/receipt", FindReceiptAsync);
        group.MapGet("/history", ListHistoryAsync);
        return app;
    }

    private static async Task<IResult> CreateInvoiceAsync(
        Guid orderId,
        BillingInvoiceCreateRequest request,
        TenantContext tenant,
        ICommerceActivationStore commerce,
        IOrganisationRepository organisations,
        IBillingStore billing,
        IOptions<BillingOptions> options,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var forbidden = TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant);
        if (forbidden is not null) return forbidden;
        try
        {
            var source = await commerce.FindOrderBillingSourceAsync(
                tenant.TenantId, orderId, cancellationToken);
            if (source is null)
                return Results.NotFound(new ErrorResponse("Paid commerce order was not found."));
            var organisation = await organisations.GetAsync(
                tenant.TenantId, source.OrganisationId, cancellationToken);
            if (organisation is null)
                return Results.NotFound(new ErrorResponse("Customer organisation was not found."));
            var deploymentMode = configuration["BusinessOS:DeploymentMode"] ?? "Unknown";
            var draft = BillingCalculator.CreateDraft(
                source, organisation, options.Value, request, deploymentMode);
            var invoice = await billing.EnsureInvoiceAsync(
                draft, options.Value.InvoicePrefix, cancellationToken);
            return Results.Ok(invoice);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> FindByOrderAsync(
        Guid orderId,
        TenantContext tenant,
        IBillingStore billing,
        CancellationToken cancellationToken)
    {
        var forbidden = TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant);
        if (forbidden is not null) return forbidden;
        var invoice = await billing.FindInvoiceByOrderAsync(
            tenant.TenantId, orderId, cancellationToken);
        return invoice is null
            ? Results.NotFound(new ErrorResponse("Invoice was not found."))
            : Results.Ok(invoice);
    }

    private static async Task<IResult> FindInvoiceAsync(
        Guid invoiceId,
        TenantContext tenant,
        IBillingStore billing,
        CancellationToken cancellationToken)
    {
        var invoice = await billing.FindInvoiceAsync(
            tenant.TenantId, invoiceId, cancellationToken);
        return invoice is null
            ? Results.NotFound(new ErrorResponse("Invoice was not found."))
            : Results.Ok(invoice);
    }

    private static async Task<IResult> FindReceiptAsync(
        Guid invoiceId,
        TenantContext tenant,
        IBillingStore billing,
        CancellationToken cancellationToken)
    {
        var invoice = await billing.FindInvoiceAsync(
            tenant.TenantId, invoiceId, cancellationToken);
        if (invoice is null)
            return Results.NotFound(new ErrorResponse("Invoice was not found."));
        return Results.Ok(new BillingReceipt(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.OrderId,
            invoice.PaymentId,
            invoice.PaidAtUtc,
            invoice.Tax.GrossAmount,
            invoice.CurrencyCode,
            invoice.Description,
            invoice.DocumentMode));
    }

    private static async Task<IResult> ListHistoryAsync(
        Guid? organisationId,
        Guid? subscriptionId,
        int? take,
        TenantContext tenant,
        IBillingStore billing,
        CancellationToken cancellationToken)
    {
        var invoices = await billing.ListInvoicesAsync(
            tenant.TenantId,
            organisationId,
            subscriptionId,
            Math.Clamp(take ?? 50, 1, 200),
            cancellationToken);
        return Results.Ok(invoices);
    }
}
