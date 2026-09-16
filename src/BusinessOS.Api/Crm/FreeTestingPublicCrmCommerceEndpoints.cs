using BusinessOS.Api.Commerce;
using BusinessOS.Crm;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmCommerceEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmCommerceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapPost("/opportunities/{opportunityId:guid}/checkout", async (
            Guid opportunityId,
            CrmOpportunityCheckoutRequest request,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmOpportunityStore opportunities,
            ICrmWorkRepository work,
            RazorpayCheckoutService checkout,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var opportunity = await opportunities.GetAsync(DemoTenantId, opportunityId, ct);
            if (opportunity is null) return Results.NotFound(new ErrorResponse("Opportunity not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) && opportunity.OwnerUserId != member.Id)
                return Results.Json(new ErrorResponse("Opportunity is outside your CRM scope."), statusCode: StatusCodes.Status403Forbidden);
            if (opportunity.Stage is OpportunityStage.Won or OpportunityStage.Lost)
                return Results.BadRequest(new ErrorResponse("Closed opportunity cannot create a new checkout."));

            var amount = request.Amount.HasValue && request.Amount.Value > 0
                ? request.Amount.Value
                : opportunity.Forecast.EstimatedValue;
            if (amount <= 0) return Results.BadRequest(new ErrorResponse("A positive checkout amount is required."));
            if (string.IsNullOrWhiteSpace(request.ProductCode))
                return Results.BadRequest(new ErrorResponse("Product code is required."));

            try
            {
                var response = await checkout.CreateInitialAsync(
                    DemoTenantId,
                    new CreateInitialCheckoutOrderRequest(
                        opportunity.OrganisationId,
                        request.ProductCode.Trim(),
                        request.PlanId,
                        request.PlanVersionId,
                        request.PlanVersionNumber <= 0 ? 1 : request.PlanVersionNumber,
                        amount,
                        string.IsNullOrWhiteSpace(request.CurrencyCode) ? opportunity.Forecast.CurrencyCode : request.CurrencyCode,
                        request.TermMonths <= 0 ? 12 : request.TermMonths,
                        Math.Max(1, request.DesktopDeviceLimit),
                        Math.Max(1, request.LocationLimit),
                        Math.Max(0, request.WebAdminSeats),
                        Math.Max(0, request.FieldStaffSeats),
                        request.MultiLocationCloud),
                    ct);

                if (opportunity.Stage is OpportunityStage.Discovery or OpportunityStage.SolutionFit)
                {
                    opportunity.MoveTo(OpportunityStage.Proposal);
                    await opportunities.SaveAsync(opportunity, ct);
                }

                if (opportunity.OriginatingLeadId.HasValue)
                {
                    await work.AddActivityAsync(new LeadActivity(
                        Guid.NewGuid(), DemoTenantId, opportunity.OriginatingLeadId.Value,
                        CrmActivityType.StatusChanged,
                        "BusinessOS checkout created",
                        $"Opportunity: {opportunity.Title}; Order: {response.CommerceOrderId}; Razorpay: {response.RazorpayOrderId}",
                        member.Id), ct);
                }

                return Results.Ok(new CrmOpportunityCheckoutResponse(
                    opportunity.Id,
                    opportunity.OrganisationId,
                    opportunity.Stage.ToString(),
                    response));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapGet("/opportunities/{opportunityId:guid}/businessos", async (
            Guid opportunityId,
            IConfiguration configuration,
            IHostEnvironment environment,
            HttpContext context,
            ICrmOpportunityStore opportunities,
            ICommerceActivationStore commerce,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var opportunity = await opportunities.GetAsync(DemoTenantId, opportunityId, ct);
            if (opportunity is null) return Results.NotFound(new ErrorResponse("Opportunity not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) && opportunity.OwnerUserId != member.Id)
                return Results.Json(new ErrorResponse("Opportunity is outside your CRM scope."), statusCode: StatusCodes.Status403Forbidden);

            var snapshot = await commerce.GetAdminSnapshotAsync(DemoTenantId, 500, ct);
            var orders = snapshot.Orders.Where(x => x.OrganisationId == opportunity.OrganisationId).ToArray();
            var activations = snapshot.Activations.Where(x => x.OrganisationId == opportunity.OrganisationId).ToArray();
            var subscriptions = activations.Select(x => x.SubscriptionId).ToHashSet();
            var renewals = snapshot.Renewals.Where(x => subscriptions.Contains(x.SubscriptionId)).ToArray();
            return Results.Ok(new CrmOpportunityBusinessOsResponse(
                opportunity.Id,
                opportunity.OrganisationId,
                orders,
                activations,
                renewals));
        });

        return app;
    }

    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(
            configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);

    private static IResult Disabled() =>
        Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
}

public sealed record CrmOpportunityCheckoutRequest(
    string ProductCode,
    Guid? PlanId,
    Guid? PlanVersionId,
    int PlanVersionNumber,
    decimal? Amount,
    string? CurrencyCode,
    int TermMonths,
    int DesktopDeviceLimit,
    int LocationLimit,
    int WebAdminSeats,
    int FieldStaffSeats,
    bool MultiLocationCloud);

public sealed record CrmOpportunityCheckoutResponse(
    Guid OpportunityId,
    Guid AccountId,
    string OpportunityStage,
    RazorpayCheckoutOrderResponse Checkout);

public sealed record CrmOpportunityBusinessOsResponse(
    Guid OpportunityId,
    Guid AccountId,
    IReadOnlyList<CommerceAdminOrderSnapshot> Orders,
    IReadOnlyList<ActivationResponse> Activations,
    IReadOnlyList<RenewalResponse> Renewals);
