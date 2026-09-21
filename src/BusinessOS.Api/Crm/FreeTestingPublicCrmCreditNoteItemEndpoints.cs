using BusinessOS.Api.Commerce;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmCreditNoteItemEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmCreditNoteItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/sales-items", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmSalesItemStore items,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var result = await items.ListAsync(DemoTenantId, cancellationToken);
            return Results.Ok(new { items = result.Select(ToItemResponse).ToArray() });
        });

        group.MapPost("/sales-items", async (
            CreateCrmSalesItemRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmSalesItemStore items,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            try
            {
                var item = new SalesItem(
                    Guid.NewGuid(), DemoTenantId, request.Code, request.Name, request.Description,
                    request.DefaultRate, request.DefaultTaxPercent,
                    ParseItemStatus(request.Status), request.CatalogProductId);
                await items.AddAsync(item, cancellationToken);
                await AuditAsync(management, context, "SalesItemCreated", "SalesItem", item.Id,
                    $"{item.Code}; rate={item.DefaultRate:0.00}; tax={item.DefaultTaxPercent:0.##}", cancellationToken);
                return Results.Ok(ToItemResponse(item));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/sales-items/{itemId:guid}/profile", async (
            Guid itemId,
            UpdateCrmSalesItemRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmSalesItemStore items,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await items.GetAsync(DemoTenantId, itemId, cancellationToken);
            if (item is null) return Results.NotFound(new ErrorResponse("Sales item not found."));
            try
            {
                item.Update(
                    request.Name, request.Description, request.DefaultRate,
                    request.DefaultTaxPercent, ParseItemStatus(request.Status));
                await items.SaveAsync(item, cancellationToken);
                await AuditAsync(management, context, "SalesItemUpdated", "SalesItem", item.Id,
                    $"{item.Code}; status={item.Status}", cancellationToken);
                return Results.Ok(ToItemResponse(item));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapGet("/credit-notes", async (
            Guid? invoiceId,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmCreditNoteStore creditNotes,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var notes = await creditNotes.ListAsync(DemoTenantId, cancellationToken);
            if (invoiceId.HasValue) notes = notes.Where(x => x.InvoiceId == invoiceId.Value).ToArray();
            return Results.Ok(new { creditNotes = notes.Select(ToCreditResponse).ToArray() });
        });

        group.MapGet("/credit-notes/{creditNoteId:guid}", async (
            Guid creditNoteId,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmCreditNoteStore creditNotes,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var note = await creditNotes.GetAsync(DemoTenantId, creditNoteId, cancellationToken);
            return note is null
                ? Results.NotFound(new ErrorResponse("Credit note not found."))
                : Results.Ok(ToCreditResponse(note));
        });

        group.MapPost("/credit-notes", async (
            CreateCrmCreditNoteRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmInvoiceStore invoices,
            ICrmCreditNoteStore creditNotes,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var invoice = await invoices.GetAsync(DemoTenantId, request.InvoiceId, cancellationToken);
            if (invoice is null) return Results.NotFound(new ErrorResponse("Invoice not found."));
            if (invoice.Status is SalesInvoiceStatus.Draft or SalesInvoiceStatus.Void)
                return Results.BadRequest(new ErrorResponse("Credit note requires a sent or settled invoice."));
            if (request.Amount > invoice.Totals.Total - invoice.AmountCredited)
                return Results.BadRequest(new ErrorResponse("Credit note cannot exceed the remaining creditable invoice amount."));

            try
            {
                var number = await creditNotes.NextNumberAsync(DemoTenantId, cancellationToken);
                var note = new CreditNote(
                    Guid.NewGuid(), DemoTenantId, number, invoice.Id, invoice.AccountId,
                    request.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                    request.Amount, request.Reason, request.Notes);
                await creditNotes.AddAsync(note, cancellationToken);
                await AuditAsync(management, context, "CreditNoteCreated", "CreditNote", note.Id,
                    $"{note.CreditNoteNumber}; invoice={invoice.InvoiceNumber}; amount={note.Amount:0.00}", cancellationToken);
                return Results.Ok(ToCreditResponse(note));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/credit-notes/{creditNoteId:guid}/profile", async (
            Guid creditNoteId,
            UpdateCrmCreditNoteRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmInvoiceStore invoices,
            ICrmCreditNoteStore creditNotes,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var note = await creditNotes.GetAsync(DemoTenantId, creditNoteId, cancellationToken);
            if (note is null) return Results.NotFound(new ErrorResponse("Credit note not found."));
            var invoice = await invoices.GetAsync(DemoTenantId, note.InvoiceId, cancellationToken);
            if (invoice is null) return Results.NotFound(new ErrorResponse("Invoice not found."));
            if (request.Amount > invoice.Totals.Total - invoice.AmountCredited)
                return Results.BadRequest(new ErrorResponse("Credit note cannot exceed the remaining creditable invoice amount."));

            try
            {
                note.UpdateDraft(request.IssueDate, request.Amount, request.Reason, request.Notes);
                await creditNotes.SaveAsync(note, cancellationToken);
                await AuditAsync(management, context, "CreditNoteUpdated", "CreditNote", note.Id,
                    $"{note.CreditNoteNumber}; amount={note.Amount:0.00}", cancellationToken);
                return Results.Ok(ToCreditResponse(note));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/credit-notes/{creditNoteId:guid}/issue", async (
            Guid creditNoteId,
            ChangeCrmCreditNoteSettlementRequest? request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmCreditNoteStore creditNotes,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            try
            {
                var result = await creditNotes.IssueAsync(
                    DemoTenantId, creditNoteId, request?.AsOf, cancellationToken);
                await AuditAsync(management, context, "CreditNoteIssued", "CreditNote", result.Note.Id,
                    $"{result.Note.CreditNoteNumber}; credited={result.Invoice.AmountCredited:0.00}; balance={result.Invoice.Balance:0.00}",
                    cancellationToken);
                return Results.Ok(new CrmCreditNoteAdjustmentResponse(
                    ToCreditResponse(result.Note),
                    FreeTestingPublicCrmInvoiceEndpoints.ToResponse(result.Invoice)));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/credit-notes/{creditNoteId:guid}/void", async (
            Guid creditNoteId,
            ChangeCrmCreditNoteSettlementRequest? request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmCreditNoteStore creditNotes,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            try
            {
                var result = await creditNotes.VoidAsync(
                    DemoTenantId, creditNoteId, request?.AsOf, cancellationToken);
                await AuditAsync(management, context, "CreditNoteVoided", "CreditNote", result.Note.Id,
                    $"{result.Note.CreditNoteNumber}; credited={result.Invoice.AmountCredited:0.00}; balance={result.Invoice.Balance:0.00}",
                    cancellationToken);
                return Results.Ok(new CrmCreditNoteAdjustmentResponse(
                    ToCreditResponse(result.Note),
                    FreeTestingPublicCrmInvoiceEndpoints.ToResponse(result.Invoice)));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        return app;
    }

    private static SalesItemStatus ParseItemStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return SalesItemStatus.Active;
        if (!Enum.TryParse<SalesItemStatus>(status, true, out var value))
            throw new ArgumentException("Item status must be Active or Inactive.");
        return value;
    }

    private static CrmSalesItemResponse ToItemResponse(SalesItem item) =>
        new(
            item.Id, item.Code, item.Name, item.Description, item.DefaultRate,
            item.DefaultTaxPercent, item.Status.ToString(), item.CatalogProductId,
            item.CreatedAtUtc, item.UpdatedAtUtc);

    private static CrmCreditNoteResponse ToCreditResponse(CreditNote note) =>
        new(
            note.Id, note.CreditNoteNumber, note.InvoiceId, note.AccountId,
            note.IssueDate, note.Amount, note.Reason, note.Notes, note.Status.ToString(),
            note.CreatedAtUtc, note.UpdatedAtUtc);

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() =>
        Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));

    private static async Task AuditAsync(
        ICrmManagementStore management, HttpContext context, string action, string entityType,
        Guid entityId, string? detail, CancellationToken cancellationToken)
    {
        var member = CrmFreeTestingAccessMiddleware.Current(context);
        await management.AddAuditAsync(new CrmAuditEntry(
            Guid.NewGuid(), DemoTenantId, member.Id, action, entityType, entityId.ToString(),
            detail, DateTimeOffset.UtcNow), cancellationToken);
    }
}

public sealed record CreateCrmSalesItemRequest(
    string Code,
    string Name,
    string? Description,
    decimal DefaultRate,
    decimal DefaultTaxPercent,
    string? Status,
    Guid? CatalogProductId);

public sealed record UpdateCrmSalesItemRequest(
    string Name,
    string? Description,
    decimal DefaultRate,
    decimal DefaultTaxPercent,
    string Status);

public sealed record CrmSalesItemResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    decimal DefaultRate,
    decimal DefaultTaxPercent,
    string Status,
    Guid? CatalogProductId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateCrmCreditNoteRequest(
    Guid InvoiceId,
    DateOnly? IssueDate,
    decimal Amount,
    string Reason,
    string? Notes);

public sealed record UpdateCrmCreditNoteRequest(
    DateOnly IssueDate,
    decimal Amount,
    string Reason,
    string? Notes);

public sealed record ChangeCrmCreditNoteSettlementRequest(DateOnly? AsOf);

public sealed record CrmCreditNoteResponse(
    Guid Id,
    string CreditNoteNumber,
    Guid InvoiceId,
    Guid AccountId,
    DateOnly IssueDate,
    decimal Amount,
    string Reason,
    string? Notes,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CrmCreditNoteAdjustmentResponse(
    CrmCreditNoteResponse CreditNote,
    CrmInvoiceResponse Invoice);
