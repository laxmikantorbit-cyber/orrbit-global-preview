using BusinessOS.Api.SoftwareDelivery;

namespace BusinessOS.Api.Tests;

public sealed class SoftwareDeliveryTests
{
    [Fact]
    public void Release_Validator_Normalizes_A_Valid_Https_Release()
    {
        var tenantId = Guid.NewGuid();
        var item = SoftwareReleaseValidator.Normalize(
            tenantId,
            Request("1.2.3", "https://downloads.example.test/repair-1.2.3.exe"));

        Assert.Equal(tenantId, item.TenantId);
        Assert.Equal("AI_REPAIR", item.ProductCode);
        Assert.Equal("1.2.3", item.Version);
        Assert.Equal("Stable", item.Channel);
        Assert.True(item.Active);
    }

    [Fact]
    public void Release_Validator_Rejects_Invalid_Sha256()
    {
        var request = Request("1.0.0", "https://downloads.example.test/repair.exe") with
        {
            Sha256 = "not-a-sha"
        };

        var error = Assert.Throws<ArgumentException>(
            () => SoftwareReleaseValidator.Normalize(Guid.NewGuid(), request));

        Assert.Contains("SHA-256", error.Message);
    }

    [Fact]
    public void Release_Validator_Rejects_NonHttps_Download_Url()
    {
        var error = Assert.Throws<ArgumentException>(
            () => SoftwareReleaseValidator.Normalize(
                Guid.NewGuid(),
                Request("1.0.0", "http://downloads.example.test/repair.exe")));

        Assert.Contains("HTTPS", error.Message);
    }

    [Fact]
    public async Task Store_Returns_Latest_Active_Release_And_Honours_Deactivation()
    {
        var tenantId = Guid.NewGuid();
        var store = new InMemorySoftwareReleaseStore();

        var oldRelease = await store.AddAsync(
            tenantId,
            Request("1.0.0", "https://downloads.example.test/repair-1.0.0.exe",
                DateTimeOffset.Parse("2026-09-01T00:00:00Z")));
        var latest = await store.AddAsync(
            tenantId,
            Request("1.1.0", "https://downloads.example.test/repair-1.1.0.exe",
                DateTimeOffset.Parse("2026-09-10T00:00:00Z")));

        var selected = await store.FindLatestActiveAsync(
            tenantId, "AI_REPAIR", "Stable", "Windows", "x64");
        Assert.Equal(latest.Id, selected!.Id);

        Assert.True(await store.DeactivateAsync(tenantId, latest.Id));

        selected = await store.FindLatestActiveAsync(
            tenantId, "AI_REPAIR", "Stable", "Windows", "x64");
        Assert.Equal(oldRelease.Id, selected!.Id);
    }

    [Fact]
    public async Task Store_Is_Tenant_Isolated()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var store = new InMemorySoftwareReleaseStore();

        await store.AddAsync(
            tenantA,
            Request("1.0.0", "https://downloads.example.test/a.exe"));

        Assert.Null(await store.FindLatestActiveAsync(
            tenantB, "AI_REPAIR", "Stable", "Windows", "x64"));
        Assert.Empty(await store.ListAsync(tenantB, null, 50));
    }


    [Fact]
    public void Release_Validator_Rejects_Unsafe_Installer_File_Name()
    {
        var request = Request("1.0.0", "https://downloads.example.test/repair.exe") with
        {
            FileName = "..\\repair.exe"
        };

        var error = Assert.Throws<ArgumentException>(
            () => SoftwareReleaseValidator.Normalize(Guid.NewGuid(), request));

        Assert.Contains("unsafe", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_Validator_Rejects_Unsupported_Target_Metadata()
    {
        var request = Request("1.0.0", "https://downloads.example.test/repair.exe") with
        {
            Channel = "Public",
            Architecture = "mips"
        };

        var error = Assert.Throws<ArgumentException>(
            () => SoftwareReleaseValidator.Normalize(Guid.NewGuid(), request));

        Assert.Contains("Channel", error.Message);
    }


    [Fact]
    public async Task Delivery_Event_Store_Is_Tenant_Isolated()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var store = new InMemorySoftwareDeliveryEventStore();

        await store.AddAsync(tenantA, new SoftwareDeliveryEventCreateRequest(
            subscriptionId, null, "AI_REPAIR", "Stable", "Windows", "x64",
            "delivery_viewed", false, "No release"));

        Assert.Single(await store.ListAsync(tenantA, subscriptionId, 50));
        Assert.Empty(await store.ListAsync(tenantB, null, 50));
    }

    [Fact]
    public async Task Delivery_Event_Store_Returns_Newest_First_And_Filters_Subscription()
    {
        var tenantId = Guid.NewGuid();
        var subscriptionA = Guid.NewGuid();
        var subscriptionB = Guid.NewGuid();
        var store = new InMemorySoftwareDeliveryEventStore();
        await store.AddAsync(tenantId, Event(subscriptionA, "2026-09-01T00:00:00Z"));
        await store.AddAsync(tenantId, Event(subscriptionB, "2026-09-03T00:00:00Z"));
        await store.AddAsync(tenantId, Event(subscriptionA, "2026-09-05T00:00:00Z"));

        var filtered = await store.ListAsync(tenantId, subscriptionA, 50);
        Assert.Equal(2, filtered.Count);
        Assert.True(filtered[0].OccurredAtUtc > filtered[1].OccurredAtUtc);
    }

    private static SoftwareDeliveryEventCreateRequest Event(Guid subscriptionId, string occurredAtUtc) =>
        new(subscriptionId, Guid.NewGuid(), "AI_REPAIR", "Stable", "Windows", "x64",
            "delivery_viewed", true, null, DateTimeOffset.Parse(occurredAtUtc));

    private static SoftwareReleaseCreateRequest Request(
        string version,
        string url,
        DateTimeOffset? published = null) =>
        new(
            "AI_REPAIR",
            version,
            "Stable",
            "Windows",
            "x64",
            $"oRRbit-AI-Repair-{version}.exe",
            url,
            new string('a', 64),
            123456,
            "FreeTesting release metadata",
            published);
}
