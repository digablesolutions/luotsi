using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class ScenarioDeviceAllocatorTests
{
    [Fact]
    public async Task AllocateAsync_ReadinessSerial_Reuses_Inventory_Metadata()
    {
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Pixel 7", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER123")
        };
        host.ConnectedDevices.Add(new DeviceInfo("SER123", "device", "product:panther model:Pixel_7 device:panther"));
        var allocator = new ScenarioDeviceAllocator();

        var result = await allocator.AllocateAsync(host, CreateConfiguration());

        Assert.Equal("SER123", result.Serial);
        Assert.NotNull(result.Device);
        Assert.Equal("online", result.Device!.State);
        Assert.Equal("Pixel_7", result.Device.Model);
        Assert.Equal("panther", result.Device.Product);
        Assert.Equal("panther", result.Device.Device);
        Assert.Equal([7], host.WaitForDeviceRequests);
        Assert.Equal(["dev.luotsi.app"], host.ReadOnlyPreflightRequests);
    }

    [Fact]
    public async Task AllocateAsync_InventoryRefreshFailure_Does_NotDiscard_Readiness()
    {
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Pixel 7", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER123"),
            GetDevicesException = new InvalidOperationException("inventory failed")
        };
        var allocator = new ScenarioDeviceAllocator();

        var result = await allocator.AllocateAsync(host, CreateConfiguration());

        Assert.Equal("SER123", result.Serial);
        Assert.Null(result.Device);
        Assert.NotNull(result.Readiness);
        Assert.Equal("Pixel 7", result.Readiness!.Model);
    }

    private static ScenarioRunConfiguration CreateConfiguration() =>
        new(
            EventsJsonlPath: null,
            JsonReportPath: null,
            JUnitReportPath: null,
            FailureArtifactCapturePolicy: ScenarioFailureArtifactCapturePolicy.Failure,
            ArtifactAttachmentPolicy: ScenarioArtifactAttachmentPolicy.OnFailure,
            ValidateOnly: false,
            RequireDeviceReady: true,
            DeviceWaitTimeoutSec: 7,
            DeviceReadinessPackage: "dev.luotsi.app");
}