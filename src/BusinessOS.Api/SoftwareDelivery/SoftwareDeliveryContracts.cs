namespace BusinessOS.Api.SoftwareDelivery;

public sealed record SoftwareReleaseCreateRequest(
    string ProductCode,
    string Version,
    string Channel,
    string Platform,
    string Architecture,
    string FileName,
    string DownloadUrl,
    string Sha256,
    long? SizeBytes,
    string? ReleaseNotes,
    DateTimeOffset? PublishedAtUtc);

public sealed record SoftwareReleaseRecord(
    Guid Id,
    Guid TenantId,
    string ProductCode,
    string Version,
    string Channel,
    string Platform,
    string Architecture,
    string FileName,
    string DownloadUrl,
    string Sha256,
    long? SizeBytes,
    string? ReleaseNotes,
    DateTimeOffset PublishedAtUtc,
    bool Active);
public sealed record SoftwareDeliveryResponse(
    Guid TenantId,
    Guid OrganisationId,
    Guid SubscriptionId,
    Guid LicenseId,
    string ProductCode,
    string SubscriptionStatus,
    DateOnly ValidUntil,
    bool DownloadEntitled,
    string? UnavailableReason,
    SoftwareReleaseRecord? Release,
    string? DownloadUrl,
    string? ActivationCode);

public interface ISoftwareReleaseStore
{
    Task<SoftwareReleaseRecord> AddAsync(
        Guid tenantId,
        SoftwareReleaseCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<SoftwareReleaseRecord?> FindLatestActiveAsync(
        Guid tenantId,
        string productCode,
        string channel,
        string platform,
        string architecture,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SoftwareReleaseRecord>> ListAsync(
        Guid tenantId,
        string? productCode,
        int take,
        CancellationToken cancellationToken = default);

    Task<bool> DeactivateAsync(
        Guid tenantId,
        Guid releaseId,
        CancellationToken cancellationToken = default);
}
public sealed class InMemorySoftwareReleaseStore : ISoftwareReleaseStore
{
    private readonly List<SoftwareReleaseRecord> _items = [];
    private readonly object _gate = new();

    public Task<SoftwareReleaseRecord> AddAsync(
        Guid tenantId,
        SoftwareReleaseCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = SoftwareReleaseValidator.Normalize(tenantId, request);
        lock (_gate)
        {
            if (_items.Any(x => x.TenantId == tenantId &&
                x.ProductCode == normalized.ProductCode &&
                x.Version == normalized.Version &&
                x.Channel == normalized.Channel &&
                x.Platform == normalized.Platform &&
                x.Architecture == normalized.Architecture))
                throw new InvalidOperationException("This software release already exists.");
            _items.Add(normalized);
        }
        return Task.FromResult(normalized);
    }
    public Task<SoftwareReleaseRecord?> FindLatestActiveAsync(
        Guid tenantId,
        string productCode,
        string channel,
        string platform,
        string architecture,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_items
                .Where(x => x.TenantId == tenantId && x.Active)
                .Where(x => string.Equals(x.ProductCode, productCode, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Channel, channel, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Platform, platform, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Architecture, architecture, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.PublishedAtUtc)
                .ThenByDescending(x => x.Version, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault());
        }
    }

    public Task<IReadOnlyList<SoftwareReleaseRecord>> ListAsync(
        Guid tenantId,
        string? productCode,
        int take,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        take = Math.Clamp(take, 1, 200);
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<SoftwareReleaseRecord>>(
                _items.Where(x => x.TenantId == tenantId)
                    .Where(x => string.IsNullOrWhiteSpace(productCode) ||
                        string.Equals(x.ProductCode, productCode, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.PublishedAtUtc)
                    .Take(take)
                    .ToArray());
        }
    }

    public Task<bool> DeactivateAsync(
        Guid tenantId,
        Guid releaseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var index = _items.FindIndex(x => x.TenantId == tenantId && x.Id == releaseId);
            if (index < 0) return Task.FromResult(false);
            _items[index] = _items[index] with { Active = false };
            return Task.FromResult(true);
        }
    }
}
public static class SoftwareReleaseValidator
{
    private static readonly string[] SupportedChannels = ["Stable", "Beta", "Internal"];
    private static readonly string[] SupportedPlatforms = ["Windows"];
    private static readonly string[] SupportedArchitectures = ["x64", "x86", "arm64"];
    private static readonly string[] SupportedInstallerExtensions = [".exe", ".msi", ".msix", ".zip"];

    public static SoftwareReleaseRecord Normalize(
        Guid tenantId,
        SoftwareReleaseCreateRequest request)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.");
        ArgumentNullException.ThrowIfNull(request);
        var product = NormalizeProductCode(request.ProductCode);
        var version = NormalizeVersion(request.Version);
        var channel = NormalizeChoice(request.Channel, SupportedChannels, "Channel");
        var platform = NormalizeChoice(request.Platform, SupportedPlatforms, "Platform");
        var architecture = NormalizeChoice(request.Architecture, SupportedArchitectures, "Architecture");
        var fileName = NormalizeFileName(request.FileName);
        var sha = Required(request.Sha256, "SHA-256").ToLowerInvariant();
        if (sha.Length != 64 || sha.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.");
        if (!Uri.TryCreate(request.DownloadUrl?.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Download URL must be an absolute HTTPS URL.");
        if (request.SizeBytes is < 0)
            throw new ArgumentException("Size bytes cannot be negative.");

        return new SoftwareReleaseRecord(
            Guid.NewGuid(), tenantId, product, version, channel, platform,
            architecture, fileName, uri.ToString(), sha, request.SizeBytes,
            NormalizeReleaseNotes(request.ReleaseNotes),
            request.PublishedAtUtc ?? DateTimeOffset.UtcNow, true);
    }

    private static string NormalizeProductCode(string? value)
    {
        var normalized = Required(value, "Product code").ToUpperInvariant();
        if (normalized.Length > 64 || normalized.Any(c => !(char.IsUpper(c) || char.IsDigit(c) || c is '_' or '-')))
            throw new ArgumentException("Product code can contain only A-Z, 0-9, underscore or hyphen.");
        return normalized;
    }

    private static string NormalizeVersion(string? value)
    {
        var version = Required(value, "Version");
        if (version.Length > 40 || version.Any(char.IsWhiteSpace) ||
            !version.Any(char.IsDigit) || version.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '-' or '+')))
            throw new ArgumentException("Version must be a safe semantic-style version string.");
        return version;
    }

    private static string NormalizeChoice(string? value, string[] allowed, string field)
    {
        var supplied = Required(value, field);
        var match = allowed.FirstOrDefault(x => string.Equals(x, supplied, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new ArgumentException($"{field} must be one of: {string.Join(", ", allowed)}.");
    }

    private static string NormalizeFileName(string? value)
    {
        var fileName = Required(value, "File name");
        if (fileName.Length > 160 || fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains("..") ||
            fileName.Any(c => char.IsControl(c) || c is '<' or '>' or ':' or '"' or '|' or '?' or '*'))
            throw new ArgumentException("Installer file name contains unsafe characters.");
        var extension = Path.GetExtension(fileName);
        if (!SupportedInstallerExtensions.Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Installer file must be .exe, .msi, .msix or .zip.");
        return fileName;
    }

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{field} is required.")
            : value.Trim();

    private static string? NormalizeReleaseNotes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var notes = value.Trim();
        if (notes.Length > 4000)
            throw new ArgumentException("Release notes must be 4000 characters or fewer.");
        return notes;
    }
}
