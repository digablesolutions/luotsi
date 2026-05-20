using System.Text.Json;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Processes;
using Luotsi.Cli.Models;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task RunAsync_Without_Command_Writes_Help_And_Returns_Usage_Exit_Code()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync([]);

        Assert.Equal(2, exitCode);
        Assert.Empty(console.OutputLines);
        Assert.Single(console.ErrorLines);
        Assert.Equal(Help.Text, console.ErrorLines[0]);
    }

    [Fact]
    public async Task RunAsync_Help_Flag_Writes_Help_And_Returns_Success()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["--help"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(console.OutputLines);
        Assert.Single(console.ErrorLines);
        Assert.Equal(Help.Text, console.ErrorLines[0]);
    }

    [Fact]
    public async Task RunAsync_Invalid_Tap_Coordinates_Return_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["tap", "--x", "nope", "--y", "1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(ResultSchemas.CommandEnvelope, envelope.RootElement.GetProperty("schema").GetString());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
    }


    [Fact]
    public async Task RunAsync_Missing_Scenario_File_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });
        var file = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");

        var exitCode = await app.RunAsync(["run", "--file", file]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("does not exist", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }


    [Fact]
    public async Task RunAsync_WaitVisible_Timeout_Returns_Timeout_Envelope()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("One"), string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("Two"), string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("Three"), string.Empty));
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            AdbClientFactory = new FakeAdbClientFactory(adb),
            Console = console
        });

        var exitCode = await app.RunAsync(["wait-visible", "--text", "Target", "--timeout-sec", "1", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(1, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("selector_or_screen_state", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal("wait-visible", envelope.RootElement.GetProperty("command").GetString());
    }


    [Fact]
    public async Task RunAsync_Invalid_Poll_Artifacts_Value_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["devices", "--poll-artifacts", "loud"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
    }


    [Fact]
    public async Task RunAsync_Invalid_Telemetry_Tail_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["telemetry-tail", "--tail", "0"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
    }


    [Fact]
    public async Task RunAsync_WaitLog_Returns_Matched_Line_And_Writes_Artifacts()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        adb.EnqueueLogLines("I/Test: boot", "I/Test: DEVICE_READY", "I/Test: idle");
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            AdbClientFactory = new FakeAdbClientFactory(adb),
            Console = console
        });

        var exitCode = await app.RunAsync(["wait-log", "--contains", "device_ready", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("I/Test: DEVICE_READY", envelope.RootElement.GetProperty("data").GetProperty("matched_line").GetString());
        Assert.Contains(adb.LogRequests, request => request.ContainsText == "device_ready");
        var artifactRoot = envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();
        Assert.NotNull(artifactRoot);
        Assert.True(fileSystem.FileExists(Path.Join(artifactRoot, "wait-log.txt")));
        Assert.True(fileSystem.FileExists(Path.Join(artifactRoot, "wait-log.json")));
    }


    [Fact]
    public async Task RunAsync_TelemetryTail_Parses_Events_And_ParseErrors()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        adb.EnqueueRunResult(new ProcessResult(
            0,
            "05-15 12:00:00.000 I/Luotsi: LUOTSI_DEVICE_TELEMETRY {\"schema\":\"luotsi-device-telemetry.v1\",\"seq\":1,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:00Z\",\"event\":\"step\",\"step\":\"STEP_IDLE\"}" + Environment.NewLine +
            "05-15 12:00:00.100 I/Luotsi: LUOTSI_DEVICE_TELEMETRY {bad json}" + Environment.NewLine,
            string.Empty));
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            AdbClientFactory = new FakeAdbClientFactory(adb),
            Console = console
        });

        var exitCode = await app.RunAsync(["telemetry-tail", "--tail", "50", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, envelope.RootElement.GetProperty("data").GetProperty("event_count").GetInt32());
        Assert.Equal(1, envelope.RootElement.GetProperty("data").GetProperty("parse_error_count").GetInt32());
        Assert.Equal("step", envelope.RootElement.GetProperty("data").GetProperty("events")[0].GetProperty("event").GetString());
        Assert.Equal("STEP_IDLE", envelope.RootElement.GetProperty("data").GetProperty("events")[0].GetProperty("step").GetString());
        var artifactRoot = envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();
        Assert.NotNull(artifactRoot);
        Assert.True(fileSystem.FileExists(Path.Join(artifactRoot, "telemetry-tail.txt")));
        Assert.True(fileSystem.FileExists(Path.Join(artifactRoot, "telemetry-tail.json")));
        Assert.Equal(["logcat", "-d", "-v", "brief", "-t", "50"], adb.RunCommands[0]);
    }


    [Fact]
    public async Task RunAsync_TelemetryTail_Accepts_Legacy_Device_Test_Telemetry_Marker()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        adb.EnqueueRunResult(new ProcessResult(
            0,
            "05-15 12:00:00.000 I/VisitLab: DEVICE_TEST_TELEMETRY {\"schema\":\"device-test-telemetry.v1\",\"seq\":1,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:00Z\",\"event\":\"step\",\"step\":\"STEP_IDLE\"}" + Environment.NewLine,
            string.Empty));
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            AdbClientFactory = new FakeAdbClientFactory(adb),
            Console = console
        });

        var exitCode = await app.RunAsync(["telemetry-tail", "--tail", "50", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, envelope.RootElement.GetProperty("data").GetProperty("event_count").GetInt32());
        Assert.Equal(0, envelope.RootElement.GetProperty("data").GetProperty("parse_error_count").GetInt32());
        Assert.Equal("step", envelope.RootElement.GetProperty("data").GetProperty("events")[0].GetProperty("event").GetString());
        Assert.Equal("STEP_IDLE", envelope.RootElement.GetProperty("data").GetProperty("events")[0].GetProperty("step").GetString());
    }


    [Fact]
    public async Task RunAsync_TelemetryWatch_Streams_And_Collects_Events()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var delay = new FakeDelay(timeProvider);
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        adb.EnqueueLogLines("05-15 12:00:03.000 I/Luotsi: LUOTSI_DEVICE_TELEMETRY {\"schema\":\"luotsi-device-telemetry.v1\",\"seq\":2,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:03Z\",\"event\":\"action_ready\",\"step\":\"STEP_IDLE\",\"action\":\"sign_in\"}");
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = delay,
            AdbClientFactory = new FakeAdbClientFactory(adb),
            Console = console
        });

        var exitCode = await app.RunAsync(["telemetry-watch", "--timeout-sec", "3", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, envelope.RootElement.GetProperty("data").GetProperty("event_count").GetInt32());
        Assert.Equal("action_ready", envelope.RootElement.GetProperty("data").GetProperty("events")[0].GetProperty("event").GetString());
        Assert.Equal("sign_in", envelope.RootElement.GetProperty("data").GetProperty("events")[0].GetProperty("action").GetString());
        Assert.Empty(delay.Calls);
        Assert.Empty(adb.RunCommands);
        Assert.Single(adb.StreamingLogRequests);
        Assert.False(adb.StreamingLogRequests[0].HasStopCondition);
        Assert.True(adb.StreamingLogRequests[0].HasLineObserver);
    }


    [Fact]
    public async Task RunAsync_WaitStep_Returns_Matched_Step_And_Writes_Artifacts()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        adb.EnqueueLogLines("05-15 12:00:00.000 I/Luotsi: LUOTSI_DEVICE_TELEMETRY {\"schema\":\"luotsi-device-telemetry.v1\",\"seq\":10,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:00Z\",\"event\":\"step\",\"step\":\"STEP_IDLE\"}");
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            AdbClientFactory = new FakeAdbClientFactory(adb),
            Console = console
        });

        var exitCode = await app.RunAsync(["wait-step", "--step", "idle", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("STEP_IDLE", envelope.RootElement.GetProperty("data").GetProperty("step").GetString());
        var artifactRoot = envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();
        Assert.NotNull(artifactRoot);
        Assert.True(fileSystem.FileExists(Path.Join(artifactRoot, "wait-step.txt")));
        Assert.True(fileSystem.FileExists(Path.Join(artifactRoot, "wait-step.json")));
        Assert.Empty(adb.RunCommands);
        Assert.Single(adb.StreamingLogRequests);
        Assert.True(adb.StreamingLogRequests[0].HasStopCondition);
        Assert.True(adb.StreamingLogRequests[0].HasLineObserver);
    }


    [Fact]
    public async Task RunAsync_WaitActionReady_Returns_Matched_Action_And_Step()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        adb.EnqueueLogLines("05-15 12:00:00.000 I/Luotsi: LUOTSI_DEVICE_TELEMETRY {\"schema\":\"luotsi-device-telemetry.v1\",\"seq\":11,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:00Z\",\"event\":\"action_ready\",\"step\":\"STEP_IDLE\",\"action\":\"sign_in\"}");
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            AdbClientFactory = new FakeAdbClientFactory(adb),
            Console = console
        });

        var exitCode = await app.RunAsync(["wait-action-ready", "--action", "sign_in", "--step", "idle", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("sign_in", envelope.RootElement.GetProperty("data").GetProperty("action").GetString());
        Assert.Equal("STEP_IDLE", envelope.RootElement.GetProperty("data").GetProperty("step").GetString());
        Assert.Empty(adb.RunCommands);
        Assert.Single(adb.StreamingLogRequests);
        Assert.True(adb.StreamingLogRequests[0].HasStopCondition);
        Assert.True(adb.StreamingLogRequests[0].HasLineObserver);
    }


    [Fact]
    public async Task RunAsync_Inspect_Streams_Snapshot_Command_Result_And_Delta()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        console.EnqueueInput(
            "{\"id\":\"1\",\"command\":\"tap_text\",\"text\":\"Sign in\",\"timeout_sec\":5}",
            "{\"id\":\"2\",\"command\":\"exit\"}");
        var host = new FakeDeviceHost(
            CreateScreenState(timeProvider.GetUtcNow(), "Sign in"),
            CreateScreenState(timeProvider.GetUtcNow().AddSeconds(1), "Welcome"));
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["inspect"]);

        Assert.Equal(0, exitCode);
        Assert.True(console.OutputLines.Count >= 5);

        using var sessionStarted = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.Inspect.SessionStarted, sessionStarted.RootElement.GetProperty("type").GetString());

        using var initialSnapshot = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.Inspect.ScreenSnapshot, initialSnapshot.RootElement.GetProperty("type").GetString());
        Assert.Equal("Sign in", initialSnapshot.RootElement.GetProperty("state").GetProperty("elements")[0].GetProperty("text").GetString());

        using var commandResult = JsonDocument.Parse(console.OutputLines[2]);
        Assert.Equal(SessionEventTypes.Inspect.CommandResult, commandResult.RootElement.GetProperty("type").GetString());
        Assert.True(commandResult.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("tap_text", commandResult.RootElement.GetProperty("command").GetString());

        using var delta = JsonDocument.Parse(console.OutputLines[3]);
        Assert.Equal(SessionEventTypes.Inspect.ScreenDelta, delta.RootElement.GetProperty("type").GetString());
        Assert.Equal("Welcome", delta.RootElement.GetProperty("state").GetProperty("elements")[0].GetProperty("text").GetString());
        Assert.Equal(1, delta.RootElement.GetProperty("delta").GetProperty("added_count").GetInt32());

        using var sessionEnded = JsonDocument.Parse(console.OutputLines[4]);
        Assert.Equal(SessionEventTypes.Inspect.SessionEnded, sessionEnded.RootElement.GetProperty("type").GetString());
        Assert.Equal(["Sign in"], host.TapTextRequests);
    }

    [Fact]
    public async Task RunAsync_Inspect_Continues_For_NonUi_Commands_When_Initial_Screen_State_Fails()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        console.EnqueueInput(
            "{\"id\":\"1\",\"command\":\"telemetry_tail\",\"tail\":20}",
            "{\"id\":\"2\",\"command\":\"exit\"}");
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Ignored"))
        {
            ScreenStateException = new ScreenStateUnavailableException("UI hierarchy dump did not contain parseable XML.")
        };
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["inspect"]);

        Assert.Equal(0, exitCode);

        using var sessionStarted = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.Inspect.SessionStarted, sessionStarted.RootElement.GetProperty("type").GetString());

        using var sessionError = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.Inspect.SessionError, sessionError.RootElement.GetProperty("type").GetString());
        Assert.Equal("screen_state_unavailable", sessionError.RootElement.GetProperty("error").GetProperty("category").GetString());

        using var commandResult = JsonDocument.Parse(console.OutputLines[2]);
        Assert.Equal(SessionEventTypes.Inspect.CommandResult, commandResult.RootElement.GetProperty("type").GetString());
        Assert.True(commandResult.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("telemetry_tail", commandResult.RootElement.GetProperty("command").GetString());

        using var sessionEnded = JsonDocument.Parse(console.OutputLines[3]);
        Assert.Equal(SessionEventTypes.Inspect.SessionEnded, sessionEnded.RootElement.GetProperty("type").GetString());
    }


    [Fact]
    public async Task RunAsync_WaitLog_Uses_Logcat_Failure_Instead_Of_Timeout()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        adb.EnqueueLogResult(new AdbLogStreamResult("ready", string.Empty, null, 0, 15, timeProvider.GetUtcNow(), "adb logcat", 1, "device offline"));
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            AdbClientFactory = new FakeAdbClientFactory(adb),
            Console = console
        });

        var exitCode = await app.RunAsync(["wait-log", "--contains", "ready", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(1, exitCode);
        Assert.Equal("configuration_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("device offline", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }


}
