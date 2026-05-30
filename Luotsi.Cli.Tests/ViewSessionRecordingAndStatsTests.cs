using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Transport;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task RunAsync_View_With_Record_Path_Creates_Recorder_And_Emits_Started_Event()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var backend = new FakeViewBackend("ffmpeg-native");
        var recorderFactory = new FakeViewRecorderFactory();
        var session = CreateViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(backend),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                    .Build()),
            new ViewPacketStreamReader(),
            viewRecorderFactory: recorderFactory);

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", true, "capture.h264", 1600, 60, "8M", false, false));

        Assert.Equal(0, exitCode);
        Assert.Equal(3, console.OutputLines.Count);
        Assert.NotNull(backend.LastRecorder);
        Assert.NotNull(recorderFactory.LastRecorder);

        using var started = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Started, started.RootElement.GetProperty("type").GetString());
        Assert.Equal("capture.h264", started.RootElement.GetProperty("record_path").GetString());

        using var recordingStarted = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.View.RecordingStarted, recordingStarted.RootElement.GetProperty("type").GetString());
        Assert.Equal("capture.h264", recordingStarted.RootElement.GetProperty("record_path").GetString());

        using var ended = JsonDocument.Parse(console.OutputLines[2]);
        Assert.Equal(SessionEventTypes.View.Ended, ended.RootElement.GetProperty("type").GetString());
        Assert.Equal("stream_ended", ended.RootElement.GetProperty("reason").GetString());
        Assert.True(recorderFactory.LastRecorder!.Disposed);
    }



    [Fact]
    public async Task RunAsync_View_Emits_ViewStats_Jsonl_Events()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = CreateViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new StatsEmittingViewBackend()),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                    .Build()),
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false));

        Assert.Equal(0, exitCode);
        Assert.Equal(3, console.OutputLines.Count);

        using var started = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Started, started.RootElement.GetProperty("type").GetString());

        using var stats = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.View.Stats, stats.RootElement.GetProperty("type").GetString());
        Assert.Equal(12, stats.RootElement.GetProperty("stats").GetProperty("decoded_frames").GetInt32());
        Assert.Equal(11, stats.RootElement.GetProperty("stats").GetProperty("presented_frames").GetInt32());

        using var ended = JsonDocument.Parse(console.OutputLines[2]);
        Assert.Equal(SessionEventTypes.View.Ended, ended.RootElement.GetProperty("type").GetString());
        Assert.Equal("stream_ended", ended.RootElement.GetProperty("reason").GetString());
    }



    [Fact]
    public async Task RunAsync_View_Throttles_ViewStats_Jsonl_Events_And_Flushes_The_Latest_Snapshot_On_End()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = CreateViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new ThrottledStatsViewBackend(timeProvider)),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                    .Build()),
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false));

        Assert.Equal(0, exitCode);
        Assert.Equal(4, console.OutputLines.Count);

        using var started = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Started, started.RootElement.GetProperty("type").GetString());

        using var firstStats = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.View.Stats, firstStats.RootElement.GetProperty("type").GetString());
        Assert.Equal(10, firstStats.RootElement.GetProperty("stats").GetProperty("decoded_frames").GetInt32());

        using var finalStats = JsonDocument.Parse(console.OutputLines[2]);
        Assert.Equal(SessionEventTypes.View.Stats, finalStats.RootElement.GetProperty("type").GetString());
        Assert.Equal(12, finalStats.RootElement.GetProperty("stats").GetProperty("decoded_frames").GetInt32());
        Assert.Equal(11, finalStats.RootElement.GetProperty("stats").GetProperty("presented_frames").GetInt32());

        using var ended = JsonDocument.Parse(console.OutputLines[3]);
        Assert.Equal(SessionEventTypes.View.Ended, ended.RootElement.GetProperty("type").GetString());
        Assert.Equal("stream_ended", ended.RootElement.GetProperty("reason").GetString());
    }



    [Fact]
    public async Task RunAsync_View_Uses_Configured_ViewStats_Interval()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = CreateViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new ThrottledStatsViewBackend(timeProvider)),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                    .Build()),
            new ViewPacketStreamReader());

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false, 100));

        Assert.Equal(0, exitCode);
        Assert.Equal(5, console.OutputLines.Count);

        using var started = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Started, started.RootElement.GetProperty("type").GetString());
        Assert.Equal(100, started.RootElement.GetProperty("stats_interval_ms").GetInt32());

        using var firstStats = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(10, firstStats.RootElement.GetProperty("stats").GetProperty("decoded_frames").GetInt32());

        using var secondStats = JsonDocument.Parse(console.OutputLines[2]);
        Assert.Equal(11, secondStats.RootElement.GetProperty("stats").GetProperty("decoded_frames").GetInt32());

        using var thirdStats = JsonDocument.Parse(console.OutputLines[3]);
        Assert.Equal(12, thirdStats.RootElement.GetProperty("stats").GetProperty("decoded_frames").GetInt32());

        using var ended = JsonDocument.Parse(console.OutputLines[4]);
        Assert.Equal(SessionEventTypes.View.Ended, ended.RootElement.GetProperty("type").GetString());
    }



    [Fact]
    public async Task RunAsync_View_Can_Throttle_Renderer_Stats_Separately_From_Jsonl_Stats()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var renderer = new StatsCapturingViewRenderer();
        var session = CreateViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new ThrottledStatsViewBackend(timeProvider)),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                    .Build()),
            new ViewPacketStreamReader(),
            new FakeViewRendererFactory(renderer));

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false, 250, 100));

        Assert.Equal(0, exitCode);
        Assert.Equal(4, console.OutputLines.Count);

        using var started = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Started, started.RootElement.GetProperty("type").GetString());
        Assert.Equal(250, started.RootElement.GetProperty("stats_interval_ms").GetInt32());
        Assert.Equal(100, started.RootElement.GetProperty("renderer_stats_interval_ms").GetInt32());

        using var firstStats = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(10, firstStats.RootElement.GetProperty("stats").GetProperty("decoded_frames").GetInt32());

        using var finalStats = JsonDocument.Parse(console.OutputLines[2]);
        Assert.Equal(12, finalStats.RootElement.GetProperty("stats").GetProperty("decoded_frames").GetInt32());

        using var ended = JsonDocument.Parse(console.OutputLines[3]);
        Assert.Equal(SessionEventTypes.View.Ended, ended.RootElement.GetProperty("type").GetString());

        Assert.Equal([10, 11, 12], renderer.StatsUpdates.Select(static stats => stats.DecodedFrames).ToArray());
    }



    [Fact]
    public async Task RunAsync_View_With_Zero_Stats_Interval_Disables_Jsonl_Stats_And_Still_Forwards_Renderer_Updates()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var renderer = new StatsCapturingViewRenderer();
        var session = CreateViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new StatsEmittingViewBackend()),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.StreamEnd, 1, 0, false, [])
                    .Build()),
            new ViewPacketStreamReader(),
            new FakeViewRendererFactory(renderer));

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false, 0));

        Assert.Equal(0, exitCode);
        Assert.Equal(2, console.OutputLines.Count);

        using var started = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.View.Started, started.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, started.RootElement.GetProperty("stats_interval_ms").GetInt32());

        using var ended = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.View.Ended, ended.RootElement.GetProperty("type").GetString());
        Assert.Equal("stream_ended", ended.RootElement.GetProperty("reason").GetString());

        Assert.Equal(new ViewStats(12, 11, 1, 59.9d, 58.7d, 84), renderer.LastStats);
    }



    [Fact]
    public async Task RunAsync_View_InteractionHandler_Toggles_Recording_And_Emits_Events()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var renderer = new ClosingViewRenderer();
        var rendererFactory = new FakeViewRendererFactory(renderer);
        var recorderFactory = new FakeViewRecorderFactory();
        var session = CreateViewSession(
            host,
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new BlockingViewBackend()),
            new FakeViewStreamConnector(new ViewPacketStreamHarness().WriteHeader("h264", 1080, 1920).Build()),
            new ViewPacketStreamReader(),
            rendererFactory,
            recorderFactory);

        var runTask = session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false));
        var interactionHandler = await ViewTestWaitHelpers.WaitForInteractionHandlerAsync(rendererFactory);
        await interactionHandler(new ViewWindowCommandRequest(ViewWindowCommand.ToggleRecording));
        await interactionHandler(new ViewWindowCommandRequest(ViewWindowCommand.ToggleRecording));
        renderer.Close();
        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.NotNull(recorderFactory.LastRecorder);
        Assert.True(recorderFactory.LastRecorder!.Disposed);
        Assert.Contains(console.OutputLines, line => line.Contains(SessionEventTypes.View.RecordingStarted, StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, line => line.Contains(SessionEventTypes.View.RecordingStopped, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_View_Reconnect_Stops_Active_Recording_With_Reconnect_Reason()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var renderer = new ClosingViewRenderer();
        var rendererFactory = new FakeViewRendererFactory(renderer);
        var recorderFactory = new FakeViewRecorderFactory();
        var bootstrap = new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward"));
        var session = CreateViewSession(
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
            rendererFactory,
            recorderFactory);

        var runTask = session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", false, "capture.h264", 1600, 60, "8M", false, false));
        var interactionHandler = await ViewTestWaitHelpers.WaitForInteractionHandlerAsync(rendererFactory);
        await ViewTestWaitHelpers.WaitForOutputLineAsync(console, SessionEventTypes.View.RecordingStarted);
        await interactionHandler(new ViewWindowCommandRequest(ViewWindowCommand.Reconnect));
        await ViewTestWaitHelpers.WaitForStartCallsAsync(bootstrap, 2);
        await ViewTestWaitHelpers.WaitForOutputLineAsync(console, SessionEventTypes.View.RecordingStopped);
        await ViewTestWaitHelpers.WaitForOutputLineCountAsync(console, SessionEventTypes.View.RecordingStarted, 2);
        renderer.Close();
        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.NotNull(recorderFactory.LastRecorder);
        Assert.True(recorderFactory.LastRecorder!.Disposed);

        var stoppedLine = Assert.Single(console.OutputLines, line => line.Contains(SessionEventTypes.View.RecordingStopped, StringComparison.Ordinal));
        using var stopped = JsonDocument.Parse(stoppedLine);
        Assert.Equal("reconnect", stopped.RootElement.GetProperty("reason").GetString());
        Assert.Equal("capture.h264", stopped.RootElement.GetProperty("record_path").GetString());

        var startedLines = console.OutputLines
            .Where(line => line.Contains(SessionEventTypes.View.RecordingStarted, StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, startedLines.Length);

        using var initialStarted = JsonDocument.Parse(startedLines[0]);
        Assert.Equal("startup", initialStarted.RootElement.GetProperty("source").GetString());
        Assert.Equal("capture.h264", initialStarted.RootElement.GetProperty("record_path").GetString());

        using var resumedStarted = JsonDocument.Parse(startedLines[1]);
        Assert.Equal("reconnect", resumedStarted.RootElement.GetProperty("source").GetString());
        Assert.Equal("capture-001.h264", Path.GetFileName(resumedStarted.RootElement.GetProperty("record_path").GetString()));
    }



}
