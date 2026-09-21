using BusinessOS.Api.Commerce;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmSalesDocumentEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmSalesDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/sales-documents", async (
            string? kind,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmSalesDocumentStore documents,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            SalesDocumentKind? parsedKind = null;
            if (!string.IsNullOrWhiteSpace(kind))
            {
                if (!Enum.TryParse<SalesDocumentKind>(kind, true, out var value))
                    return Results.BadRequest(new ErrorResponse("Kind must be Proposal or Estimate."));
                parsedKind = value;
            }
            var items = await documents.ListAsync(DemoTenantId, parsedKind, cancellationToken);
            return Results.Ok(new { documents = items.Select(ToResponse).ToArray() });
        });

        group.MapGet("/sales-documents/{documentId:guid}", async (
            Guid documentId,
            IConfiguration configuration,
            IHostEnvironment environment,
            ICrmSalesDocumentStore documents,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await documents.GetAsync(DemoTenantId, documentId, cancellationToken);
            return item is null
                ? Results.NotFound(new ErrorResponse("Sales document not found."))
                : Results.Ok(ToResponse(item));
        });

        group.MapPost("/sales-documents", async (
            CreateCrmSalesDocumentRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmOpportunityStore opportunities,
            ICrmSalesDocumentStore documents,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            if (!Enum.TryParse<SalesDocumentKind>(request.Kind, true, out var kind))
                return Results.BadRequest(new ErrorResponse("Kind must be Proposal or Estimate."));
            if (await accounts.GetAsync(DemoTenantId, request.AccountId, cancellationToken) is null)
                return Results.NotFound(new ErrorResponse("Customer not found."));
            var opportunityError = await ValidateOpportunityAsync(
                request.OpportunityId, request.AccountId, opportunities, cancellationToken);
            if (opportunityError is not null) return opportunityError;

            try
            {
                var number = await documents.NextNumberAsync(DemoTenantId, kind, cancellationToken);
                var document = new SalesDocument(
                    Guid.NewGuid(), DemoTenantId, kind, number, request.AccountId, request.Subject,
                    ToLines(request.Lines), Currency(request.CurrencyCode), request.IssueDate,
                    request.ExpiryDate, request.DiscountPercent, request.OpportunityId, request.Notes, request.Terms);
                await documents.AddAsync(document, cancellationToken);
                await AuditAsync(management, context, "SalesDocumentCreated", kind.ToString(), document.Id,
                    $"{document.DocumentNumber}; total={document.Totals.Total:0.00}", cancellationToken);
                return Results.Ok(ToResponse(document));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/sales-documents/{documentId:guid}/profile", async (
            Guid documentId,
            UpdateCrmSalesDocumentRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmAccountStore accounts,
            ICrmOpportunityStore opportunities,
            ICrmSalesDocumentStore documents,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var document = await documents.GetAsync(DemoTenantId, documentId, cancellationToken);
            if (document is null) return Results.NotFound(new ErrorResponse("Sales document not found."));
            if (await accounts.GetAsync(DemoTenantId, request.AccountId, cancellationToken) is null)
                return Results.NotFound(new ErrorResponse("Customer not found."));
            var opportunityError = await ValidateOpportunityAsync(
                request.OpportunityId, request.AccountId, opportunities, cancellationToken);
            if (opportunityError is not null) return opportunityError;

            try
            {
                document.UpdateDraft(
                    request.AccountId, request.Subject, ToLines(request.Lines), Currency(request.CurrencyCode),
                    request.IssueDate, request.ExpiryDate, request.DiscountPercent, request.OpportunityId,
                    request.Notes, request.Terms);
                await documents.SaveAsync(document, cancellationToken);
                await AuditAsync(management, context, "SalesDocumentUpdated", document.Kind.ToString(), document.Id,
                    $"{document.DocumentNumber}; total={document.Totals.Total:0.00}", cancellationToken);
                return Results.Ok(ToResponse(document));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/sales-documents/{documentId:guid}/status", async (
            Guid documentId,
            ChangeCrmSalesDocumentStatusRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmSalesDocumentStore documents,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var document = await documents.GetAsync(DemoTenantId, documentId, cancellationToken);
            if (document is null) return Results.NotFound(new ErrorResponse("Sales document not found."));
            if (!Enum.TryParse<SalesDocumentStatus>(request.Status, true, out var status))
                return Results.BadRequest(new ErrorResponse("Valid sales document status is required."));
            try
            {
                document.ChangeStatus(status);
                await documents.SaveAsync(document, cancellationToken);
                await AuditAsync(management, context, "SalesDocumentStatusChanged", document.Kind.ToString(), document.Id,
                    $"{document.DocumentNumber}; status={document.Status}", cancellationToken);
                return Results.Ok(ToResponse(document));
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
            line.Id ?? Guid.NewGuid(), line.ItemId, line.Description, line.Quantity, line.UnitPrice, line.TaxPercent)).ToArray();

    private static string Currency(string? value) => string.IsNullOrWhiteSpace(value) ? "INR" : value;

    private static CrmSalesDocumentResponse ToResponse(SalesDocument document)
    {
        var totals = document.Totals;
        return new CrmSalesDocumentResponse(
            document.Id, document.Kind.ToString(), document.DocumentNumber, document.AccountId,
            document.OpportunityId, document.Subject, document.Status.ToString(), document.CurrencyCode,
            document.IssueDate, document.ExpiryDate, document.DiscountPercent, document.Notes, document.Terms,
            document.Lines.Select(line => new CrmSalesDocumentLineResponse(
                line.Id, line.ItemId, line.Description, line.Quantity, line.UnitPrice, line.TaxPercent,
                decimal.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero))).ToArray(),
            totals.Subtotal, totals.DiscountAmount, totals.TaxAmount, totals.Total,
            document.CreatedAtUtc, document.UpdatedAtUtc);
    }

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

public sealed record CrmSalesDocumentLineRequest(
    Guid? Id,
    Guid? ItemId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxPercent);

public sealed record CreateCrmSalesDocumentRequest(
    string Kind,
    Guid AccountId,
    Guid? OpportunityId,
    string Subject,
    string? CurrencyCode,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    decimal DiscountPercent,
    string? Notes,
    string? Terms,
    IReadOnlyList<CrmSalesDocumentLineRequest> Lines);

public sealed record UpdateCrmSalesDocumentRequest(
    Guid AccountId,
    Guid? OpportunityId,
    string Subject,
    string? CurrencyCode,
    DateOnly IssueDate,
    DateOnly? ExpiryDate,
    decimal DiscountPercent,
    string? Notes,
    string? Terms,
    IReadOnlyList<CrmSalesDocumentLineRequest> Lines);

public sealed record ChangeCrmSalesDocumentStatusRequest(string Status);

public sealed record CrmSalesDocumentLineResponse(
    Guid Id,
    Guid? ItemId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TaxPercent,
    decimal Subtotal);

public sealed record CrmSalesDocumentResponse(
    Guid Id,
    string Kind,
    string DocumentNumber,
    Guid AccountId,
    Guid? OpportunityId,
    string Subject,
    string Status,
    string CurrencyCode,
    DateOnly IssueDate,
    DateOnly? ExpiryDate,
    decimal DiscountPercent,
    string? Notes,
    string? Terms,
    IReadOnlyList<CrmSalesDocumentLineResponse> Lines,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal Total,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
