using Luotsi.Cli.Cli.Routing;
using Luotsi.Cli.Errors;
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

    [Fact]
    public async Task AllocateAsync_RequirementMismatch_Throws_UsageException_With_InventoryCommand()
    {
        var fileSystem = new FakeFileSystem();
        var inventoryStore = new LabDeviceInventoryStore(fileSystem, TimeProvider.System);
        await inventoryStore.SetAsync("SER123", "regression", "camera", "lab-admin");
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Pixel 7", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER123")
        };
        host.ConnectedDevices.Add(new DeviceInfo("SER123", "device", "product:panther model:Pixel_7 device:panther"));
        var allocator = new ScenarioDeviceAllocator(inventoryStore);

        var error = await Assert.ThrowsAsync<UsageException>(() => allocator.AllocateAsync(
            host,
            CreateConfiguration(new DeviceAdmissionRequirements("smoke", ["camera", "nfc"]))));

        Assert.Contains("requires pool 'smoke' but inventory pool is 'regression'", error.Message, StringComparison.Ordinal);
        Assert.Contains("luotsi lab inventory set --serial SER123 --pool smoke", error.Message, StringComparison.Ordinal);
        Assert.Contains("--capabilities camera,nfc", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllocateAsync_Requirements_Without_Single_Selected_Device_Throws_UsageException()
    {
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Model", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", string.Empty)
        };
        host.ConnectedDevices.Add(new DeviceInfo("SER123", "device", "product:panther model:Pixel_7 device:panther"));
        host.ConnectedDevices.Add(new DeviceInfo("SER456", "device", "product:caiman model:Pixel_9_Pro device:caiman"));
        var allocator = new ScenarioDeviceAllocator();

        var error = await Assert.ThrowsAsync<UsageException>(() => allocator.AllocateAsync(
            host,
            CreateConfiguration(new DeviceAdmissionRequirements("smoke", ["camera"]), requireDeviceReady: false)));

        Assert.Contains("require a single selected device", error.Message, StringComparison.Ordinal);
        Assert.Contains("--device", error.Message, StringComparison.Ordinal);
        Assert.Contains("--device-query", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllocateAsync_Returns_Inventory_Metadata_When_Requirements_Are_Satisfied()
    {
        var fileSystem = new FakeFileSystem();
        var inventoryStore = new LabDeviceInventoryStore(fileSystem, TimeProvider.System);
        await inventoryStore.SetAsync("SER123", "smoke", "camera,nfc", "lab-admin");
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Pixel 7", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER123")
        };
        host.ConnectedDevices.Add(new DeviceInfo("SER123", "device", "product:panther model:Pixel_7 device:panther"));
        var allocator = new ScenarioDeviceAllocator(inventoryStore);

        var result = await allocator.AllocateAsync(
            host,
            CreateConfiguration(new DeviceAdmissionRequirements("smoke", ["camera", "nfc"])));

        Assert.Equal("smoke", result.Pool);
        Assert.True(result.InventoryRegistered);
        Assert.Equal(["camera", "nfc"], result.Requirements!.Capabilities);
        Assert.Contains("adb", result.Capabilities!);
        Assert.Contains("camera", result.Capabilities);
        Assert.Contains("nfc", result.Capabilities);
        Assert.Contains("model:Pixel_7", result.Capabilities);
    }

    private static ScenarioRunConfiguration CreateConfiguration(
        DeviceAdmissionRequirements? requirements = null,
        bool requireDeviceReady = true) =>
        new(
            EventsJsonlPath: null,
            JsonReportPath: null,
            JUnitReportPath: null,
            FailureArtifactCapturePolicy: ScenarioFailureArtifactCapturePolicy.Failure,
            ArtifactAttachmentPolicy: ScenarioArtifactAttachmentPolicy.OnFailure,
            ValidateOnly: false,
            RequireDeviceReady: requireDeviceReady,
            DeviceWaitTimeoutSec: 7,
            DeviceReadinessPackage: "dev.luotsi.app",
            DeviceRequirements: requirements);
}