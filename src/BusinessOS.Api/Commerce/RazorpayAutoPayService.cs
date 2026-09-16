using BusinessOS.Api.Payments;
using BusinessOS.Commerce;

namespace BusinessOS.Api.Commerce;

public sealed class RazorpayAutoPayService
{
    private const string Provider = "razorpay";
    private readonly ICommerceActivationStore _store;
    private readonly IRazorpaySubscriptionClient _razorpay;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public RazorpayAutoPayService(
        ICommerceActivationStore store,
        IRazorpaySubscriptionClient razorpay,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _store = store;
        _razorpay = razorpay;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task<AutoPaySetupResponse?> SetupAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var state = await _store.FindSubscriptionStateAsync(
            tenantId, subscriptionId, cancellationToken);
        if (state is null) return null;
        if (state.SubscriptionStatus == SubscriptionStatus.Cancelled)
            throw new InvalidOperationException(
                "AutoPay cannot be enabled after cancellation at period end.");
        var existing = await _store.FindProviderSubscriptionAsync(
            tenantId, subscriptionId, Provider, cancellationToken);
        if (existing is not null)
            return ToResponse(existing, existingBinding: true);

        var planId = ResolvePlanId(state.ProductCode);
        var totalCount = ResolveTotalCount();
        var startAtUnix = ResolveStartAtUnix(state.ValidUntil);
        var request = new RazorpaySubscriptionRequest(
            planId,
            totalCount,
            1,
            true,
            startAtUnix,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tenantId"] = tenantId.ToString(),
                ["subscriptionId"] = subscriptionId.ToString(),
                ["productCode"] = state.ProductCode
            });

        var provider = await _razorpay.CreateSubscriptionAsync(
            request, cancellationToken);
        var binding = await _store.RecordProviderSubscriptionAsync(
            new ProviderSubscriptionBinding(
                tenantId,
                subscriptionId,
                Provider,
                provider.Id,
                provider.PlanId,
                provider.StartAtUnix,
                provider.TotalCount,
                provider.Status,
                true,
                false,
                provider.ShortUrl,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToResponse(binding, existingBinding: false);
    }

    public async Task<ProviderSubscriptionBinding?> CancelAsync(
        Guid tenantId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var binding = await _store.FindProviderSubscriptionAsync(
            tenantId, subscriptionId, Provider, cancellationToken);
        if (binding is null) return null;
        if (binding.CancelAtPeriodEnd) return binding;

        var terminal = binding.Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase) ||
            binding.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
            binding.Status.Equals("expired", StringComparison.OrdinalIgnoreCase);
        var providerStatus = binding.Status;
        if (!terminal)
        {
            var cancelled = await _razorpay.CancelSubscriptionAsync(
                binding.ProviderSubscriptionId,
                cancelAtCycleEnd: false,
                cancellationToken);
            providerStatus = cancelled.Status;
        }

        return await _store.UpdateProviderSubscriptionStateAsync(
            Provider,
            binding.ProviderSubscriptionId,
            providerStatus,
            autoRenewEnabled: false,
            cancelAtPeriodEnd: true,
            cancellationToken);
    }

    private AutoPaySetupResponse ToResponse(
        ProviderSubscriptionBinding binding,
        bool existingBinding) => new(
            binding.TenantId,
            binding.SubscriptionId,
            binding.Provider,
            binding.ProviderSubscriptionId,
            binding.ProviderPlanId,
            binding.Status,
            ProviderPublicKeyId(),
            binding.AuthorizationUrl,
            binding.StartAtUnix,
            binding.TotalCount,
            existingBinding);

    private string ResolvePlanId(string productCode)
    {
        var specific = _configuration[$"Payments:RazorpayAutoPay:PlanIds:{productCode}"];
        if (!string.IsNullOrWhiteSpace(specific)) return specific.Trim();
        var fallback = _configuration["Payments:RazorpaySubscriptionPlanId"];
        if (!string.IsNullOrWhiteSpace(fallback)) return fallback.Trim();
        if (IsFreeTestingMode()) return "plan_free_test_annual";
        throw new InvalidOperationException(
            $"Razorpay AutoPay plan id is not configured for product '{productCode}'.");
    }

    private int ResolveTotalCount()
    {
        var configured = _configuration["Payments:RazorpayAutoPay:TotalCount"];
        if (int.TryParse(configured, out var count) && count > 0) return count;
        if (IsFreeTestingMode()) return 12;
        throw new InvalidOperationException(
            "Razorpay AutoPay total billing cycle count is not configured.");
    }
    private long ResolveStartAtUnix(DateOnly validUntil)
    {
        var renewalStart = new DateTimeOffset(
            validUntil.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var minimum = DateTimeOffset.UtcNow.AddMinutes(15);
        return (renewalStart > minimum ? renewalStart : minimum).ToUnixTimeSeconds();
    }

    private string ProviderPublicKeyId()
    {
        var configured = _configuration["Payments:RazorpayKeyId"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        if (IsFreeTestingMode()) return "rzp_test_free_testing";
        throw new InvalidOperationException("Razorpay key id is not configured.");
    }

    private bool IsFreeTestingMode() =>
        !_environment.IsProduction() &&
        string.Equals(
            _configuration["BusinessOS:Payments:Mode"],
            "RazorpayTestPending",
            StringComparison.OrdinalIgnoreCase);
}
