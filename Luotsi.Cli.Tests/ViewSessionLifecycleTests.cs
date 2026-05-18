using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Session;
using Luotsi.Cli.View.Transport;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task RunAsync_View_Streams_Scaffold_Events()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var backend = new FakeViewBackend("ffmpeg-native");
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(backend),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.Config, 1, 0, false, [0x01, 0x02])
                    .WritePacket(ViewPacketType.StreamEnd, 2, 33_000, false, [])
                    .Build()),
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", true, false));

        Assert.Equal(0, exitCode);
        Assert.Equal(2, console.OutputLines.Count);

        using var started = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Started, started.RootElement.GetProperty("type").GetString());
        Assert.Equal("192.168.0.134:5555", started.RootElement.GetProperty("device").GetString());
        Assert.Equal("ffmpeg", started.RootElement.GetProperty("decoder").GetString());
        Assert.Equal("h264", started.RootElement.GetProperty("connection").GetProperty("codec").GetString());
        Assert.Equal(1080, started.RootElement.GetProperty("connection").GetProperty("width").GetInt32());
        Assert.True(started.RootElement.GetProperty("headless").GetBoolean());
        Assert.True(started.RootElement.GetProperty("overlay_screen_state").GetBoolean());

        using var ended = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.View.Ended, ended.RootElement.GetProperty("type").GetString());
        Assert.Equal("stream_ended", ended.RootElement.GetProperty("reason").GetString());
        Assert.Equal(2, backend.Packets.Count);
    }



    [Fact]
    public async Task RunAsync_View_Uses_Backend_Selected_By_Decoder()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var ffmpegBackend = new FakeViewBackend("ffmpeg-native");
        var wmfBackend = new FakeViewBackend("wmf-test");
        var backendFactory = new FakeViewBackendFactory(new Dictionary<string, IViewBackend>(StringComparer.OrdinalIgnoreCase)
        {
            ["ffmpeg"] = ffmpegBackend,
            ["wmf"] = wmfBackend
        });
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            backendFactory,
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                    .Build()),
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "wmf", true, null, 1600, 60, "8M", false, false));

        Assert.Equal(0, exitCode);
        Assert.Equal(["wmf"], backendFactory.RequestedDecoders);
        Assert.Empty(ffmpegBackend.Packets);
        Assert.Single(wmfBackend.Packets);

        using var started = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal("wmf-test", started.RootElement.GetProperty("backend").GetString());
        Assert.Equal("wmf", started.RootElement.GetProperty("decoder").GetString());
        Assert.DoesNotContain(console.OutputLines, line => line.Contains("process_backed_ffmpeg", StringComparison.Ordinal));
    }



    [Fact]
    public async Task RunAsync_View_Retries_Initial_Stream_Header_Read()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var backend = new FakeViewBackend();
        var streamConnector = new FakeViewStreamConnector(
            new MemoryStream(),
            new ViewPacketStreamHarness()
                .WriteHeader("h264", 1080, 1920)
                .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                .Build());
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(backend),
            streamConnector,
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false));

        Assert.Equal(0, exitCode);
        Assert.Equal(2, streamConnector.ConnectCallCount);
        Assert.Contains(console.OutputLines, line => line.Contains(SessionEventTypes.View.Started, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_View_Streams_Startup_Phases_From_Transport_Bootstrap()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var backend = new FakeViewBackend();
        var bootstrap = new FakeViewTransportBootstrap(
            [new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")],
            [new ViewStartupPhase("helper_resolve", ViewStartupPhaseStatus.Started, "Resolving Android view helper package.")]);
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            bootstrap,
            new FakeViewBackendFactory(backend),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                    .Build()),
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false));

        Assert.Equal(0, exitCode);
        Assert.Equal(3, console.OutputLines.Count);

        using var phase = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.StartupPhase, phase.RootElement.GetProperty("type").GetString());
        Assert.Equal("helper_resolve", phase.RootElement.GetProperty("phase").GetString());
        Assert.Equal(ViewStartupPhaseStatus.Started, phase.RootElement.GetProperty("status").GetString());

        using var started = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.View.Started, started.RootElement.GetProperty("type").GetString());

        using var ended = JsonDocument.Parse(console.OutputLines[2]);
        Assert.Equal(SessionEventTypes.View.Ended, ended.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task RunAsync_View_AutoCaptureBackend_Falls_Back_To_Screenrecord_When_MediaProjection_Start_Fails()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var backend = new FakeViewBackend();
        var bootstrap = new FakeViewTransportBootstrap([
            new InvalidOperationException("mediaprojection consent was not granted"),
            new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward", CaptureBackend: ViewCaptureBackends.Screenrecord)
        ]);
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            bootstrap,
            new FakeViewBackendFactory(backend),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                    .Build()),
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false));

        Assert.Equal(0, exitCode);
        Assert.Equal([ViewCaptureBackends.Auto, ViewCaptureBackends.Screenrecord], bootstrap.StartRequests.Select(static request => request.CaptureBackend).ToArray());
        Assert.Equal(2, bootstrap.StopCallCount);

        using var fallback = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.CaptureBackendFallback, fallback.RootElement.GetProperty("type").GetString());
        Assert.Equal(ViewCaptureBackends.MediaProjection, fallback.RootElement.GetProperty("failed_capture_backend").GetString());
        Assert.Equal(ViewCaptureBackends.Screenrecord, fallback.RootElement.GetProperty("fallback_capture_backend").GetString());

        using var started = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.View.Started, started.RootElement.GetProperty("type").GetString());
        Assert.Equal(ViewCaptureBackends.Screenrecord, started.RootElement.GetProperty("capture_backend").GetString());
    }

    [Fact]
    public async Task RunAsync_View_AutoCaptureBackend_Falls_Back_To_Screenrecord_When_MediaProjection_Reports_Startup_ServerError()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var backend = new FakeViewBackend();
        var bootstrap = new FakeViewTransportBootstrap([
            new ViewConnectionInfo("mediaprojection-session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward", CaptureBackend: ViewCaptureBackends.MediaProjection),
            new ViewConnectionInfo("screenrecord-session", "h264", 1, 1080, 1920, 27184, "helper", "adb-forward", CaptureBackend: ViewCaptureBackends.Screenrecord)
        ]);
        var streamConnector = new FakeViewStreamConnector(
            new ViewPacketStreamHarness()
                .WriteHeader("h264", 1080, 1920)
                .WritePacket(ViewPacketType.ServerError, 1, 0, false, System.Text.Encoding.UTF8.GetBytes("MediaCodec preflight failed"))
                .WritePacket(ViewPacketType.StreamEnd, 2, 0, false, [])
                .Build(),
            new ViewPacketStreamHarness()
                .WriteHeader("h264", 1080, 1920)
                .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                .Build());
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            bootstrap,
            new FakeViewBackendFactory(backend),
            streamConnector,
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false));

        Assert.Equal(0, exitCode);
        Assert.Equal([ViewCaptureBackends.Auto, ViewCaptureBackends.Screenrecord], bootstrap.StartRequests.Select(static request => request.CaptureBackend).ToArray());
        Assert.Equal(2, streamConnector.ConnectCallCount);
        Assert.Equal(2, bootstrap.StopCallCount);
        Assert.Single(backend.Packets);
        Assert.Equal(ViewPacketType.StreamEnd, backend.Packets[0].PacketType);

        using var fallback = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.CaptureBackendFallback, fallback.RootElement.GetProperty("type").GetString());
        Assert.Equal("MediaCodec preflight failed", fallback.RootElement.GetProperty("reason").GetString());

        using var started = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.View.Started, started.RootElement.GetProperty("type").GetString());
        Assert.Equal(ViewCaptureBackends.Screenrecord, started.RootElement.GetProperty("capture_backend").GetString());
    }

    [Fact]
    public async Task RunAsync_View_Explicit_MediaProjection_Consent_Failure_Returns_Usage_Error()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var backend = new FakeViewBackend();
        var bootstrap = new FakeViewTransportBootstrap([
            new MediaProjectionConsentException("MediaProjection consent prompt was not approved or could not be detected.")
        ]);
        using var stream = new MemoryStream();
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            bootstrap,
            new FakeViewBackendFactory(backend),
            new FakeViewStreamConnector(stream),
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions(
            "192.168.0.134:5555",
            "adb",
            "h264",
            "ffmpeg",
            true,
            null,
            1600,
            60,
            "8M",
            false,
            false,
            CaptureBackend: ViewCaptureBackends.MediaProjection));

        Assert.Equal(1, exitCode);
        Assert.Equal([ViewCaptureBackends.MediaProjection], bootstrap.StartRequests.Select(static request => request.CaptureBackend).ToArray());

        using var error = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Error, error.RootElement.GetProperty("type").GetString());
        Assert.Equal("usage_error", error.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("--capture-backend screenrecord", error.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_View_AutoCaptureBackend_Does_Not_Fall_Back_When_Helper_Package_Is_Missing()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var backend = new FakeViewBackend();
        var bootstrap = new FakeViewTransportBootstrap([
            new InvalidOperationException("Android view helper package was not found. Set LUOTSI_VIEW_HELPER_APK or build the helper APK at Luotsi.ViewServer.Android\\app\\build\\outputs\\apk\\debug\\app-debug.apk")
        ]);
        using var stream = new MemoryStream();
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            bootstrap,
            new FakeViewBackendFactory(backend),
            new FakeViewStreamConnector(stream),
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false));

        Assert.Equal(1, exitCode);
        Assert.Equal([ViewCaptureBackends.Auto], bootstrap.StartRequests.Select(static request => request.CaptureBackend).ToArray());
        Assert.Equal(1, bootstrap.StopCallCount);
        Assert.Equal(2, console.OutputLines.Count);

        using var error = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Error, error.RootElement.GetProperty("type").GetString());
        Assert.Equal("configuration_error", error.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("LUOTSI_VIEW_HELPER_APK", error.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);

        using var ended = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.View.Ended, ended.RootElement.GetProperty("type").GetString());
        Assert.Equal("error", ended.RootElement.GetProperty("reason").GetString());
    }



    [Fact]
    public async Task RunAsync_View_Window_Close_Ends_Session_Cleanly()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var renderer = new ClosingViewRenderer();
        var session = new ViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new BlockingViewBackend()),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .Build()),
            new ViewPacketStreamReader(),
            new FakeViewRendererFactory(renderer));

        var runTask = session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false));
        renderer.Close();
        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        using var ended = JsonDocument.Parse(console.OutputLines[^1]);
        Assert.Equal(SessionEventTypes.View.Ended, ended.RootElement.GetProperty("type").GetString());
        Assert.Equal("window_closed", ended.RootElement.GetProperty("reason").GetString());
    }



}
