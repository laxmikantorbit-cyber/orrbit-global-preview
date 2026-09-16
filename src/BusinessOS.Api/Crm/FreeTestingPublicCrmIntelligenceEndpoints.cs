using BusinessOS.Api.Commerce;
using BusinessOS.Crm;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmIntelligenceEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Engine = "businessos-crm-intelligence-rules-v1";

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmIntelligenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm/ai");

        group.MapGet("/leads/{leadId:guid}", async (
            Guid leadId, IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ILeadRepository leads, ICrmWorkRepository work, CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var lead = await leads.GetAsync(DemoTenantId, leadId, ct);
            if (lead is null) return Results.NotFound(new ErrorResponse("Lead not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanAccessLead(member, lead)) return Forbidden("Lead is outside your CRM scope.");
            var followUps = await work.ListFollowUpsAsync(DemoTenantId, lead.Id, ct);
            return Results.Ok(LeadInsight(lead, followUps));
        });

        group.MapGet("/opportunities/{opportunityId:guid}", async (
            Guid opportunityId, IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ILeadRepository leads, ICrmOpportunityStore opportunities, CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await opportunities.GetAsync(DemoTenantId, opportunityId, ct);
            if (item is null) return Results.NotFound(new ErrorResponse("Opportunity not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) && item.OwnerUserId != member.Id)
                return Forbidden("Opportunity is outside your CRM scope.");
            Lead? lead = item.OriginatingLeadId.HasValue ? await leads.GetAsync(DemoTenantId, item.OriginatingLeadId.Value, ct) : null;
            return Results.Ok(OpportunityInsight(item, lead));
        });

        group.MapGet("/daily-brief", async (
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ILeadRepository leads, ICrmWorkRepository work, ICrmOpportunityStore opportunities,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var leadItems = await leads.ListAsync(DemoTenantId, ct);
            var followUps = await work.ListFollowUpsAsync(DemoTenantId, null, ct);
            var tasks = await work.ListTasksAsync(DemoTenantId, null, ct);
            var opps = await opportunities.ListAsync(DemoTenantId, ct);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
            {
                leadItems = leadItems.Where(x => x.Attribution.AccountOwnerUserId == member.Id).ToArray();
                followUps = followUps.Where(x => x.OwnerUserId == member.Id).ToArray();
                tasks = tasks.Where(x => x.AssigneeUserId == member.Id).ToArray();
                opps = opps.Where(x => x.OwnerUserId == member.Id).ToArray();
            }
            var openLeads = leadItems.Where(x => x.Status is not (LeadStatus.Converted or LeadStatus.Unqualified)).ToArray();
            var leadInsights = new List<CrmAiLeadInsight>();
            foreach (var lead in openLeads)
                leadInsights.Add(LeadInsight(lead, followUps.Where(x => x.LeadId == lead.Id).ToArray()));
            var leadMap = leadItems.ToDictionary(x => x.Id);
            var dealInsights = opps.Where(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost))
                .Select(x => OpportunityInsight(x, x.OriginatingLeadId.HasValue && leadMap.TryGetValue(x.OriginatingLeadId.Value, out var lead) ? lead : null))
                .ToArray();
            var now = DateTimeOffset.UtcNow;
            var topLeads = leadInsights.OrderByDescending(x => x.PriorityScore).Take(5).ToArray();
            var riskyDeals = dealInsights.OrderByDescending(x => x.RiskScore).Take(5).ToArray();
            var topActions = topLeads.Select(x => new CrmAiAction("Lead", x.LeadId, x.Title, x.NextBestAction, x.PriorityScore))
                .Concat(riskyDeals.Where(x => x.RiskScore >= 30).Select(x => new CrmAiAction("Opportunity", x.OpportunityId, x.Title, x.NextBestAction, x.RiskScore)))
                .OrderByDescending(x => x.Score).Take(7).ToArray();
            return Results.Ok(new CrmAiDailyBrief(
                Engine, DateTimeOffset.UtcNow,
                openLeads.Length,
                followUps.Count(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc < now),
                tasks.Count(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc.HasValue && x.DueAtUtc.Value < now),
                dealInsights.Length,
                dealInsights.Sum(x => x.WeightedValue),
                topLeads, riskyDeals, topActions));
        });

        group.MapGet("/ask", async (
            string? q, IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ILeadRepository leads, ICrmWorkRepository work, ICrmAccountStore accounts,
            ICrmOpportunityStore opportunities, CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest(new ErrorResponse("CRM question is required."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var leadItems = await leads.ListAsync(DemoTenantId, ct);
            var followUps = await work.ListFollowUpsAsync(DemoTenantId, null, ct);
            var tasks = await work.ListTasksAsync(DemoTenantId, null, ct);
            var accountItems = await accounts.ListAsync(DemoTenantId, ct);
            var opps = await opportunities.ListAsync(DemoTenantId, ct);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
            {
                leadItems = leadItems.Where(x => x.Attribution.AccountOwnerUserId == member.Id).ToArray();
                followUps = followUps.Where(x => x.OwnerUserId == member.Id).ToArray();
                tasks = tasks.Where(x => x.AssigneeUserId == member.Id).ToArray();
                opps = opps.Where(x => x.OwnerUserId == member.Id).ToArray();
                var accountIds = opps.Select(x => x.OrganisationId).Concat(leadItems.Select(x => x.OrganisationId)).ToHashSet();
                accountItems = accountItems.Where(x => accountIds.Contains(x.Id)).ToArray();
            }
            var answer = Answer(q.Trim(), leadItems, followUps, tasks, accountItems, opps);
            return Results.Ok(answer);
        });

        return app;
    }

    private static CrmAiLeadInsight LeadInsight(Lead lead, IReadOnlyList<LeadFollowUp> followUps)
    {
        var now = DateTimeOffset.UtcNow;
        var reasons = new List<string>();
        var score = lead.Priority switch { LeadPriority.Urgent => 30, LeadPriority.High => 20, LeadPriority.Normal => 10, _ => 5 };
        reasons.Add($"Configured priority: {lead.Priority}");
        if (lead.Status == LeadStatus.Qualified) { score += 25; reasons.Add("Lead is qualified and sales-ready."); }
        else if (lead.Status == LeadStatus.New) { score += 15; reasons.Add("New lead still needs first engagement."); }
        else if (lead.Status == LeadStatus.Contacted) { score += 10; reasons.Add("Conversation has started but lead is not qualified yet."); }
        if (!string.IsNullOrWhiteSpace(lead.ProductInterest)) { score += 5; reasons.Add("Product/service interest is known."); }
        if (!string.IsNullOrWhiteSpace(lead.MobileNumber) || !string.IsNullOrWhiteSpace(lead.Email)) score += 5;
        var staleDays = Math.Max(0, (int)Math.Floor((now - lead.UpdatedAtUtc).TotalDays));
        if (staleDays >= 7) { score += 20; reasons.Add($"Lead has been stale for {staleDays} days."); }
        else if (staleDays >= 3) { score += 10; reasons.Add($"No meaningful update for {staleDays} days."); }
        var overdue = followUps.Where(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc < now).OrderBy(x => x.DueAtUtc).FirstOrDefault();
        var next = followUps.Where(x => x.Status == CrmWorkStatus.Open).OrderBy(x => x.DueAtUtc).FirstOrDefault();
        if (overdue is not null) { score += 25; reasons.Add("An open follow-up is overdue."); }
        score = Math.Clamp(score, 0, 100);
        var action = overdue is not null ? $"Complete overdue {overdue.Channel} follow-up now: {overdue.Purpose}"
            : lead.Status == LeadStatus.New ? "Make first contact and capture outcome."
            : lead.Status == LeadStatus.Contacted && next is null ? "Schedule the next follow-up with a clear purpose."
            : lead.Status == LeadStatus.Qualified ? "Advance to opportunity/quotation and confirm expected close date."
            : next is not null ? $"Prepare for {next.Channel} follow-up due {next.DueAtUtc:yyyy-MM-dd HH:mm} UTC."
            : "Review the lead and define the next measurable sales action.";
        var band = score >= 75 ? "Critical" : score >= 55 ? "High" : score >= 30 ? "Medium" : "Low";
        return new CrmAiLeadInsight(Engine, lead.Id, lead.Title, score, band, staleDays >= 3, staleDays, action, reasons);
    }

    private static CrmAiOpportunityInsight OpportunityInsight(Opportunity item, Lead? lead)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var reasons = new List<string>(); var risk = 0;
        if (item.Forecast.ProbabilityPercent < 30) { risk += 25; reasons.Add("Win probability is below 30%."); }
        else if (item.Forecast.ProbabilityPercent < 50) { risk += 15; reasons.Add("Win probability is below 50%."); }
        if (!item.Forecast.ExpectedCloseDate.HasValue) { risk += 15; reasons.Add("Expected close date is missing."); }
        else if (item.Forecast.ExpectedCloseDate.Value < today) { risk += 30; reasons.Add("Expected close date has already passed."); }
        else if (item.Forecast.ExpectedCloseDate.Value <= today.AddDays(7)) { risk += 10; reasons.Add("Expected close date is within 7 days."); }
        if (string.IsNullOrWhiteSpace(item.ProductService)) { risk += 15; reasons.Add("Product/service is not set on the deal."); }
        if (item.Stage == OpportunityStage.Discovery) { risk += 10; reasons.Add("Deal is still in Discovery stage."); }
        if (lead is not null)
        {
            var staleDays = Math.Max(0, (int)Math.Floor((DateTimeOffset.UtcNow - lead.UpdatedAtUtc).TotalDays));
            if (staleDays >= 7) { risk += 15; reasons.Add($"Originating lead has been stale for {staleDays} days."); }
        }
        risk = Math.Clamp(risk, 0, 100);
        var action = item.Forecast.ExpectedCloseDate is null ? "Set a realistic expected close date and confirm the decision process."
            : item.Forecast.ExpectedCloseDate.Value < today ? "Revalidate close date, customer intent and next commitment today."
            : string.IsNullOrWhiteSpace(item.ProductService) ? "Confirm and attach the exact product/service to this opportunity."
            : item.Forecast.ProbabilityPercent < 50 ? "Identify the main objection and secure the next customer commitment."
            : item.Stage == OpportunityStage.Discovery ? "Move discovery into solution-fit with confirmed requirements."
            : "Keep the next milestone scheduled and update probability after customer feedback.";
        var band = risk >= 70 ? "Critical" : risk >= 45 ? "High" : risk >= 25 ? "Medium" : "Low";
        return new CrmAiOpportunityInsight(Engine, item.Id, item.Title, item.ProductService, risk, band,
            item.Forecast.EstimatedValue * item.Forecast.ProbabilityPercent / 100m, action, reasons);
    }

    private static CrmAiAskResponse Answer(
        string question, IReadOnlyList<Lead> leads, IReadOnlyList<LeadFollowUp> followUps,
        IReadOnlyList<CrmTask> tasks, IReadOnlyList<BusinessOS.Customers.Organisation> accounts,
        IReadOnlyList<Opportunity> opportunities)
    {
        var q = question.ToLowerInvariant(); var now = DateTimeOffset.UtcNow; var today = DateOnly.FromDateTime(now.UtcDateTime);
        var hits = new List<CrmAiAskHit>(); string intent; string answer;
        if (q.Contains("overdue") && q.Contains("follow"))
        {
            intent = "overdue_followups";
            var items = followUps.Where(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc < now).OrderBy(x => x.DueAtUtc).Take(25).ToArray();
            hits.AddRange(items.Select(x => new CrmAiAskHit("FollowUp", x.Id, x.Purpose, "Overdue", x.DueAtUtc.ToString("O"))));
            answer = $"{items.Length} overdue follow-up(s) found in your current CRM scope.";
        }
        else if (q.Contains("overdue") && q.Contains("task"))
        {
            intent = "overdue_tasks";
            var items = tasks.Where(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc.HasValue && x.DueAtUtc.Value < now).OrderBy(x => x.DueAtUtc).Take(25).ToArray();
            hits.AddRange(items.Select(x => new CrmAiAskHit("Task", x.Id, x.Title, "Overdue", x.DueAtUtc?.ToString("O"))));
            answer = $"{items.Length} overdue task(s) found.";
        }
        else if (q.Contains("qualified") && q.Contains("lead"))
        {
            intent = "qualified_leads"; var items = leads.Where(x => x.Status == LeadStatus.Qualified).OrderByDescending(x => x.Priority).Take(25).ToArray();
            hits.AddRange(items.Select(x => new CrmAiAskHit("Lead", x.Id, x.Title, x.Priority.ToString(), x.ProductInterest)));
            answer = $"{items.Length} qualified lead(s) are ready for sales action.";
        }
        else if ((q.Contains("high priority") || q.Contains("urgent")) && q.Contains("lead"))
        {
            intent = "priority_leads"; var items = leads.Where(x => x.Priority is LeadPriority.High or LeadPriority.Urgent && x.Status is not (LeadStatus.Converted or LeadStatus.Unqualified)).Take(25).ToArray();
            hits.AddRange(items.Select(x => new CrmAiAskHit("Lead", x.Id, x.Title, x.Priority.ToString(), x.ProductInterest)));
            answer = $"{items.Length} high/urgent open lead(s) found.";
        }
        else if (q.Contains("stale") && q.Contains("lead"))
        {
            intent = "stale_leads"; var items = leads.Where(x => x.Status is not (LeadStatus.Converted or LeadStatus.Unqualified) && (now-x.UpdatedAtUtc).TotalDays>=3).OrderBy(x=>x.UpdatedAtUtc).Take(25).ToArray();
            hits.AddRange(items.Select(x => new CrmAiAskHit("Lead",x.Id,x.Title,$"{(int)(now-x.UpdatedAtUtc).TotalDays} days stale",x.Status.ToString()))); answer=$"{items.Length} stale open lead(s) need review.";
        }
        else if (q.Contains("closing") || q.Contains("close this month"))
        {
            intent = "closing_opportunities"; var monthEnd=new DateOnly(today.Year,today.Month,DateTime.DaysInMonth(today.Year,today.Month));var items=opportunities.Where(x=>x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost)&&x.Forecast.ExpectedCloseDate.HasValue&&x.Forecast.ExpectedCloseDate.Value>=today&&x.Forecast.ExpectedCloseDate.Value<=monthEnd).OrderBy(x=>x.Forecast.ExpectedCloseDate).Take(25).ToArray();
            hits.AddRange(items.Select(x=>new CrmAiAskHit("Opportunity",x.Id,x.Title,$"{x.Forecast.ProbabilityPercent}%",x.Forecast.ExpectedCloseDate?.ToString())));answer=$"{items.Length} open opportunity/deal(s) are expected to close this month.";
        }
        else
        {
            intent="global_search";var needle=q.Trim();
            hits.AddRange(leads.Where(x=>Contains(needle,x.Title,x.ContactName,x.MobileNumber,x.Email,x.ProductInterest)).Take(15).Select(x=>new CrmAiAskHit("Lead",x.Id,x.Title,x.Status.ToString(),x.ProductInterest)));
            hits.AddRange(accounts.Where(x=>Contains(needle,x.Name,x.LegalName,x.Gstin,x.PrimaryContact?.Name,x.PrimaryContact?.Email,x.PrimaryContact?.Phone)).Take(15).Select(x=>new CrmAiAskHit("Account",x.Id,x.Name,x.Status.ToString(),x.PrimaryContact?.Name)));
            hits.AddRange(opportunities.Where(x=>Contains(needle,x.Title,x.ProductService,x.Stage.ToString())).Take(15).Select(x=>new CrmAiAskHit("Opportunity",x.Id,x.Title,x.Stage.ToString(),x.ProductService)));
            answer=$"{hits.Count} matching CRM record(s) found for '{question}'.";
        }
        return new CrmAiAskResponse(Engine, question, intent, answer, hits.Take(30).ToArray());
    }

    private static bool Contains(string needle, params string?[] values) => values.Any(x => !string.IsNullOrWhiteSpace(x) && x.Contains(needle, StringComparison.OrdinalIgnoreCase));
    private static IResult Forbidden(string error) => Results.Json(new ErrorResponse(error), statusCode: StatusCodes.Status403Forbidden);
    private static bool Enabled(IConfiguration c,IHostEnvironment e)=>!e.IsProduction()&&string.Equals(c["BusinessOS:DeploymentMode"],"FreeTesting",StringComparison.OrdinalIgnoreCase);
    private static IResult Disabled()=>Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
}

public sealed record CrmAiLeadInsight(string Engine,Guid LeadId,string Title,int PriorityScore,string PriorityBand,bool IsStale,int StaleDays,string NextBestAction,IReadOnlyList<string> Reasons);
public sealed record CrmAiOpportunityInsight(string Engine,Guid OpportunityId,string Title,string? ProductService,int RiskScore,string RiskBand,decimal WeightedValue,string NextBestAction,IReadOnlyList<string> Reasons);
public sealed record CrmAiAction(string Type,Guid Id,string Title,string Action,int Score);
public sealed record CrmAiDailyBrief(string Engine,DateTimeOffset GeneratedAtUtc,int OpenLeads,int OverdueFollowUps,int OverdueTasks,int OpenOpportunities,decimal WeightedPipelineValue,IReadOnlyList<CrmAiLeadInsight> TopLeadPriorities,IReadOnlyList<CrmAiOpportunityInsight> AtRiskOpportunities,IReadOnlyList<CrmAiAction> TopActions);
public sealed record CrmAiAskHit(string Type,Guid Id,string Title,string Status,string? Detail);
public sealed record CrmAiAskResponse(string Engine,string Question,string Intent,string Answer,IReadOnlyList<CrmAiAskHit> Results);
