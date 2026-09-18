using BusinessOS.Api.Commerce;
using BusinessOS.Application;
using BusinessOS.Licensing;

namespace BusinessOS.Api.Tests;

public sealed class DesktopDeviceManagementTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrganisationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Device_Limit_Is_Enforced_And_Revoke_Frees_Seat()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(), signer);
        var activation = await store.ActivateInitialPurchaseAsync(TenantId, InitialRequest("pay_device_limit"));

        var first = await store.ActivateDesktopDeviceAsync(TenantId, activation.SubscriptionId,
            new DesktopDeviceActivationRequest("device-a", "Front Desk", "1.0.0"));
        Assert.NotNull(first);
        Assert.True(first!.Allowed);
        Assert.Equal(1, first.ActiveDesktopDevices);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ActivateDesktopDeviceAsync(TenantId, activation.SubscriptionId,
                new DesktopDeviceActivationRequest("device-b", "Workshop", "1.0.0")));

        Assert.True(await store.RevokeDesktopDeviceAsync(TenantId, activation.SubscriptionId, "device-a"));
        var second = await store.ActivateDesktopDeviceAsync(TenantId, activation.SubscriptionId,
            new DesktopDeviceActivationRequest("device-b", "Workshop", "1.0.1"));
        Assert.NotNull(second);
        Assert.Equal(1, second!.ActiveDesktopDevices);
    }

    [Fact]
    public async Task Replace_Device_Deactivates_Old_And_Activates_New()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(), signer);
        var activation = await store.ActivateInitialPurchaseAsync(TenantId, InitialRequest("pay_device_replace"));
        await store.ActivateDesktopDeviceAsync(TenantId, activation.SubscriptionId,
            new DesktopDeviceActivationRequest("device-old", "Old PC", "1.0.0"));

        var replacement = await store.ReplaceDesktopDeviceAsync(TenantId, activation.SubscriptionId,
            new DesktopDeviceReplaceRequest("device-old", "device-new", "New PC", "1.1.0"));

        Assert.NotNull(replacement);
        Assert.True(replacement!.Allowed);
        Assert.Equal("device-new", replacement.DeviceFingerprint);
        Assert.Equal("device_replaced", replacement.Reason);
        var devices = await store.ListDesktopDevicesAsync(TenantId, activation.SubscriptionId);
        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, x => x.DeviceFingerprint == "device-old" && !x.Active);
        Assert.Contains(devices, x => x.DeviceFingerprint == "device-new" && x.Active && x.DeviceName == "New PC");
    }

    [Fact]
    public async Task Device_Inventory_Is_Tenant_And_Subscription_Scoped()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(), signer);
        var activation = await store.ActivateInitialPurchaseAsync(TenantId, InitialRequest("pay_device_scope"));
        await store.ActivateDesktopDeviceAsync(TenantId, activation.SubscriptionId,
            new DesktopDeviceActivationRequest("device-a", "Office", "2.0.0"));

        Assert.Single(await store.ListDesktopDevicesAsync(TenantId, activation.SubscriptionId));
        Assert.Empty(await store.ListDesktopDevicesAsync(Guid.NewGuid(), activation.SubscriptionId));
    }


    [Fact]
    public async Task Device_Lifecycle_Events_Are_Recorded_In_Order()
    {
        using var signer = new LeaseSigner();
        ICommerceActivationStore store = new InMemoryCommerceActivationStore(
            new PaymentSubscriptionActivationService(), signer);
        var activation = await store.ActivateInitialPurchaseAsync(
            TenantId, InitialRequest("pay_device_audit"));

        await store.ActivateDesktopDeviceAsync(TenantId, activation.SubscriptionId,
            new DesktopDeviceActivationRequest("device-a", "Front Desk", "1.0.0"));
        await store.ValidateDesktopDeviceAsync(TenantId, activation.SubscriptionId,
            new DesktopDeviceValidationRequest("device-a", null));
        var replacement = await store.ReplaceDesktopDeviceAsync(TenantId, activation.SubscriptionId,
            new DesktopDeviceReplaceRequest("device-a", "device-b", "Back Office", "1.1.0"));
        Assert.NotNull(replacement);
        Assert.True(await store.RevokeDesktopDeviceAsync(
            TenantId, activation.SubscriptionId, "device-b"));

        var events = await store.ListDesktopDeviceEventsAsync(
            TenantId, activation.SubscriptionId, 10);
        Assert.Equal(new[] { "revoke", "replace", "validate", "activate" },
            events.Select(x => x.Action).ToArray());
        Assert.Contains(events, x =>
            x.Action == "replace" &&
            x.DeviceFingerprint == "device-b" &&
            x.PreviousDeviceFingerprint == "device-a");
        Assert.All(events, x => Assert.Equal(TenantId, x.TenantId));
    }

    private static InitialActivationRequest InitialRequest(string paymentId) => new(
        OrganisationId,
        "AI_REPAIR",
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        1,
        29999m,
        "INR",
        12,
        1,
        1,
        10,
        10,
        true,
        paymentId,
        new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero));
}
