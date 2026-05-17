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
