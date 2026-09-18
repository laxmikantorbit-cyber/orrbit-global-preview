using BusinessOS.Api.Commerce;
using BusinessOS.Api.Tenancy;

namespace BusinessOS.Api.SoftwareDelivery;

public static class SoftwareDeliveryEndpoints
{
    public static IEndpointRouteBuilder MapSoftwareDeliveryEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/software/subscriptions/{subscriptionId:guid}/delivery",
            GetDeliveryAsync);

        var admin = app.MapGroup("/api/software/admin/releases");
        admin.MapGet("", ListReleasesAsync);
        admin.MapPost("", PublishReleaseAsync);
        admin.MapPost("/{releaseId:guid}/deactivate", DeactivateReleaseAsync);
        return app;
    }

    private static async Task<IResult> GetDeliveryAsync(
        Guid subscriptionId,
        string? channel,
        string? platform,
        string? architecture,
        TenantContext tenant,
        ICommerceActivationStore commerce,
        ISoftwareReleaseStore releases,
        CancellationToken cancellationToken)
    {
        var state = await commerce.FindSubscriptionStateAsync(
            tenant.TenantId, subscriptionId, cancellationToken);
        if (state is null)
            return Results.NotFound(new ErrorResponse(
                "Subscription was not found for this tenant."));
        var status = EntitlementStatusEvaluator.Evaluate(
            state, DateOnly.FromDateTime(DateTime.UtcNow));
        var selectedChannel = Clean(channel) ?? "Stable";
        var selectedPlatform = Clean(platform) ?? "Windows";
        var selectedArchitecture = Clean(architecture) ?? "x64";

        var allowedByStatus = status.Status is "Active" or "Grace";
        var hasDesktop = state.Entitlements.DesktopSystems > 0;
        var release = await releases.FindLatestActiveAsync(
            tenant.TenantId,
            state.ProductCode,
            selectedChannel,
            selectedPlatform,
            selectedArchitecture,
            cancellationToken);

        var reason = ResolveUnavailableReason(
            status.Status,
            allowedByStatus,
            hasDesktop,
            release,
            selectedChannel,
            selectedPlatform,
            selectedArchitecture);

        var downloadEntitled = reason is null;
        var activationEntitled = allowedByStatus && hasDesktop;
        LicenseActivationCodeResponse? activation = null;
        if (activationEntitled)
            activation = await commerce.GetOrCreateDesktopActivationCodeAsync(
                tenant.TenantId, subscriptionId, cancellationToken);

        return Results.Ok(new SoftwareDeliveryResponse(
            tenant.TenantId,
            state.OrganisationId,
            state.SubscriptionId,
            state.LicenseId,
            state.ProductCode,
            status.Status,
            state.ValidUntil,
            downloadEntitled,
            reason,
            downloadEntitled ? release : null,
            downloadEntitled ? release!.DownloadUrl : null,
            activationEntitled ? activation?.ActivationCode : null));
    }
    private static async Task<IResult> ListReleasesAsync(
        string? productCode,
        int? take,
        TenantContext tenant,
        ISoftwareReleaseStore releases,
        CancellationToken cancellationToken)
    {
        if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden)
            return forbidden;
        var items = await releases.ListAsync(
            tenant.TenantId, productCode, take ?? 50, cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> PublishReleaseAsync(
        SoftwareReleaseCreateRequest request,
        TenantContext tenant,
        ISoftwareReleaseStore releases,
        CancellationToken cancellationToken)
    {
        if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden)
            return forbidden;
        try
        {
            var item = await releases.AddAsync(
                tenant.TenantId, request, cancellationToken);
            return Results.Created(
                $"/api/software/admin/releases/{item.Id}",
                item);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new ErrorResponse(ex.Message));
        }
    }
    private static async Task<IResult> DeactivateReleaseAsync(
        Guid releaseId,
        TenantContext tenant,
        ISoftwareReleaseStore releases,
        CancellationToken cancellationToken)
    {
        if (TenantRoleAuthorization.ForbidUnlessCommerceAdmin(tenant) is { } forbidden)
            return forbidden;
        return await releases.DeactivateAsync(
            tenant.TenantId, releaseId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound(new ErrorResponse("Software release was not found."));
    }

    private static string? ResolveUnavailableReason(
        string subscriptionStatus,
        bool allowedByStatus,
        bool hasDesktop,
        SoftwareReleaseRecord? release,
        string channel,
        string platform,
        string architecture)
    {
        if (!allowedByStatus)
            return subscriptionStatus switch
            {
                "NotStarted" => "Subscription has not started yet.",
                "Expired" => "Subscription must be renewed before downloading software.",
                _ => $"Subscription status {subscriptionStatus} does not allow downloads."
            };

        if (!hasDesktop)
            return "This plan does not include a desktop software entitlement.";

        if (release is null)
            return $"No active {channel} {platform} {architecture} release has been published yet.";

        return null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
