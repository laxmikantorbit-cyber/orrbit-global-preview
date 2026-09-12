using BusinessOS.Licensing;

namespace BusinessOS.Licensing.Tests;

public sealed class CapacityEntitlementTests
{
    [Fact]
    public void Single_Location_Works_Without_MultiLocation_Cloud()
    {
        var manager = Create(locations: 1, multiLocation: false);
        manager.RegisterLocation("MAIN");
        Assert.Single(manager.Locations, x => x.Active);
    }

    [Fact]
    public void Second_Location_Requires_MultiLocation_Cloud()
    {
        var manager = Create(locations: 2, multiLocation: false);
        manager.RegisterLocation("MAIN");

        Assert.Throws<InvalidOperationException>(() =>
            manager.RegisterLocation("BRANCH-2"));
    }

    [Fact]
    public void MultiLocation_Allows_Additional_Location_Within_Limit()
    {
        var manager = Create(locations: 2, multiLocation: true);
        manager.RegisterLocation("MAIN");
        manager.RegisterLocation("BRANCH-2");
        Assert.Equal(2, manager.Locations.Count(x => x.Active));
    }

    [Fact]
    public void Location_Limit_Blocks_Extra_Branch()
    {
        var manager = Create(locations: 2, multiLocation: true);
        manager.RegisterLocation("MAIN");
        manager.RegisterLocation("BRANCH-2");

        Assert.Throws<InvalidOperationException>(() =>
            manager.RegisterLocation("BRANCH-3"));
    }

    [Fact]
    public void Admin_Seat_Limit_Is_Enforced()
    {
        var manager = Create(adminSeats: 1);
        manager.AssignWebSeat("ADMIN-1", WebSeatKind.Admin);

        Assert.Throws<InvalidOperationException>(() =>
            manager.AssignWebSeat("ADMIN-2", WebSeatKind.Admin));
    }

    [Fact]
    public void Field_Seat_Limit_Is_Enforced()
    {
        var manager = Create(fieldSeats: 1);
        manager.AssignWebSeat("TECH-1", WebSeatKind.FieldStaff);

        Assert.Throws<InvalidOperationException>(() =>
            manager.AssignWebSeat("TECH-2", WebSeatKind.FieldStaff));
    }

    [Fact]
    public void Admin_And_Field_Seats_Use_Separate_Capacity()
    {
        var manager = Create(adminSeats: 1, fieldSeats: 1);
        manager.AssignWebSeat("USER-A", WebSeatKind.Admin);
        manager.AssignWebSeat("USER-B", WebSeatKind.FieldStaff);

        Assert.Equal(2, manager.WebSeats.Count(x => x.Active));
    }

    [Fact]
    public void Released_Web_Seat_Can_Be_Reused()
    {
        var manager = Create(fieldSeats: 1);
        manager.AssignWebSeat("TECH-1", WebSeatKind.FieldStaff);
        manager.ReleaseWebSeat("TECH-1", WebSeatKind.FieldStaff);
        manager.AssignWebSeat("TECH-2", WebSeatKind.FieldStaff);

        Assert.Single(manager.WebSeats, x => x.Active);
        Assert.Contains(manager.WebSeats, x => x.Active && x.UserId == "TECH-2");
    }

    [Fact]
    public void Purchased_AddOn_Capacity_Allows_More_Usage()
    {
        var manager = Create(locations: 1, adminSeats: 1, fieldSeats: 1, multiLocation: true);
        manager.AddLocationCapacity(1);
        manager.AddWebAdminSeats(1);
        manager.AddFieldStaffSeats(2);

        Assert.Equal(2, manager.Entitlements.Locations);
        Assert.Equal(2, manager.Entitlements.WebAdminSeats);
        Assert.Equal(3, manager.Entitlements.FieldStaffSeats);
    }

    [Fact]
    public void Released_Location_Can_Be_Replaced()
    {
        var manager = Create(locations: 1);
        manager.RegisterLocation("OLD");
        manager.ReleaseLocation("OLD");
        manager.RegisterLocation("NEW");

        Assert.Single(manager.Locations, x => x.Active);
        Assert.Contains(manager.Locations, x => x.Active && x.LocationId == "NEW");
    }

    private static CapacityEntitlementManager Create(
        int locations = 1,
        int adminSeats = 0,
        int fieldSeats = 0,
        bool multiLocation = false)
    {
        return new CapacityEntitlementManager(new EntitlementSnapshot(
            DesktopSystems: 1,
            Locations: locations,
            WebAdminSeats: adminSeats,
            FieldStaffSeats: fieldSeats,
            MultiLocationCloud: multiLocation));
    }
}
