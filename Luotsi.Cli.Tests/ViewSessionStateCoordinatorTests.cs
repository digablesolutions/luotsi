using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Session;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class ViewSessionStateCoordinatorTests
{
    [Fact]
    public async Task SwitchDeviceAsync_Updates_Chrome_And_Signals_Reconnect()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("device-a", "device", "Primary"));
        host.ConnectedDevices.Add(new DeviceInfo("device-b", "device", "Secondary"));
        var context = CreateContext(host, fileSystem, timeProvider, new ViewOptions("device-a", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false));
        var coordinator = new ViewSessionStateCoordinator(context);
        var chromeUpdates = new List<ViewChromeState>();
        using var iterationCancellation = new CancellationTokenSource();

        coordinator.AttachChromeUpdater(chrome =>
        {
            chromeUpdates.Add(chrome);
            return Task.CompletedTask;
        });
        coordinator.BeginIteration("device-a", iterationCancellation);
        await coordinator.EmitDeviceShelfSnapshotIfNeededAsync();

        var switched = await coordinator.SwitchDeviceAsync("device-b");

        Assert.True(switched);
        Assert.Equal("device-b", coordinator.ActiveDeviceSelector);
        Assert.True(iterationCancellation.IsCancellationRequested);
        Assert.Equal(coordinator.WaitForReconnectAsync(), await Task.WhenAny(coordinator.WaitForReconnectAsync(), Task.Delay(100)));
        Assert.Equal("device-b", chromeUpdates[^1].ActiveDevice);
        Assert.Equal([false, true], chromeUpdates[^1].Devices.Select(static device => device.IsActive).ToArray());
        Assert.True(chromeUpdates[^1].CanSwitchDevices);
    }

    [Fact]
    public async Task UpdateShareStateAsync_Publishes_Chrome_With_Observer_Count()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var context = CreateContext(
            new FakeDeviceHost(),
            fileSystem,
            timeProvider,
            new ViewOptions("device-a", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false));
        var coordinator = new ViewSessionStateCoordinator(context);
        ViewChromeState? lastChrome = null;

        coordinator.AttachChromeUpdater(chrome =>
        {
            lastChrome = chrome;
            return Task.CompletedTask;
        });
        coordinator.BeginIteration("device-a", new CancellationTokenSource());

        await coordinator.UpdateShareStateAsync("127.0.0.1:4040", 3);

        Assert.NotNull(lastChrome);
        Assert.Equal("127.0.0.1:4040", lastChrome!.ShareEndpoint);
        Assert.Equal(3, lastChrome.ObserverCount);
        Assert.Equal("device-a", lastChrome.ActiveDevice);
        Assert.True(lastChrome.CanReconnect);
    }

    private static ViewSessionInteractionContext CreateContext(
        IDeviceHost host,
        FakeFileSystem fileSystem,
        ManualTimeProvider timeProvider,
        ViewOptions options)
    {
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider);
        var recorder = new SessionControlledViewRecorder(new FakeViewRecorderFactory(), options);
        return new ViewSessionInteractionContext(
            host,
            artifacts,
            options,
            recorder,
            timeProvider,
            "session",
            _ => { },
            new FakeArtifactFolderOpener());
    }
}