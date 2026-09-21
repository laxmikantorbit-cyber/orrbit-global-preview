using BusinessOS.Api.Commerce;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmInvoiceEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/invoices", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmInvoiceStore invoices,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var items = await invoices.ListAsync(DemoTenantId, cancellationToken);
            return Results.Ok(new { invoices = items.Select(ToResponse).ToArray() });
        });

        group.MapGet("/invoices/{invoiceId:guid}", async (
            Guid invoiceId,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmInvoiceStore invoices,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var invoice = await invoices.GetAsync(DemoTenantId, invoiceId, cancellationToken);
            if (invoice is null) return Results.NotFound(new ErrorResponse("Invoice not found."));
            var payments = await invoices.ListPaymentsAsync(DemoTenantId, invoiceId, cancellationToken);
            return Results.Ok(new CrmInvoiceDetailResponse(ToResponse(invoice), payments.Select(ToPaymentResponse).ToArray()));
        });

        group.MapPost("/invoices", async (
            CreateCrmInvoiceRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmOpportunityStore opportunities,
            ICrmInvoiceStore invoices,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            if (await accounts.GetAsync(DemoTenantId, request.AccountId, cancellationToken) is null)
                return Results.NotFound(new ErrorResponse("Customer not found."));
            var opportunityError = await ValidateOpportunityAsync(
                request.OpportunityId, request.AccountId, opportunities, cancellationToken);
            if (opportunityError is not null) return opportunityError;

            try
            {
                var number = await invoices.NextInvoiceNumberAsync(DemoTenantId, cancellationToken);
                var invoice = new SalesInvoice(
                    Guid.NewGuid(),
                    DemoTenantId,
                    number,
                    request.AccountId,
                    request.Subject,
                    ToLines(request.Lines),
                    Currency(request.CurrencyCode),
                    request.IssueDate,
                    request.DueDate,
                    request.DiscountPercent,
                    request.OpportunityId,
                    null,
                    request.Notes,
                    request.Terms);

                await invoices.AddAsync(invoice, cancellationToken);
                await AuditAsync(management, context, "InvoiceCreated", "Invoice", invoice.Id,
                    $"{invoice.InvoiceNumber}; total={invoice.Totals.Total:0.00}", cancellationToken);
                return Results.Ok(ToResponse(invoice));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/sales-documents/{documentId:guid}/invoice", async (
            Guid documentId,
            ConvertSalesDocumentToInvoiceRequest? request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmSalesDocumentStore documents,
            ICrmInvoiceStore invoices,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var document = await documents.GetAsync(DemoTenantId, documentId, cancellationToken);
            if (document is null) return Results.NotFound(new ErrorResponse("Sales document not found."));
            if (document.Status != SalesDocumentStatus.Accepted)
                return Results.BadRequest(new ErrorResponse("Only accepted proposals or estimates can be converted to an invoice."));

            try
            {
                var number = await invoices.NextInvoiceNumberAsync(DemoTenantId, cancellationToken);
                var issueDate = request?.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
                var dueDate = request?.DueDate ?? issueDate.AddDays(7);
                var invoice = new SalesInvoice(
                    Guid.NewGuid(),
                    DemoTenantId,
                    number,
                    document.AccountId,
                    document.Subject,
                    document.Lines,
                    document.CurrencyCode,
                    issueDate,
                    dueDate,
                    document.DiscountPercent,
                    document.OpportunityId,
                    document.Id,
                    document.Notes,
                    document.Terms);

                await invoices.AddAsync(invoice, cancellationToken);
                await AuditAsync(management, context, "SalesDocumentConvertedToInvoice", document.Kind.ToString(),
                    document.Id, $"{document.DocumentNumber} -> {invoice.InvoiceNumber}", cancellationToken);
                return Results.Ok(ToResponse(invoice));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/invoices/{invoiceId:guid}/profile", async (
            Guid invoiceId,
            UpdateCrmInvoiceRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmOpportunityStore opportunities,
            ICrmInvoiceStore invoices,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var invoice = await invoices.GetAsync(DemoTenantId, invoiceId, cancellationToken);
            if (invoice is null) return Results.NotFound(new ErrorResponse("Invoice not found."));
            if (await accounts.GetAsync(DemoTenantId, request.AccountId, cancellationToken) is null)
                return Results.NotFound(new ErrorResponse("Customer not found."));
            var opportunityError = await ValidateOpportunityAsync(
                request.OpportunityId, request.AccountId, opportunities, cancellationToken);
            if (opportunityError is not null) return opportunityError;

            try
            {
                invoice.UpdateDraft(
                    request.AccountId,
                    request.Subject,
                    ToLines(request.Lines),
                    Currency(request.CurrencyCode),
                    request.IssueDate,
                    request.DueDate,
                    request.DiscountPercent,
                    request.OpportunityId,
                    request.Notes,
                    request.Terms);

                await invoices.SaveAsync(invoice, cancellationToken);
                await AuditAsync(management, context, "InvoiceUpdated", "Invoice", invoice.Id,
                    $"{invoice.InvoiceNumber}; total={invoice.Totals.Total:0.00}", cancellationToken);
                return Results.Ok(ToResponse(invoice));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/invoices/{invoiceId:guid}/status", async (
            Guid invoiceId,
            ChangeCrmInvoiceStatusRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmInvoiceStore invoices,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var invoice = await invoices.GetAsync(DemoTenantId, invoiceId, cancellationToken);
            if (invoice is null) return Results.NotFound(new ErrorResponse("Invoice not found."));
            if (!Enum.TryParse<SalesInvoiceStatus>(request.Status, true, out var status))
                return Results.BadRequest(new ErrorResponse("Valid invoice status is required."));

            try
            {
                invoice.ChangeStatus(status, request.AsOf);
                await invoices.SaveAsync(invoice, cancellationToken);
                await AuditAsync(management, context, "InvoiceStatusChanged", "Invoice", invoice.Id,
                    $"{invoice.InvoiceNumber}; status={invoice.Status}", cancellationToken);
                return Results.Ok(ToResponse(invoice));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapGet("/invoices/{invoiceId:guid}/payments", async (
            Guid invoiceId,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmInvoiceStore invoices,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            if (await invoices.GetAsync(DemoTenantId, invoiceId, cancellationToken) is null)
                return Results.NotFound(new ErrorResponse("Invoice not found."));
            var payments = await invoices.ListPaymentsAsync(DemoTenantId, invoiceId, cancellationToken);
            return Results.Ok(new { payments = payments.Select(ToPaymentResponse).ToArray() });
        });

        group.MapPost("/invoices/{invoiceId:guid}/payments", async (
            Guid invoiceId,
            CreateCrmInvoicePaymentRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmInvoiceStore invoices,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);

            try
            {
                var result = await invoices.RecordPaymentAsync(
                    DemoTenantId,
                    invoiceId,
                    request.Amount,
                    request.Method,
                    request.Reference,
                    request.Notes,
                    request.ReceivedAtUtc ?? DateTimeOffset.UtcNow,
                    member.Id,
                    cancellationToken);

                await AuditAsync(management, context, "InvoicePaymentRecorded", "Invoice", invoiceId,
                    $"{result.Payment.PaymentNumber}; amount={result.Payment.Amount:0.00}; balance={result.Invoice.Balance:0.00}",
                    cancellationToken);

                return Results.Ok(new CrmInvoicePaymentResultResponse(
                    ToResponse(result.Invoice),
                    ToPaymentResponse(result.Payment)));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        return app;
    }

    private static async Task<IResult?> ValidateOpportunityAsync(
        Guid? opportunityId,
        Guid accountId,
        ICrmOpportunityStore opportunities,
        CancellationToken cancellationToken)
    {
        if (!opportunityId.HasValue) return null;
        var opportunity = await opportunities.GetAsync(DemoTenantId, opportunityId.Value, cancellationToken);
        if (opportunity is null) return Results.NotFound(new ErrorResponse("Opportunity not found."));
        if (opportunity.OrganisationId != accountId)
            return Results.BadRequest(new ErrorResponse("Opportunity belongs to a different customer."));
        return null;
    }

    private static IReadOnlyList<SalesDocumentLine> ToLines(IReadOnlyList<CrmSalesDocumentLineRequest>? lines) =>
        (lines ?? []).Select(line => new SalesDocumentLine(
            line.Id ?? Guid.NewGuid(),
            line.ItemId,
            line.Description,
            line.Quantity,
            line.UnitPrice,
            line.TaxPercent)).ToArray();

    private static string Currency(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "INR" : value.Trim();

    internal static CrmInvoiceResponse ToResponse(SalesInvoice invoice)
    {
        var totals = invoice.Totals;
        return new CrmInvoiceResponse(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.AccountId,
            invoice.OpportunityId,
            invoice.SourceDocumentId,
            invoice.Subject,
            invoice.Status.ToString(),
            invoice.CurrencyCode,
            invoice.IssueDate,
            invoice.DueDate,
            invoice.DiscountPercent,
            invoice.AmountPaid,
            invoice.AmountCredited,
            invoice.NetTotal,
            invoice.Balance,
            invoice.OverpaidAmount,
            invoice.Notes,
            invoice.Terms,
            invoice.Lines.Select(line => new CrmSalesDocumentLineResponse(
                line.Id,
                line.ItemId,
                line.Description,
                line.Quantity,
                line.UnitPrice,
                line.TaxPercent,
                decimal.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero))).ToArray(),
            totals.Subtotal,
            totals.DiscountAmount,
            totals.TaxAmount,
            totals.Total,
            invoice.CreatedAtUtc,
            invoice.UpdatedAtUtc);
    }

    private static CrmInvoicePaymentResponse ToPaymentResponse(SalesInvoicePayment payment) =>
        new(
            payment.Id,
            payment.InvoiceId,
            payment.PaymentNumber,
            payment.Amount,
            payment.Method,
            payment.Reference,
            payment.Notes,
            payment.ReceivedAtUtc,
            payment.ReceivedByUserId,
            payment.CreatedAtUtc);

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() =>
        Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));

    private static async Task AuditAsync(
        ICrmManagementStore management,
        HttpContext context,
        string action,
        string entityType,
        Guid entityId,
        string? detail,
        CancellationToken cancellationToken)
    {
        var member = CrmFreeTestingAccessMiddleware.Current(context);
        await management.AddAuditAsync(new CrmAuditEntry(
            Guid.NewGuid(),
            DemoTenantId,
            member.Id,
            action,
            entityType,
            entityId.ToString(),
            detail,
            DateTimeOffset.UtcNow),
            cancellationToken);
    }
}

public sealed record CreateCrmInvoiceRequest(
    Guid AccountId,
    Guid? OpportunityId,
    string Subject,
    string? CurrencyCode,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    decimal DiscountPercent,
    string? Notes,
    string? Terms,
    IReadOnlyList<CrmSalesDocumentLineRequest> Lines);

public sealed record UpdateCrmInvoiceRequest(
    Guid AccountId,
    Guid? OpportunityId,
    string Subject,
    string? CurrencyCode,
    DateOnly IssueDate,
    DateOnly DueDate,
    decimal DiscountPercent,
    string? Notes,
    string? Terms,
    IReadOnlyList<CrmSalesDocumentLineRequest> Lines);

public sealed record ChangeCrmInvoiceStatusRequest(string Status, DateOnly? AsOf);

public sealed record ConvertSalesDocumentToInvoiceRequest(DateOnly? IssueDate, DateOnly? DueDate);

public sealed record CreateCrmInvoicePaymentRequest(
    decimal Amount,
    string Method,
    string? Reference,
    string? Notes,
    DateTimeOffset? ReceivedAtUtc);

public sealed record CrmInvoiceResponse(
    Guid Id,
    string InvoiceNumber,
    Guid AccountId,
    Guid? OpportunityId,
    Guid? SourceDocumentId,
    string Subject,
    string Status,
    string CurrencyCode,
    DateOnly IssueDate,
    DateOnly DueDate,
    decimal DiscountPercent,
    decimal AmountPaid,
    decimal AmountCredited,
    decimal NetTotal,
    decimal Balance,
    decimal OverpaidAmount,
    string? Notes,
    string? Terms,
    IReadOnlyList<CrmSalesDocumentLineResponse> Lines,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal Total,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CrmInvoicePaymentResponse(
    Guid Id,
    Guid InvoiceId,
    string PaymentNumber,
    decimal Amount,
    string Method,
    string? Reference,
    string? Notes,
    DateTimeOffset ReceivedAtUtc,
    Guid? ReceivedByUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record CrmInvoiceDetailResponse(
    CrmInvoiceResponse Invoice,
    IReadOnlyList<CrmInvoicePaymentResponse> Payments);

public sealed record CrmInvoicePaymentResultResponse(
    CrmInvoiceResponse Invoice,
    CrmInvoicePaymentResponse Payment);
