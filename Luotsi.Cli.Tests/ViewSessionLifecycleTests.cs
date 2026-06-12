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
    public void ViewConsoleEventWriter_Human_Output_Uses_Ansi_Color_When_Console_Supports_It()
    {
        var console = new FakeConsole { SupportsAnsiStyling = true };
        var writer = new ViewConsoleEventWriter(
            console,
            new ViewOptions("device", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false, ConsoleOutput: ViewConsoleOutputModes.Human));

        writer.Write("""{"type":"view_recording_started","record_path":"C:\\tmp\\kick-smoke.mp4"}""");

        var line = Assert.Single(console.OutputLines);
        Assert.StartsWith("\u001b[32;1mOK \u001b[0m Recording started:", line, StringComparison.Ordinal);
        Assert.Contains("\u001b[36;1mC:\\tmp\\kick-smoke.mp4\u001b[0m", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewRuntimeDiagnostic_Uses_JoinShare_Command_For_Share_Sessions()
    {
        var options = new ViewOptions("127.0.0.1:9000", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false, JoinShareEndpoint: "127.0.0.1:9000");

        var diagnostic = ViewRuntimeDiagnostic.From(new InvalidOperationException("Unexpected end of stream"), options);

        Assert.Equal("transport", diagnostic.Category);
        Assert.Contains("--join-share 127.0.0.1:9000", diagnostic.NextCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("--device 127.0.0.1:9000", diagnostic.NextCommand, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_View_Streams_Scaffold_Events()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var backend = new FakeViewBackend("ffmpeg-native");
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider);
        var session = CreateViewSession(
            host,
            artifacts,
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(backend),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.Diagnostic, 1, 1_000, false, """{"phase":"encoder_setup","status":"succeeded","message":"Encoder ready.","capture_backend":"screenrecord","width":1080,"height":1920}"""u8.ToArray())
                    .WritePacket(ViewPacketType.Config, 2, 0, false, [0x01, 0x02])
                    .WritePacket(ViewPacketType.StreamEnd, 3, 33_000, false, [])
                    .Build()),
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", true, false));

        Assert.Equal(0, exitCode);
        Assert.Equal(3, console.OutputLines.Count);

        using var started = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Started, started.RootElement.GetProperty("type").GetString());
        Assert.Equal("192.168.0.134:5555", started.RootElement.GetProperty("device").GetString());
        Assert.Equal("ffmpeg", started.RootElement.GetProperty("decoder").GetString());
        Assert.Equal("h264", started.RootElement.GetProperty("connection").GetProperty("codec").GetString());
        Assert.Equal(1080, started.RootElement.GetProperty("connection").GetProperty("width").GetInt32());
        Assert.True(started.RootElement.GetProperty("headless").GetBoolean());
        Assert.True(started.RootElement.GetProperty("overlay_screen_state").GetBoolean());

        using var diagnostic = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.View.Diagnostic, diagnostic.RootElement.GetProperty("type").GetString());
        Assert.Equal("android_helper", diagnostic.RootElement.GetProperty("source").GetString());
        Assert.Equal("encoder_setup", diagnostic.RootElement.GetProperty("phase").GetString());
        Assert.Equal("succeeded", diagnostic.RootElement.GetProperty("status").GetString());
        Assert.Equal("Encoder ready.", diagnostic.RootElement.GetProperty("message").GetString());
        Assert.Equal(ViewCaptureBackends.Screenrecord, diagnostic.RootElement.GetProperty("capture_backend").GetString());
        Assert.Equal(1080, diagnostic.RootElement.GetProperty("width").GetInt32());

        using var ended = JsonDocument.Parse(console.OutputLines[2]);
        Assert.Equal(SessionEventTypes.View.Ended, ended.RootElement.GetProperty("type").GetString());
        Assert.Equal("stream_ended", ended.RootElement.GetProperty("reason").GetString());
        Assert.Equal(2, backend.Packets.Count);
        Assert.DoesNotContain(backend.Packets, static packet => packet.PacketType == ViewPacketType.Diagnostic);

        var replayPath = Path.Join(artifacts.Root, "session-replay.json");
        var timelinePath = Path.Join(artifacts.Root, "session-timeline.jsonl");
        Assert.True(fileSystem.FileExists(replayPath));
        Assert.True(fileSystem.FileExists(timelinePath));

        using var replay = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(replayPath));
        Assert.Equal(ResultSchemas.SessionReplay, replay.RootElement.GetProperty("schema").GetString());
        Assert.Equal("view", replay.RootElement.GetProperty("sessionKind").GetString());
        Assert.Equal("192.168.0.134:5555", replay.RootElement.GetProperty("target").GetString());
        Assert.Equal("session-timeline.jsonl", replay.RootElement.GetProperty("timelineFileName").GetString());
        Assert.Equal(3, replay.RootElement.GetProperty("eventCount").GetInt32());

        var timeline = await fileSystem.ReadAllTextAsync(timelinePath);
        Assert.Contains(SessionEventTypes.View.Started, timeline, StringComparison.Ordinal);
        Assert.Contains("encoder_setup", timeline, StringComparison.Ordinal);
        Assert.Contains(SessionEventTypes.View.Ended, timeline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_View_Human_Output_Still_Writes_Jsonl_Timeline()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var backend = new FakeViewBackend("ffmpeg-native");
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider);
        var session = CreateViewSession(
            host,
            artifacts,
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
            ConsoleOutput: ViewConsoleOutputModes.Human));

        Assert.Equal(0, exitCode);
        Assert.Contains(console.OutputLines, static line => line.Equals("View started", StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, static line => line.Contains("device: 192.168.0.134:5555", StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, static line => line.Contains("stream: 1080x1920 h264", StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, static line => line.Equals("View ended: stream_ended", StringComparison.Ordinal));
        Assert.DoesNotContain(console.OutputLines, static line => line.StartsWith('{'));

        var timeline = await fileSystem.ReadAllTextAsync(Path.Join(artifacts.Root, "session-timeline.jsonl"));
        Assert.Contains(SessionEventTypes.View.Started, timeline, StringComparison.Ordinal);
        Assert.Contains(SessionEventTypes.View.Ended, timeline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_View_Quiet_Output_Suppresses_Normal_Lifecycle_Events()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider);
        var session = CreateViewSession(
            host,
            artifacts,
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new FakeViewBackend("ffmpeg-native")),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                    .Build()),
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
            ConsoleOutput: ViewConsoleOutputModes.Quiet));

        Assert.Equal(0, exitCode);
        Assert.Empty(console.OutputLines);

        var timeline = await fileSystem.ReadAllTextAsync(Path.Join(artifacts.Root, "session-timeline.jsonl"));
        Assert.Contains(SessionEventTypes.View.Started, timeline, StringComparison.Ordinal);
        Assert.Contains(SessionEventTypes.View.Ended, timeline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_View_Fails_Before_Startup_When_Selected_Device_Is_Not_Visible()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        host.ConnectedDevices.Add(new DeviceInfo("192.168.0.134:5555", "device", "Panel"));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider);
        using var stream = new MemoryStream();
        var session = CreateViewSession(
            host,
            artifacts,
            console,
            timeProvider,
            new FakeViewTransportBootstrap([new InvalidOperationException("transport should not start")]),
            new FakeViewBackendFactory(new FakeViewBackend("ffmpeg-native")),
            new FakeViewStreamConnector(stream),
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:555", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false));

        Assert.Equal(1, exitCode);
        Assert.Equal(3, console.OutputLines.Count);
        using var diagnostic = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Diagnostic, diagnostic.RootElement.GetProperty("type").GetString());
        Assert.Equal("usage_error", diagnostic.RootElement.GetProperty("category").GetString());
        Assert.Contains("Live view device selection is not usable.", diagnostic.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);

        using var error = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Contains("Did you mean '--device 192.168.0.134:5555'?", error.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
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
        var session = CreateViewSession(
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
        var session = CreateViewSession(
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
        var session = CreateViewSession(
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
        var session = CreateViewSession(
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
                .WritePacket(ViewPacketType.Diagnostic, 1, 0, false, """{"phase":"encoder_setup","status":"failed","message":"Encoder setup failed.","capture_backend":"mediaprojection","error":"preflight"}"""u8.ToArray())
                .WritePacket(ViewPacketType.ServerError, 2, 0, false, "MediaCodec preflight failed"u8.ToArray())
                .WritePacket(ViewPacketType.StreamEnd, 3, 0, false, [])
                .Build(),
            new ViewPacketStreamHarness()
                .WriteHeader("h264", 1080, 1920)
                .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                .Build());
        var session = CreateViewSession(
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

        using var diagnostic = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Diagnostic, diagnostic.RootElement.GetProperty("type").GetString());
        Assert.Equal("android_helper", diagnostic.RootElement.GetProperty("source").GetString());
        Assert.Equal("encoder_setup", diagnostic.RootElement.GetProperty("phase").GetString());
        Assert.Equal("failed", diagnostic.RootElement.GetProperty("status").GetString());

        using var fallback = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.View.CaptureBackendFallback, fallback.RootElement.GetProperty("type").GetString());
        Assert.Equal("MediaCodec preflight failed", fallback.RootElement.GetProperty("reason").GetString());

        using var started = JsonDocument.Parse(console.OutputLines[2]);
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
        var session = CreateViewSession(
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

        using var diagnostic = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Diagnostic, diagnostic.RootElement.GetProperty("type").GetString());
        Assert.Equal("mediaprojection_consent", diagnostic.RootElement.GetProperty("category").GetString());
        Assert.Contains("capture-backend auto", diagnostic.RootElement.GetProperty("next_command").GetString(), StringComparison.Ordinal);

        using var error = JsonDocument.Parse(console.OutputLines[1]);
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
            new InvalidOperationException("Android view helper package was not found. Run `luotsi view setup --device <serial> --fix` to build/install it from source, set LUOTSI_VIEW_HELPER_APK, or reinstall Luotsi from a release bundle that includes Luotsi.ViewServer.Android\\app\\build\\outputs\\apk\\release\\app-release.apk.")
        ]);
        using var stream = new MemoryStream();
        var session = CreateViewSession(
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
        Assert.Equal(3, console.OutputLines.Count);

        using var diagnostic = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Diagnostic, diagnostic.RootElement.GetProperty("type").GetString());
        Assert.Equal("helper", diagnostic.RootElement.GetProperty("category").GetString());
        Assert.Contains("view setup", diagnostic.RootElement.GetProperty("next_command").GetString(), StringComparison.Ordinal);

        using var error = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.View.Error, error.RootElement.GetProperty("type").GetString());
        Assert.Equal("configuration_error", error.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("LUOTSI_VIEW_HELPER_APK", error.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);

        using var ended = JsonDocument.Parse(console.OutputLines[2]);
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
        var session = CreateViewSession(
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
