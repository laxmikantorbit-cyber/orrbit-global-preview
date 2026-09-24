using BusinessOS.Api.Commerce;
using BusinessOS.Crm;
using BusinessOS.Customers;
using BusinessOS.Sales;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmAdvancedEndpoints
{
    private static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmAdvancedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/testing/public/crm");

        group.MapGet("/duplicates", async (
            string? mobile, string? email, string? business,
            IConfiguration configuration, IHostEnvironment environment,
            ILeadRepository leads, ICrmAccountStore accounts,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var leadItems = await leads.ListAsync(DemoTenantId, ct);
            var accountItems = await accounts.ListAsync(DemoTenantId, ct);
            var normalizedMobile = Digits(mobile);
            var normalizedEmail = Clean(email)?.ToLowerInvariant();
            var normalizedBusiness = Clean(business)?.ToLowerInvariant();

            var leadHits = leadItems.Where(x =>
                    (!string.IsNullOrEmpty(normalizedMobile) && Digits(x.MobileNumber) == normalizedMobile) ||
                    (!string.IsNullOrEmpty(normalizedEmail) && x.Email?.Trim().ToLowerInvariant() == normalizedEmail) ||
                    (!string.IsNullOrEmpty(normalizedBusiness) && x.Title.Trim().ToLowerInvariant() == normalizedBusiness))
                .Select(x => new CrmDuplicateHit("Lead", x.Id, x.Title, x.MobileNumber, x.Email))
                .ToArray();

            var accountHits = accountItems.Where(x =>
                    (!string.IsNullOrEmpty(normalizedBusiness) && x.Name.Trim().ToLowerInvariant() == normalizedBusiness) ||
                    x.Contacts.Any(c =>
                        (!string.IsNullOrEmpty(normalizedMobile) && Digits(c.Phone) == normalizedMobile) ||
                        (!string.IsNullOrEmpty(normalizedEmail) && c.Email?.Trim().ToLowerInvariant() == normalizedEmail)))
                .Select(x => new CrmDuplicateHit("Account", x.Id, x.Name,
                    x.PrimaryContact?.Phone, x.PrimaryContact?.Email))
                .ToArray();

            return Results.Ok(new CrmDuplicateCheckResponse(leadHits.Length + accountHits.Length > 0,
                leadHits.Concat(accountHits).ToArray()));
        });

        group.MapPost("/leads/{leadId:guid}/tags", async (
            Guid leadId, CrmTagRequest request,
            IConfiguration configuration, IHostEnvironment environment,
            HttpContext context, ILeadRepository leads, ICrmWorkRepository work,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var lead = await leads.GetAsync(DemoTenantId, leadId, ct);
            if (lead is null) return Results.NotFound(new ErrorResponse("Lead not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanAccessLead(member, lead)) return Forbidden("Lead is outside your CRM scope.");
            try
            {
                if (request.Remove) lead.RemoveTag(request.Tag); else lead.AddTag(request.Tag);
                await leads.SaveAsync(lead, ct);
                await work.AddActivityAsync(new LeadActivity(Guid.NewGuid(), DemoTenantId, lead.Id,
                    CrmActivityType.ProfileUpdated, request.Remove ? "Lead tag removed" : "Lead tag added", request.Tag, member.Id), ct);
                return Results.Ok(new { leadId = lead.Id, tags = lead.Tags.OrderBy(x => x).ToArray() });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/leads/bulk", async (
            CrmBulkLeadRequest request,
            IConfiguration configuration, IHostEnvironment environment,
            HttpContext context, ILeadRepository leads, ICrmWorkRepository work, ICrmTeamRepository team,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            if (request.LeadIds is null || request.LeadIds.Count == 0)
                return Results.BadRequest(new ErrorResponse("At least one lead id is required."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            CrmTeamMember? newOwner = null;
            if (request.ChangeOwner && !CrmRolePolicy.Allows(member.Role, CrmPermission.AssignLead))
                return Forbidden("Lead assignment permission is required.");
            if (request.ChangeOwner && request.OwnerUserId.HasValue)
            {
                newOwner = await team.GetAsync(DemoTenantId, request.OwnerUserId.Value, ct);
                if (newOwner is null || !newOwner.Active) return Results.BadRequest(new ErrorResponse("Assigned CRM user must be active."));
            }

            LeadPriority? priority = null;
            if (!string.IsNullOrWhiteSpace(request.Priority))
            {
                if (!Enum.TryParse<LeadPriority>(request.Priority, true, out var parsedPriority))
                    return Results.BadRequest(new ErrorResponse("Valid priority is required."));
                priority = parsedPriority;
            }

            LeadStatus? status = null;
            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse<LeadStatus>(request.Status, true, out var parsedStatus) || parsedStatus == LeadStatus.Converted)
                    return Results.BadRequest(new ErrorResponse("Bulk status must be New, Contacted, Qualified or Unqualified."));
                status = parsedStatus;
            }

            var updated = new List<Guid>();
            var failed = new List<CrmBulkFailure>();
            foreach (var id in request.LeadIds.Distinct())
            {
                var lead = await leads.GetAsync(DemoTenantId, id, ct);
                if (lead is null) { failed.Add(new(id, "Lead not found.")); continue; }
                if (!CrmFreeTestingAccessMiddleware.CanAccessLead(member, lead)) { failed.Add(new(id, "Lead is outside your CRM scope.")); continue; }
                try
                {
                    if (status.HasValue) ApplyBulkStatus(lead, status.Value, request.Reason);
                    if (priority.HasValue) lead.SetPriority(priority.Value);
                    if (request.ChangeOwner) lead.AssignOwner(request.OwnerUserId);
                    await leads.SaveAsync(lead, ct);
                    await work.AddActivityAsync(new LeadActivity(Guid.NewGuid(), DemoTenantId, lead.Id,
                        CrmActivityType.ProfileUpdated, "Bulk lead update", request.Note, member.Id), ct);
                    updated.Add(id);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    failed.Add(new(id, ex.Message));
                }
            }
            return Results.Ok(new CrmBulkLeadResponse(updated, failed));
        });

        group.MapPost("/follow-ups/{followUpId:guid}/reschedule", async (
            Guid followUpId, CrmRescheduleFollowUpRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ILeadRepository leads, ICrmWorkRepository work, ICrmTeamRepository team,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await work.GetFollowUpAsync(DemoTenantId, followUpId, ct);
            if (item is null) return Results.NotFound(new ErrorResponse("Follow-up not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) && item.OwnerUserId != member.Id) return Forbidden("Follow-up is outside your CRM scope.");
            var ownerId = request.OwnerUserId ?? item.OwnerUserId;
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member)) ownerId = member.Id;
            if (ownerId.HasValue)
            {
                var owner = await team.GetAsync(DemoTenantId, ownerId.Value, ct);
                if (owner is null || !owner.Active) return Results.BadRequest(new ErrorResponse("Follow-up owner must be active."));
            }
            if (!Enum.TryParse<FollowUpChannel>(request.Channel, true, out var channel))
                return Results.BadRequest(new ErrorResponse("Valid follow-up channel is required."));
            try
            {
                item.Reschedule(request.DueAtUtc, channel, request.Purpose, ownerId);
                await work.SaveFollowUpAsync(item, ct);
                await RefreshNextFollowUpAsync(item.LeadId, leads, work, ct);
                await work.AddActivityAsync(new LeadActivity(Guid.NewGuid(), DemoTenantId, item.LeadId,
                    CrmActivityType.FollowUpScheduled, "Follow-up rescheduled", request.Purpose, member.Id), ct);
                return Results.Ok(ToFollowUp(item));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/follow-ups/{followUpId:guid}/cancel", async (
            Guid followUpId, CrmCancelRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ILeadRepository leads, ICrmWorkRepository work, CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await work.GetFollowUpAsync(DemoTenantId, followUpId, ct);
            if (item is null) return Results.NotFound(new ErrorResponse("Follow-up not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) && item.OwnerUserId != member.Id) return Forbidden("Follow-up is outside your CRM scope.");
            try
            {
                item.Cancel(request.Reason);
                await work.SaveFollowUpAsync(item, ct);
                await RefreshNextFollowUpAsync(item.LeadId, leads, work, ct);
                await work.AddActivityAsync(new LeadActivity(Guid.NewGuid(), DemoTenantId, item.LeadId,
                    CrmActivityType.FollowUpCompleted, "Follow-up cancelled", request.Reason, member.Id), ct);
                return Results.Ok(ToFollowUp(item));
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new ErrorResponse(ex.Message)); }
        });

        group.MapPost("/tasks/{taskId:guid}/update", async (
            Guid taskId, CrmUpdateTaskRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmWorkRepository work, ICrmTeamRepository team, CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await work.GetTaskAsync(DemoTenantId, taskId, ct);
            if (item is null) return Results.NotFound(new ErrorResponse("Task not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) && item.AssigneeUserId != member.Id) return Forbidden("Task is outside your CRM scope.");
            var assignee = request.AssigneeUserId;
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member)) assignee = member.Id;
            if (assignee.HasValue)
            {
                var user = await team.GetAsync(DemoTenantId, assignee.Value, ct);
                if (user is null || !user.Active) return Results.BadRequest(new ErrorResponse("Task assignee must be active."));
            }
            if (!Enum.TryParse<LeadPriority>(request.Priority, true, out var priority))
                return Results.BadRequest(new ErrorResponse("Valid task priority is required."));
            try
            {
                item.Update(request.Title, request.Details, request.DueAtUtc, priority, assignee);
                await work.SaveTaskAsync(item, ct);
                return Results.Ok(ToTask(item));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        group.MapPost("/tasks/{taskId:guid}/cancel", async (
            Guid taskId, IConfiguration configuration, IHostEnvironment environment,
            HttpContext context, ICrmWorkRepository work, CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var item = await work.GetTaskAsync(DemoTenantId, taskId, ct);
            if (item is null) return Results.NotFound(new ErrorResponse("Task not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member) && item.AssigneeUserId != member.Id) return Forbidden("Task is outside your CRM scope.");
            try
            {
                item.Cancel();
                await work.SaveTaskAsync(item, ct);
                return Results.Ok(ToTask(item));
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new ErrorResponse(ex.Message)); }
        });

        group.MapGet("/global-search", async (
            string? q, IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ILeadRepository leads, ICrmAccountStore accounts, ICrmOpportunityStore opportunities,
            ICrmEstimateRequestStore estimateRequests, ICrmKnowledgeStore knowledge, ICrmMediaStore media,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var query = Clean(q);
            if (string.IsNullOrWhiteSpace(query)) return Results.Ok(new CrmGlobalSearchResponse([]));
            var needle = query.ToLowerInvariant();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var leadItems = await leads.ListAsync(DemoTenantId, ct);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
                leadItems = leadItems.Where(x => x.Attribution.AccountOwnerUserId == member.Id).ToArray();
            var accountItems = await accounts.ListAsync(DemoTenantId, ct);
            var opportunityItems = await opportunities.ListAsync(DemoTenantId, ct);
            var estimateItems = await estimateRequests.ListAsync(DemoTenantId, ct);
            var knowledgeItems = await knowledge.ListArticlesAsync(DemoTenantId, ct);
            var mediaItems = await media.ListAsync(DemoTenantId, ct);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
            {
                opportunityItems = opportunityItems.Where(x => x.OwnerUserId == member.Id).ToArray();
                estimateItems = estimateItems.Where(x => x.AssignedUserId == member.Id).ToArray();
            }
            if (member.Role is not (CrmRoleCode.Owner or CrmRoleCode.Admin))
                knowledgeItems = knowledgeItems.Where(x => x.Visibility == CrmKnowledgeVisibility.Team || x.OwnerUserId == member.Id).ToArray();

            var hits = new List<CrmGlobalSearchHit>();
            hits.AddRange(leadItems.Where(x => Match(needle, x.Title, x.ContactName, x.MobileNumber, x.Email, x.ProductInterest))
                .Select(x => new CrmGlobalSearchHit("Lead", x.Id, x.Title, x.Status.ToString(), x.ContactName, x.MobileNumber)));
            hits.AddRange(accountItems.Where(x => Match(needle, x.Name, x.LegalName, x.Gstin, x.PrimaryContact?.Name, x.PrimaryContact?.Email, x.PrimaryContact?.Phone))
                .Select(x => new CrmGlobalSearchHit("Account", x.Id, x.Name, x.Status.ToString(), x.PrimaryContact?.Name, x.PrimaryContact?.Phone)));
            hits.AddRange(opportunityItems.Where(x => Match(needle, x.Title, x.Stage.ToString(), x.Forecast.CurrencyCode))
                .Select(x => new CrmGlobalSearchHit("Opportunity", x.Id, x.Title, x.Stage.ToString(), $"{x.Forecast.CurrencyCode} {x.Forecast.EstimatedValue:0.##}", null)));
            hits.AddRange(estimateItems.Where(x => Match(needle, x.Source, x.Requirement, x.ContactName, x.MobileNumber, x.Email, x.BusinessCompany, x.Notes))
                .Select(x => new CrmGlobalSearchHit("EstimateRequest", x.Id, x.BusinessCompany ?? x.ContactName ?? x.Email ?? x.MobileNumber ?? "Estimate request",
                    x.Status.ToString(), x.Requirement, x.Source)));
            hits.AddRange(knowledgeItems.Where(x => Match(needle, x.Title, x.Content, x.Status.ToString()))
                .Select(x => new CrmGlobalSearchHit("KnowledgeArticle", x.Id, x.Title, x.Status.ToString(),
                    x.Visibility.ToString(), null)));
            hits.AddRange(mediaItems.Where(x => x.Active && Match(needle, x.FileName, x.Purpose, x.EntityType, x.MimeType))
                .Select(x => new CrmGlobalSearchHit("MediaAsset", x.Id, x.FileName, "Active", x.Purpose, x.EntityType)));
            return Results.Ok(new CrmGlobalSearchResponse(hits.Take(50).ToArray()));
        });

        group.MapGet("/reports/summary", async (
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ILeadRepository leads, ICrmWorkRepository work, ICrmOpportunityStore opportunities,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var leadItems = await leads.ListAsync(DemoTenantId, ct);
            var followUps = await work.ListFollowUpsAsync(DemoTenantId, null, ct);
            var tasks = await work.ListTasksAsync(DemoTenantId, null, ct);
            var opportunityItems = await opportunities.ListAsync(DemoTenantId, ct);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
            {
                leadItems = leadItems.Where(x => x.Attribution.AccountOwnerUserId == member.Id).ToArray();
                followUps = followUps.Where(x => x.OwnerUserId == member.Id).ToArray();
                tasks = tasks.Where(x => x.AssigneeUserId == member.Id).ToArray();
                opportunityItems = opportunityItems.Where(x => x.OwnerUserId == member.Id).ToArray();
            }
            var now = DateTimeOffset.UtcNow;
            var converted = leadItems.Count(x => x.Status == LeadStatus.Converted);
            var openOpps = opportunityItems.Where(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost)).ToArray();
            var leadSources = leadItems.GroupBy(x => x.Attribution.LeadSource ?? "Unknown")
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
            var stages = opportunityItems.GroupBy(x => x.Stage.ToString()).ToDictionary(x => x.Key, x => x.Count());
            var lostReasons = opportunityItems.Where(x => x.Stage == OpportunityStage.Lost)
                .GroupBy(x => x.LossReason ?? "Unknown").ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
            return Results.Ok(new CrmReportSummaryResponse(
                leadItems.Count,
                converted,
                leadItems.Count == 0 ? 0m : Math.Round(converted * 100m / leadItems.Count, 2),
                openOpps.Sum(x => x.Forecast.EstimatedValue),
                openOpps.Sum(x => x.Forecast.EstimatedValue * x.Forecast.ProbabilityPercent / 100m),
                opportunityItems.Where(x => x.Stage == OpportunityStage.Won).Sum(x => x.Forecast.EstimatedValue),
                opportunityItems.Count(x => x.Stage == OpportunityStage.Lost),
                followUps.Count(x => x.Status == CrmWorkStatus.Open),
                followUps.Count(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc < now),
                tasks.Count(x => x.Status == CrmWorkStatus.Open),
                tasks.Count(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc.HasValue && x.DueAtUtc.Value < now),
                leadSources, stages, lostReasons));
        });

        group.MapGet("/notifications", async (
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmWorkRepository work, ICrmOpportunityStore opportunities, CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var followUps = await work.ListFollowUpsAsync(DemoTenantId, null, ct);
            var tasks = await work.ListTasksAsync(DemoTenantId, null, ct);
            var opps = await opportunities.ListAsync(DemoTenantId, ct);
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member))
            {
                followUps = followUps.Where(x => x.OwnerUserId == member.Id).ToArray();
                tasks = tasks.Where(x => x.AssigneeUserId == member.Id).ToArray();
                opps = opps.Where(x => x.OwnerUserId == member.Id).ToArray();
            }
            var now = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var nextWeek = today.AddDays(7);
            var items = new List<CrmNotificationItem>();
            items.AddRange(followUps.Where(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc < now)
                .Select(x => new CrmNotificationItem("FollowUpOverdue", "Overdue follow-up", x.Purpose, x.LeadId, x.Id, x.DueAtUtc, "High")));
            items.AddRange(followUps.Where(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc >= now && x.DueAtUtc <= now.AddDays(1))
                .Select(x => new CrmNotificationItem("FollowUpDue", "Follow-up due", x.Purpose, x.LeadId, x.Id, x.DueAtUtc, "Normal")));
            items.AddRange(tasks.Where(x => x.Status == CrmWorkStatus.Open && x.DueAtUtc.HasValue && x.DueAtUtc.Value < now)
                .Select(x => new CrmNotificationItem("TaskOverdue", "Overdue task", x.Title, x.LeadId, x.Id, x.DueAtUtc, "High")));
            items.AddRange(opps.Where(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost) && x.Forecast.ExpectedCloseDate.HasValue && x.Forecast.ExpectedCloseDate.Value >= today && x.Forecast.ExpectedCloseDate.Value <= nextWeek)
                .Select(x => new CrmNotificationItem("OpportunityClosing", "Opportunity closing soon", x.Title, x.OriginatingLeadId, x.Id,
                    new DateTimeOffset(x.Forecast.ExpectedCloseDate!.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), "Normal")));
            return Results.Ok(new CrmNotificationResponse(items.OrderBy(x => x.DueAtUtc).Take(100).ToArray()));
        });

        group.MapGet("/accounts/{accountId:guid}/customer360", async (
            Guid accountId, IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmAccountStore accounts, ICrmOpportunityStore opportunities, ICommerceActivationStore commerce,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var account = await accounts.GetAsync(DemoTenantId, accountId, ct);
            if (account is null) return Results.NotFound(new ErrorResponse("Account not found."));
            var member = CrmFreeTestingAccessMiddleware.Current(context);
            var opps = (await opportunities.ListAsync(DemoTenantId, ct)).Where(x => x.OrganisationId == accountId).ToArray();
            if (!CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member)) opps = opps.Where(x => x.OwnerUserId == member.Id).ToArray();
            var snapshot = await commerce.GetAdminSnapshotAsync(DemoTenantId, 500, ct);
            var orders = snapshot.Orders.Where(x => x.OrganisationId == accountId).ToArray();
            var activations = snapshot.Activations.Where(x => x.OrganisationId == accountId).ToArray();
            var subscriptionIds = activations.Select(x => x.SubscriptionId).ToHashSet();
            var renewals = snapshot.Renewals.Where(x => subscriptionIds.Contains(x.SubscriptionId)).ToArray();
            return Results.Ok(new CrmCustomer360Response(
                new CrmCustomer360Account(account.Id, account.Name, account.LegalName, account.Gstin, account.Status.ToString(),
                    account.PrimaryContact?.Name, account.PrimaryContact?.Email, account.PrimaryContact?.Phone, account.Contacts.Count),
                opps.Select(x => new CrmCustomer360Opportunity(x.Id, x.Title, x.Stage.ToString(), x.Forecast.EstimatedValue,
                    x.Forecast.CurrencyCode, x.Forecast.ProbabilityPercent, x.Forecast.ExpectedCloseDate)).ToArray(),
                orders,
                activations,
                renewals,
                orders.Where(x => x.PaidAtUtc.HasValue).Sum(x => x.Amount)));
        });

        group.MapPost("/team/{memberId:guid}/exit", async (
            Guid memberId, CrmEmployeeExitRequest request,
            IConfiguration configuration, IHostEnvironment environment, HttpContext context,
            ICrmTeamRepository team, ILeadRepository leads, ICrmWorkRepository work, ICrmOpportunityStore opportunities,
            CancellationToken ct) =>
        {
            if (!Enabled(configuration, environment)) return Disabled();
            var actor = CrmFreeTestingAccessMiddleware.Current(context);
            if (!CrmRolePolicy.Allows(actor.Role, CrmPermission.ManageTeam)) return Forbidden("Team management permission is required.");
            var exiting = await team.GetAsync(DemoTenantId, memberId, ct);
            if (exiting is null) return Results.NotFound(new ErrorResponse("CRM user not found."));
            if (exiting.Role == CrmRoleCode.Owner) return Results.BadRequest(new ErrorResponse("Owner cannot be exited through this workflow."));
            var replacement = await team.GetAsync(DemoTenantId, request.ReassignToUserId, ct);
            if (replacement is null || !replacement.Active || replacement.Id == exiting.Id)
                return Results.BadRequest(new ErrorResponse("A different active reassignment user is required."));

            var leadCount = 0; var followUpCount = 0; var taskCount = 0; var opportunityCount = 0;
            foreach (var lead in (await leads.ListAsync(DemoTenantId, ct)).Where(x => x.Attribution.AccountOwnerUserId == exiting.Id && x.Status is not (LeadStatus.Converted or LeadStatus.Unqualified)))
            {
                lead.AssignOwner(replacement.Id); await leads.SaveAsync(lead, ct); leadCount++;
            }
            foreach (var item in (await work.ListFollowUpsAsync(DemoTenantId, null, ct)).Where(x => x.OwnerUserId == exiting.Id && x.Status == CrmWorkStatus.Open))
            {
                item.Reschedule(item.DueAtUtc, item.Channel, item.Purpose, replacement.Id); await work.SaveFollowUpAsync(item, ct); followUpCount++;
            }
            foreach (var item in (await work.ListTasksAsync(DemoTenantId, null, ct)).Where(x => x.AssigneeUserId == exiting.Id && x.Status == CrmWorkStatus.Open))
            {
                item.Update(item.Title, item.Details, item.DueAtUtc, item.Priority, replacement.Id); await work.SaveTaskAsync(item, ct); taskCount++;
            }
            foreach (var item in (await opportunities.ListAsync(DemoTenantId, ct)).Where(x => x.OwnerUserId == exiting.Id && x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost)))
            {
                item.AssignOwner(replacement.Id); await opportunities.SaveAsync(item, ct); opportunityCount++;
            }
            exiting.SetActive(false); await team.SaveAsync(exiting, ct);
            return Results.Ok(new CrmEmployeeExitResponse(exiting.Id, replacement.Id, leadCount, followUpCount, taskCount, opportunityCount));
        });

        return app;
    }

    private static async Task RefreshNextFollowUpAsync(Guid leadId, ILeadRepository leads, ICrmWorkRepository work, CancellationToken ct)
    {
        var lead = await leads.GetAsync(DemoTenantId, leadId, ct);
        if (lead is null || lead.Status is LeadStatus.Converted or LeadStatus.Unqualified) return;
        var next = (await work.ListFollowUpsAsync(DemoTenantId, leadId, ct))
            .Where(x => x.Status == CrmWorkStatus.Open).OrderBy(x => x.DueAtUtc).FirstOrDefault();
        lead.ScheduleNextFollowUp(next?.DueAtUtc); await leads.SaveAsync(lead, ct);
    }

    private static void ApplyBulkStatus(Lead lead, LeadStatus status, string? reason)
    {
        if (lead.Status is LeadStatus.Converted or LeadStatus.Unqualified && status is LeadStatus.New or LeadStatus.Contacted or LeadStatus.Qualified)
        { lead.Reopen(status); return; }
        switch (status)
        {
            case LeadStatus.New:
                if (lead.Status != LeadStatus.New) throw new InvalidOperationException("Only closed leads can be reopened to New.");
                break;
            case LeadStatus.Contacted: lead.MarkContacted(); break;
            case LeadStatus.Qualified: lead.Qualify(); break;
            case LeadStatus.Unqualified: lead.MarkUnqualified(string.IsNullOrWhiteSpace(reason) ? "Bulk unqualified" : reason); break;
            default: throw new ArgumentException("Unsupported bulk status.");
        }
    }

    private static CrmAdvancedFollowUpResponse ToFollowUp(LeadFollowUp x) =>
        new(x.Id, x.LeadId, x.DueAtUtc, x.Channel.ToString(), x.Purpose, x.OwnerUserId, x.Status.ToString(), x.Outcome);
    private static CrmAdvancedTaskResponse ToTask(CrmTask x) =>
        new(x.Id, x.LeadId, x.Title, x.Details, x.DueAtUtc, x.Priority.ToString(), x.AssigneeUserId, x.Status.ToString());
    private static bool Match(string needle, params string?[] values) => values.Any(v => v?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true);
    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        !environment.IsProduction() && string.Equals(configuration["BusinessOS:DeploymentMode"], "FreeTesting", StringComparison.OrdinalIgnoreCase);
    private static IResult Disabled() => Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
    private static IResult Forbidden(string message) => Results.Json(new ErrorResponse(message), statusCode: StatusCodes.Status403Forbidden);
}

public sealed record CrmDuplicateHit(string Type, Guid Id, string Name, string? Mobile, string? Email);
public sealed record CrmDuplicateCheckResponse(bool HasDuplicates, IReadOnlyList<CrmDuplicateHit> Matches);
public sealed record CrmTagRequest(string Tag, bool Remove = false);
public sealed record CrmBulkLeadRequest(IReadOnlyList<Guid> LeadIds, string? Status, string? Priority, bool ChangeOwner, Guid? OwnerUserId, string? Reason, string? Note);
public sealed record CrmBulkFailure(Guid LeadId, string Error);
public sealed record CrmBulkLeadResponse(IReadOnlyList<Guid> UpdatedLeadIds, IReadOnlyList<CrmBulkFailure> Failed);
public sealed record CrmRescheduleFollowUpRequest(DateTimeOffset DueAtUtc, string Channel, string Purpose, Guid? OwnerUserId);
public sealed record CrmCancelRequest(string? Reason);
public sealed record CrmUpdateTaskRequest(string Title, string? Details, DateTimeOffset? DueAtUtc, string Priority, Guid? AssigneeUserId);
public sealed record CrmAdvancedFollowUpResponse(Guid Id, Guid LeadId, DateTimeOffset DueAtUtc, string Channel, string Purpose, Guid? OwnerUserId, string Status, string? Outcome);
public sealed record CrmAdvancedTaskResponse(Guid Id, Guid? LeadId, string Title, string? Details, DateTimeOffset? DueAtUtc, string Priority, Guid? AssigneeUserId, string Status);
public sealed record CrmGlobalSearchHit(string Type, Guid Id, string Title, string Status, string? Subtitle, string? Secondary);
public sealed record CrmGlobalSearchResponse(IReadOnlyList<CrmGlobalSearchHit> Results);
public sealed record CrmReportSummaryResponse(int TotalLeads, int ConvertedLeads, decimal ConversionPercent, decimal OpenPipelineValue, decimal WeightedPipelineValue, decimal WonValue, int LostDeals, int OpenFollowUps, int OverdueFollowUps, int OpenTasks, int OverdueTasks, IReadOnlyDictionary<string,int> LeadSources, IReadOnlyDictionary<string,int> OpportunityStages, IReadOnlyDictionary<string,int> LostReasons);
public sealed record CrmNotificationItem(string Type, string Title, string Detail, Guid? LeadId, Guid RecordId, DateTimeOffset? DueAtUtc, string Severity);
public sealed record CrmNotificationResponse(IReadOnlyList<CrmNotificationItem> Items);
public sealed record CrmCustomer360Account(Guid Id, string Name, string? LegalName, string? Gstin, string Status, string? PrimaryContactName, string? PrimaryContactEmail, string? PrimaryContactPhone, int ContactCount);
public sealed record CrmCustomer360Opportunity(Guid Id, string Title, string Stage, decimal EstimatedValue, string CurrencyCode, int ProbabilityPercent, DateOnly? ExpectedCloseDate);
public sealed record CrmCustomer360Response(CrmCustomer360Account Account, IReadOnlyList<CrmCustomer360Opportunity> Opportunities, IReadOnlyList<CommerceAdminOrderSnapshot> Orders, IReadOnlyList<ActivationResponse> Activations, IReadOnlyList<RenewalResponse> Renewals, decimal PaidOrderValue);
public sealed record CrmEmployeeExitRequest(Guid ReassignToUserId);
public sealed record CrmEmployeeExitResponse(Guid ExitedUserId, Guid ReassignedToUserId, int LeadsReassigned, int FollowUpsReassigned, int TasksReassigned, int OpportunitiesReassigned);
