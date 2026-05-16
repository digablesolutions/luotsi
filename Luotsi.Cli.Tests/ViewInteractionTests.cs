using System.Text.Json;
using Luotsi.Cli;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Luotsi.Cli.Telemetry;
using Luotsi.Cli.View;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task RunAsync_View_InteractionHandler_Routes_Text_Scroll_Clipboard_And_FileDrop_To_DeviceHost()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var renderer = new ClosingViewRenderer();
        var rendererFactory = new FakeViewRendererFactory(renderer);
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new BlockingViewBackend()),
            new FakeViewStreamConnector(new ViewPacketStreamHarness().WriteHeader("h264", 1080, 1920).Build()),
            new ViewPacketStreamReader(),
            rendererFactory);

        var runTask = session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false));
        var interactionHandler = await ViewTestWaitHelpers.WaitForInteractionHandlerAsync(rendererFactory);
        await interactionHandler(new ViewTextInputRequest("hello"));
        await interactionHandler(new ViewKeyInputRequest("KEYCODE_ENTER"));
        await interactionHandler(new ViewScrollRequest(0, 1));
        await interactionHandler(new ViewClipboardPasteRequest("paste"));
        await interactionHandler(new ViewFileDropRequest("C:/tmp/note.txt"));
        await interactionHandler(new ViewFileDropRequest("C:/tmp/app.apk"));
        await interactionHandler(new ViewFilePullRequest("/sdcard/Download/report.txt", "C:/tmp/pulled"));
        await interactionHandler(new ViewWindowCommandRequest(ViewWindowCommand.Back));
        await interactionHandler(new ViewWindowCommandRequest(ViewWindowCommand.Home));
        await interactionHandler(new ViewWindowCommandRequest(ViewWindowCommand.Recents));
        await interactionHandler(new ViewWindowCommandRequest(ViewWindowCommand.OpenArtifacts));
        renderer.Close();
        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.Equal(["hello", "paste"], host.TypeTextRequests);
        Assert.Equal(["KEYCODE_ENTER", "KEYCODE_BACK", "KEYCODE_HOME", "KEYCODE_APP_SWITCH"], host.KeyEventRequests);
        Assert.Equal([(0, 1)], host.ScrollRequests);
        Assert.Equal([("C:/tmp/note.txt", null)], host.PushFileRequests);
        Assert.Equal([("/sdcard/Download/report.txt", "C:/tmp/pulled")], host.PullFileRequests);
        Assert.Equal(["C:/tmp/app.apk"], host.InstallPackageRequests);
        Assert.Contains(console.OutputLines, line => line.Contains("view_clipboard_pasted", StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, line => line.Contains("view_file_pushed", StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, line => line.Contains("view_file_pulled", StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, line => line.Contains("view_package_installed", StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, line => line.Contains("view_artifacts_requested", StringComparison.Ordinal));
    }



    [Fact]
    public async Task RunAsync_View_ReadOnly_Blocks_Interactive_Requests()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var renderer = new ClosingViewRenderer();
        var rendererFactory = new FakeViewRendererFactory(renderer);
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new BlockingViewBackend()),
            new FakeViewStreamConnector(new ViewPacketStreamHarness().WriteHeader("h264", 1080, 1920).Build()),
            new ViewPacketStreamReader(),
            rendererFactory);

        var runTask = session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false, 1000, 0, "balanced", true));
        var interactionHandler = await ViewTestWaitHelpers.WaitForInteractionHandlerAsync(rendererFactory);
        await interactionHandler(new ViewTapRequest(0.5d, 0.5d));
        await interactionHandler(new ViewTextInputRequest("hello"));
        await interactionHandler(new ViewScrollRequest(0, 1));
        await interactionHandler(new ViewFileDropRequest("C:/tmp/note.txt"));
        await interactionHandler(new ViewFilePullRequest("/sdcard/Download/report.txt"));
        await interactionHandler(new ViewWindowCommandRequest(ViewWindowCommand.Back));
        renderer.Close();
        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.Empty(host.TapPointRequests);
        Assert.Empty(host.TypeTextRequests);
        Assert.Empty(host.ScrollRequests);
        Assert.Empty(host.PushFileRequests);
        Assert.Empty(host.PullFileRequests);
        Assert.Empty(host.KeyEventRequests);
        Assert.Contains(console.OutputLines, line => line.Contains("view_input_blocked", StringComparison.Ordinal));
    }



    [Fact]
    public async Task RunAsync_View_Emits_Interaction_Failure_Event()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var renderer = new ClosingViewRenderer();
        var rendererFactory = new FakeViewRendererFactory(renderer);
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new BlockingViewBackend()),
            new FakeViewStreamConnector(new ViewPacketStreamHarness().WriteHeader("h264", 1080, 1920).Build()),
            new ViewPacketStreamReader(),
            rendererFactory);

        var runTask = session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false));
        var interactionHandler = await ViewTestWaitHelpers.WaitForInteractionHandlerAsync(rendererFactory);
        await interactionHandler(new ViewInteractionFailedRequest("ViewFileDropRequest", "System.InvalidOperationException", "push failed"));
        renderer.Close();
        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.Contains(console.OutputLines, line => line.Contains("view_interaction_failed", StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, line => line.Contains("ViewFileDropRequest", StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, line => line.Contains("push failed", StringComparison.Ordinal));
    }



    [Fact]
    public async Task RunAsync_View_Emits_Device_Shelf_When_Multiple_Devices_Are_Visible()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        host.ConnectedDevices.Add(new DeviceInfo("192.168.0.134:5555", "device", "Pixel 9"));
        host.ConnectedDevices.Add(new DeviceInfo("emulator-5554", "device", "Emulator"));
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new FakeViewBackend("ffmpeg-native")),
            new FakeViewStreamConnector(new ViewPacketStreamHarness().WriteHeader("h264", 1080, 1920).WritePacket(ViewPacketType.StreamEnd, 1, 0, false, []).Build()),
            new ViewPacketStreamReader(),
            new FakeViewRendererFactory(new StatsCapturingViewRenderer()));

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false));

        Assert.Equal(0, exitCode);
        Assert.Contains(console.OutputLines, line => line.Contains("view_device_shelf", StringComparison.Ordinal));
    }



    [Fact]
    public async Task RunAsync_View_InteractionHandler_Reconnects_And_Emits_Events()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var renderer = new ClosingViewRenderer();
        var rendererFactory = new FakeViewRendererFactory(renderer);
        var bootstrap = new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward"));
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            bootstrap,
            new FakeViewBackendFactory(new BlockingViewBackend()),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness().WriteHeader("h264", 1080, 1920).Build(),
                new ViewPacketStreamHarness().WriteHeader("h264", 1080, 1920).Build()),
            new ViewPacketStreamReader(),
            rendererFactory);

        var runTask = session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false));
        var interactionHandler = await ViewTestWaitHelpers.WaitForInteractionHandlerAsync(rendererFactory);
        await interactionHandler(new ViewWindowCommandRequest(ViewWindowCommand.Reconnect));
        await ViewTestWaitHelpers.WaitForStartCallsAsync(bootstrap, 2);
        renderer.Close();
        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.True(bootstrap.StartCallCount >= 2);
        Assert.Contains(console.OutputLines, line => line.Contains("view_reconnect_requested", StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, line => line.Contains("view_reconnected", StringComparison.Ordinal));
    }



    [Fact]
    public async Task RunAsync_View_InteractionHandler_Switches_Device_And_Reconnects_On_Selected_Device()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        host.ConnectedDevices.Add(new DeviceInfo("device-a", "device", "Primary"));
        host.ConnectedDevices.Add(new DeviceInfo("device-b", "device", "Secondary"));

        var renderer = new ClosingViewRenderer();
        var rendererFactory = new FakeViewRendererFactory(renderer);
        var bootstrap = new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward"));
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            bootstrap,
            new FakeViewBackendFactory(new BlockingViewBackend()),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness().WriteHeader("h264", 1080, 1920).Build(),
                new ViewPacketStreamHarness().WriteHeader("h264", 1080, 1920).Build()),
            new ViewPacketStreamReader(),
            rendererFactory);

        var runTask = session.RunAsync(new ViewOptions("device-a", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false));
        var interactionHandler = await ViewTestWaitHelpers.WaitForInteractionHandlerAsync(rendererFactory);
    await ViewTestWaitHelpers.WaitForStartCallsAsync(bootstrap, 1);
        await interactionHandler(new ViewSwitchDeviceRequest("device-b"));
        await ViewTestWaitHelpers.WaitForStartCallsAsync(bootstrap, 2);
        renderer.Close();
        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.Equal(["device-a", "device-b"], bootstrap.StartRequests.Select(request => request.DeviceSelector).ToArray());
        Assert.Contains(console.OutputLines, line => line.Contains("view_device_switch_requested", StringComparison.Ordinal));
        Assert.Equal(2, console.OutputLines.Count(line => line.Contains("view_device_shelf", StringComparison.Ordinal)));
    }



}
