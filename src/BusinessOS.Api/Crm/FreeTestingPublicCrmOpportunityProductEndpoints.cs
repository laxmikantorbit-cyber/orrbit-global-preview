using BusinessOS.Api.Commerce;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmOpportunityProductEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmOpportunityProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/opportunities/detailed", async (
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmOpportunityStore opportunities,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var items = await opportunities.ListAsync(DemoTenantId, ct);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
                items = items.Where(x => x.OwnerUserId == member.Id).ToArray();
            return Results.Ok(new
            {
                opportunities = items.Select(x => new CrmOpportunityDetailedResponse(
                    x.Id, x.OrganisationId, x.OriginatingLeadId, x.Title, x.ProductService,
                    x.Stage.ToString(), x.Forecast.EstimatedValue, x.Forecast.CurrencyCode,
                    x.Forecast.ProbabilityPercent, x.Forecast.ExpectedCloseDate, x.OwnerUserId,
                    x.LossReason)).OrderByDescending(x => x.EstimatedValue).ToArray()
            });
        });

        group.MapPost("/opportunities/{opportunityId:guid}/product-service", async (
            Guid opportunityId,
            UpdateOpportunityProductServiceRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmOpportunityStore opportunities,
            ICrmManagementStore management,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await opportunities.GetAsync(DemoTenantId, opportunityId, ct);
            if (item is null) return Results.NotFound(new ErrorResponse("Opportunity not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) && item.OwnerUserId != member.Id)
                return Results.Json(new ErrorResponse("Opportunity is outside your CRM scope."), statusCode: StatusCodes.Status403Forbidden);
            try
            {
                var previous = item.ProductService;
                item.SetProductService(request.ProductService);
                await opportunities.SaveAsync(item, ct);
                await management.AddAuditAsync(new CrmAuditEntry(
                    Guid.NewGuid(), DemoTenantId, member.Id, "OpportunityProductUpdated", "Opportunity",
                    item.Id.ToString(), $"Before={previous ?? "-"}; After={item.ProductService ?? "-"}", DateTimeOffset.UtcNow), ct);
                return Results.Ok(new { item.Id, item.ProductService });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        return app;
    }

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
}

public sealed record UpdateOpportunityProductServiceRequest(string? ProductService);
public sealed record CrmOpportunityDetailedResponse(
    Guid Id,
    Guid AccountId,
    Guid? OriginatingLeadId,
    string Title,
    string? ProductService,
    string Stage,
    decimal EstimatedValue,
    string CurrencyCode,
    int ProbabilityPercent,
    DateOnly? ExpectedCloseDate,
    Guid? OwnerUserId,
    string? LossReason);
