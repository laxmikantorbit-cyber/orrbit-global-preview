using BusinessOS.Api.Commerce;
using BusinessOS.Crm;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmOpportunityMaintenanceEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmOpportunityMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapPost("/opportunities/{opportunityId:guid}/update", async (
            Guid opportunityId,
            UpdateCrmOpportunityRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmOpportunityStore opportunities,
            ICrmTeamRepository team,
            ICrmManagementStore management,
            CancellationToken cancellationToken) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await opportunities.GetAsync(DemoTenantId, opportunityId, cancellationToken);
            if (item is null) return Results.NotFound(new ErrorResponse("Opportunity not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) && item.OwnerUserId != member.Id)
                return Results.Json(new ErrorResponse("Opportunity is outside your CRM scope."), statusCode: StatusCodes.Status403Forbidden);

            var ownerUserId = request.OwnerUserId;
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
            {
                if (ownerUserId.HasValue && ownerUserId.Value != member.Id)
                    return Results.Json(new ErrorResponse("You can only own your opportunities."), statusCode: StatusCodes.Status403Forbidden);
                ownerUserId = member.Id;
            }
            if (ownerUserId.HasValue)
            {
                var owner = await team.GetAsync(DemoTenantId, ownerUserId.Value, cancellationToken);
                if (owner is null || !owner.Active)
                    return Results.BadRequest(new ErrorResponse("Opportunity owner must be an active CRM user."));
            }

            try
            {
                var previous = $"title={item.Title}; value={item.Forecast.EstimatedValue}; probability={item.Forecast.ProbabilityPercent}; close={item.Forecast.ExpectedCloseDate}; owner={item.OwnerUserId}";
                item.UpdateDetails(
                    request.Title,
                    new OpportunityForecast(request.EstimatedValue,
                        string.IsNullOrWhiteSpace(request.CurrencyCode) ? "INR" : request.CurrencyCode,
                        request.ProbabilityPercent,
                        request.ExpectedCloseDate),
                    ownerUserId);
                await opportunities.SaveAsync(item, cancellationToken);
                var current = $"title={item.Title}; value={item.Forecast.EstimatedValue}; probability={item.Forecast.ProbabilityPercent}; close={item.Forecast.ExpectedCloseDate}; owner={item.OwnerUserId}";
                await management.AddAuditAsync(new CrmAuditEntry(
                    Guid.NewGuid(), DemoTenantId, member.Id, "OpportunityUpdated", "Opportunity",
                    item.Id.ToString(), $"Before[{previous}] After[{current}]", DateTimeOffset.UtcNow), cancellationToken);
                return Results.Ok(new CrmOpportunityResponse(
                    item.Id, item.OrganisationId, item.OriginatingLeadId, item.Title, item.Stage.ToString(),
                    item.Forecast.EstimatedValue, item.Forecast.CurrencyCode, item.Forecast.ProbabilityPercent,
                    item.Forecast.ExpectedCloseDate, item.OwnerUserId, item.LossReason));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
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

public sealed record UpdateCrmOpportunityRequest(
    string Title,
    decimal EstimatedValue,
    string CurrencyCode,
    int ProbabilityPercent,
    DateOnly? ExpectedCloseDate,
    Guid? OwnerUserId);
