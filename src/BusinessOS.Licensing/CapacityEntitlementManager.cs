namespace BusinessOS.Licensing;

public sealed record LocationAssignment(string LocationId, bool Active);

public sealed class CapacityEntitlementManager
{
    private readonly List<LocationAssignment> _locations = [];
    private readonly List<WebSeatAssignment> _webSeats = [];

    public CapacityEntitlementManager(EntitlementSnapshot entitlements)
    {
        Validate(entitlements);
        Entitlements = entitlements;
    }

    public EntitlementSnapshot Entitlements { get; private set; }
    public IReadOnlyList<LocationAssignment> Locations => _locations.AsReadOnly();
    public IReadOnlyList<WebSeatAssignment> WebSeats => _webSeats.AsReadOnly();

    public void RegisterLocation(string locationId)
    {
        if (string.IsNullOrWhiteSpace(locationId))
            throw new ArgumentException("Location id is required.");

        if (_locations.Any(x => x.Active && x.LocationId == locationId)) return;

        var activeCount = _locations.Count(x => x.Active);
        if (activeCount >= Entitlements.Locations)
            throw new InvalidOperationException("No location entitlement is available.");
        if (activeCount >= 1 && !Entitlements.MultiLocationCloud)
            throw new InvalidOperationException("Multi-location cloud entitlement is required.");

        _locations.Add(new LocationAssignment(locationId, true));
    }

    public void AssignWebSeat(string userId, WebSeatKind kind)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.");

        if (_webSeats.Any(x => x.Active && x.UserId == userId && x.Kind == kind)) return;

        var limit = kind == WebSeatKind.Admin
            ? Entitlements.WebAdminSeats
            : Entitlements.FieldStaffSeats;
        var used = _webSeats.Count(x => x.Active && x.Kind == kind);

        if (used >= limit)
            throw new InvalidOperationException($"No {kind} web seat entitlement is available.");

        _webSeats.Add(new WebSeatAssignment(userId, kind, true));
    }

    public void ReleaseWebSeat(string userId, WebSeatKind kind)
    {
        var index = _webSeats.FindLastIndex(x => x.Active && x.UserId == userId && x.Kind == kind);
        if (index < 0) return;

        _webSeats[index] = _webSeats[index] with { Active = false };
    }

    public void ReleaseLocation(string locationId)
    {
        var index = _locations.FindLastIndex(x => x.Active && x.LocationId == locationId);
        if (index < 0) return;

        _locations[index] = _locations[index] with { Active = false };
    }

    public void AddLocationCapacity(int quantity)
    {
        EnsurePositive(quantity);
        Entitlements = Entitlements with { Locations = Entitlements.Locations + quantity };
    }

    public void AddWebAdminSeats(int quantity)
    {
        EnsurePositive(quantity);
        Entitlements = Entitlements with { WebAdminSeats = Entitlements.WebAdminSeats + quantity };
    }

    public void AddFieldStaffSeats(int quantity)
    {
        EnsurePositive(quantity);
        Entitlements = Entitlements with { FieldStaffSeats = Entitlements.FieldStaffSeats + quantity };
    }

    public void SetMultiLocationCloud(bool enabled) =>
        Entitlements = Entitlements with { MultiLocationCloud = enabled };

    private static void Validate(EntitlementSnapshot entitlements)
    {
        if (entitlements.DesktopSystems < 1)
            throw new ArgumentOutOfRangeException(nameof(entitlements));
        if (entitlements.Locations < 1)
            throw new ArgumentOutOfRangeException(nameof(entitlements));
        if (entitlements.WebAdminSeats < 0 || entitlements.FieldStaffSeats < 0)
            throw new ArgumentOutOfRangeException(nameof(entitlements));
    }

    private static void EnsurePositive(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
    }
}
