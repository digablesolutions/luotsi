using System.Text.Json;
using DeviceE2ELab.Cli;
using Xunit;

namespace DeviceE2ELab.Cli.Tests;

public sealed class AppTests
{
    [Fact]
    public void Parse_Allows_Global_Options_Before_Command()
    {
        var options = CliOptions.Parse(["--device", "abc", "devices"]);

        Assert.Equal("devices", options.Command);
        Assert.Equal("abc", options.Get("device"));
    }

    [Fact]
    public async Task RunAsync_Invalid_Tap_Coordinates_Return_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(console: console);

        var exitCode = await app.RunAsync(["tap", "--x", "nope", "--y", "1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("device-e2e-lab-command.v1", envelope.RootElement.GetProperty("schema").GetString());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task RunAsync_Missing_Scenario_File_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(console: console);
        var file = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");

        var exitCode = await app.RunAsync(["run", "--file", file]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("does not exist", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessRunner_Captures_Stdout_And_Exit_Code()
    {
        var result = await new DefaultProcessRunner().RunAsync("/bin/sh", ["-c", "printf 'ok'"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ok", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
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
        var app = new App(timeProvider, fileSystem, new DefaultProcessRunner(), new FakeDelay(timeProvider), new FakeAdbClientFactory(adb), console);

        var exitCode = await app.RunAsync(["wait-visible", "--text", "Target", "--timeout-sec", "1", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(1, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("selector_or_screen_state", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal("wait-visible", envelope.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public async Task RunScenarioAsync_Parses_Valid_Steps_And_Returns_Passed_Status()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
        var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
        var scenarioPath = "/tmp/scenario.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "basic",
          "steps": [
            { "name": "pause", "action": "sleep", "milliseconds": 250 },
            { "name": "press back", "action": "keyevent", "code": "KEYCODE_BACK" }
          ]
        }
        """);

        var result = await scenarios.RunAsync(scenarioPath);
        var json = JsonSerializer.Serialize(result);
        var envelope = JsonDocument.Parse(json).RootElement;

        Assert.Equal("basic", envelope.GetProperty("scenario").GetString());
        Assert.Equal("passed", envelope.GetProperty("status").GetString());
        Assert.Equal(2, envelope.GetProperty("steps").GetArrayLength());
        Assert.Equal("sleep", envelope.GetProperty("steps")[0].GetProperty("action").GetString());
        Assert.Equal("keyevent", envelope.GetProperty("steps")[1].GetProperty("action").GetString());
    }

    [Fact]
    public async Task RunScenarioAsync_Unknown_Action_Throws_UsageException()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
        var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
        var scenarioPath = "/tmp/unknown-action.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "broken",
          "steps": [
            { "action": "launchApp" }
          ]
        }
        """);

        var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

        Assert.Contains("Unknown scenario action 'launchApp'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunScenarioAsync_Corrupted_NonEmpty_Json_Throws_UsageException()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
        var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
        var scenarioPath = "/tmp/corrupted.json";
        fileSystem.AddFile(scenarioPath, "{ \"name\": \"broken\", \"steps\": [ ");

        var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

        Assert.Contains("not valid JSON", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetScreenStateAsync_Writes_Invalid_Dump_Artifact_On_Parse_Failure()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["screen-state"]), fileSystem, timeProvider);
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, "not-xml", string.Empty));
        var runner = new DeviceRunner(adb, artifacts, timeProvider, new FakeDelay(timeProvider), fileSystem);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.GetScreenStateAsync());

        Assert.Contains("invalid XML", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(fileSystem.FileExists(Path.Combine(artifacts.Root, "hierarchy.xml")));
        Assert.True(fileSystem.FileExists(Path.Combine(artifacts.Root, "hierarchy-invalid.xml")));
    }

    [Fact]
    public async Task WaitVisibleAsync_Preserves_Per_Attempt_Snapshots_Without_Real_Delay()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var delay = new FakeDelay(timeProvider);
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["wait-visible"]), fileSystem, timeProvider);
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("First"), string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("Second"), string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("Target"), string.Empty));
        var runner = new DeviceRunner(adb, artifacts, timeProvider, delay, fileSystem);

        var element = await runner.WaitVisibleAsync("Target", 2);

        Assert.Equal("Target", element.Text);
        Assert.True(fileSystem.FileExists(Path.Combine(artifacts.Root, "wait-visible-001-hierarchy.xml")));
        Assert.True(fileSystem.FileExists(Path.Combine(artifacts.Root, "wait-visible-002-hierarchy.xml")));
        Assert.True(fileSystem.FileExists(Path.Combine(artifacts.Root, "wait-visible-003-hierarchy.xml")));
        Assert.Equal(2, delay.Calls.Count);
    }

    [Fact]
    public async Task RecordAsync_Uses_Injected_Id_And_Cleans_Up_Remote_File()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var idGenerator = new FakeUniqueIdGenerator("fixed-recording-id");
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["record"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem, idGenerator);

        var result = await runner.RecordAsync("capture.mp4", 999);
        var json = JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement;

        Assert.Equal("capture.mp4", json.GetProperty("output").GetString());
        Assert.Equal(180, json.GetProperty("time_limit_sec").GetInt32());
        Assert.Contains("screenrecord --time-limit 180 /sdcard/device-e2e-fixed-recording-id.mp4", adb.ShellCommands[0], StringComparison.Ordinal);
        Assert.Equal(["pull", "/sdcard/device-e2e-fixed-recording-id.mp4", "capture.mp4"], adb.RunCommands[0]);
        Assert.Contains("rm -f /sdcard/device-e2e-fixed-recording-id.mp4", adb.ShellCommands[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WaitLog_Returns_Matched_Line_And_Writes_Artifacts()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        adb.EnqueueLogLines("I/Test: boot", "I/Test: DEVICE_READY", "I/Test: idle");
        var app = new App(timeProvider, fileSystem, new DefaultProcessRunner(), new FakeDelay(timeProvider), new FakeAdbClientFactory(adb), console);

        var exitCode = await app.RunAsync(["wait-log", "--contains", "device_ready", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("I/Test: DEVICE_READY", envelope.RootElement.GetProperty("data").GetProperty("matched_line").GetString());
        Assert.Contains(adb.LogRequests, request => request.ContainsText == "device_ready");
        var artifactRoot = envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();
        Assert.NotNull(artifactRoot);
        Assert.True(fileSystem.FileExists(Path.Combine(artifactRoot!, "wait-log.txt")));
        Assert.True(fileSystem.FileExists(Path.Combine(artifactRoot, "wait-log.json")));
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
            "05-15 12:00:00.000 I/Test: DEVICE_TEST_TELEMETRY {\"schema\":\"systam-device-test-telemetry.v1\",\"seq\":1,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:00Z\",\"event\":\"step\",\"step\":\"STEP_IDLE\"}" + Environment.NewLine +
            "05-15 12:00:00.100 I/Test: DEVICE_TEST_TELEMETRY {bad json}" + Environment.NewLine,
            string.Empty));
        var app = new App(timeProvider, fileSystem, new DefaultProcessRunner(), new FakeDelay(timeProvider), new FakeAdbClientFactory(adb), console);

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
        Assert.True(fileSystem.FileExists(Path.Combine(artifactRoot!, "telemetry-tail.txt")));
        Assert.True(fileSystem.FileExists(Path.Combine(artifactRoot, "telemetry-tail.json")));
        Assert.Equal(["logcat", "-d", "-v", "brief", "-t", "50"], adb.RunCommands[0]);
    }

    [Fact]
    public async Task RunAsync_TelemetryWatch_Waits_And_Collects_Events()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var delay = new FakeDelay(timeProvider);
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        adb.EnqueueRunResult(new ProcessResult(
            0,
            "05-15 12:00:03.000 I/Test: DEVICE_TEST_TELEMETRY {\"schema\":\"systam-device-test-telemetry.v1\",\"seq\":2,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:03Z\",\"event\":\"action_ready\",\"step\":\"STEP_IDLE\",\"action\":\"sign_in\"}" + Environment.NewLine,
            string.Empty));
        var app = new App(timeProvider, fileSystem, new DefaultProcessRunner(), delay, new FakeAdbClientFactory(adb), console);

        var exitCode = await app.RunAsync(["telemetry-watch", "--timeout-sec", "3", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, envelope.RootElement.GetProperty("data").GetProperty("event_count").GetInt32());
        Assert.Equal("action_ready", envelope.RootElement.GetProperty("data").GetProperty("events")[0].GetProperty("event").GetString());
        Assert.Equal("sign_in", envelope.RootElement.GetProperty("data").GetProperty("events")[0].GetProperty("action").GetString());
        Assert.Equal([3000], delay.Calls);
        Assert.Equal("logcat", adb.RunCommands[0][0]);
        Assert.Contains("-T", adb.RunCommands[0]);
        Assert.Contains("*:V", adb.RunCommands[0]);
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
        var app = new App(console: console, timeProvider: timeProvider, deviceHostFactory: new FakeDeviceHostFactory(host));

        var exitCode = await app.RunAsync(["inspect"]);

        Assert.Equal(0, exitCode);
        Assert.True(console.OutputLines.Count >= 5);

        using var sessionStarted = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal("session_started", sessionStarted.RootElement.GetProperty("type").GetString());

        using var initialSnapshot = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal("screen_snapshot", initialSnapshot.RootElement.GetProperty("type").GetString());
        Assert.Equal("Sign in", initialSnapshot.RootElement.GetProperty("state").GetProperty("elements")[0].GetProperty("text").GetString());

        using var commandResult = JsonDocument.Parse(console.OutputLines[2]);
        Assert.Equal("command_result", commandResult.RootElement.GetProperty("type").GetString());
        Assert.True(commandResult.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("tap_text", commandResult.RootElement.GetProperty("command").GetString());

        using var delta = JsonDocument.Parse(console.OutputLines[3]);
        Assert.Equal("screen_delta", delta.RootElement.GetProperty("type").GetString());
        Assert.Equal("Welcome", delta.RootElement.GetProperty("state").GetProperty("elements")[0].GetProperty("text").GetString());
        Assert.Equal(1, delta.RootElement.GetProperty("delta").GetProperty("added_count").GetInt32());

        using var sessionEnded = JsonDocument.Parse(console.OutputLines[4]);
        Assert.Equal("session_ended", sessionEnded.RootElement.GetProperty("type").GetString());
        Assert.Equal(["Sign in"], host.TapTextRequests);
    }

    [Fact]
    public async Task PreflightAsync_Writes_Device_Fingerprint_Artifact()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, "SER123", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "Pixel 9", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "16", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "36", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "google/pixel/device", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "arm64-v8a,x86_64", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "mCurrentFocus=App", string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["preflight"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        var result = await runner.PreflightAsync(null);
        var json = JsonDocument.Parse(JsonSerializer.Serialize(result)).RootElement;
        var artifactRoot = Path.Combine("/tmp/device-e2e-lab", "20260515-120000-preflight");

        Assert.Equal("Pixel 9", json.GetProperty("model").GetString());
        Assert.Equal("google/pixel/device", json.GetProperty("fingerprint").GetString());
        Assert.True(fileSystem.FileExists(Path.Combine(artifactRoot, "device-fingerprint.json")));
    }

    [Fact]
    public async Task RunAsync_Scenario_LogWait_Timeout_Captures_Failure_Bundle()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        var scenarioPath = "/tmp/log-timeout.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "log-timeout",
          "steps": [
            { "name": "wait for ready marker", "action": "waitLog", "text": "READY", "timeoutSec": 2 }
          ]
        }
        """);
        adb.EnqueueShellResult(new ProcessResult(0, "SER123", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "Pixel 9", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "16", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "36", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "google/pixel/device", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "arm64-v8a", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "mCurrentFocus=App", string.Empty));
        adb.EnqueueLogLines("I/Test: boot", "I/Test: still waiting");
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "01-01 00:00:00.000 I/Test: snapshot", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("Failure"), string.Empty));
        var app = new App(timeProvider, fileSystem, new DefaultProcessRunner(), new FakeDelay(timeProvider), new FakeAdbClientFactory(adb), console);

        var exitCode = await app.RunAsync(["run", "--file", scenarioPath, "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(1, exitCode);
        Assert.Equal("log_wait_timeout", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal("failed", envelope.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("wait for ready marker", envelope.RootElement.GetProperty("data").GetProperty("failed_step").GetProperty("name").GetString());
        var failureArtifacts = envelope.RootElement.GetProperty("data").GetProperty("failure_artifacts");
        Assert.Equal("device-e2e-lab-failure-bundle.v1", failureArtifacts.GetProperty("schema").GetString());
        Assert.True(failureArtifacts.GetProperty("artifacts").GetArrayLength() >= 2);
    }

    [Fact]
    public async Task RecordAsync_Normalizes_Device_Path_For_Pull_When_Configured()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var idGenerator = new FakeUniqueIdGenerator("fixed-recording-id");
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["DEVICE_E2E_EMULATED_STORAGE_TARGET"] = "/sdcard",
            ["DEVICE_E2E_EMULATED_STORAGE_SOURCE"] = "/mnt/shell/emulated/0",
        });
        var runner = new DeviceRunner(
            adb,
            ArtifactSession.Create(CliOptions.Parse(["record"]), fileSystem, timeProvider),
            timeProvider,
            new FakeDelay(timeProvider),
            fileSystem,
            idGenerator,
            environment);

        await runner.RecordAsync("capture.mp4", 30);

        Assert.Equal(["pull", "/mnt/shell/emulated/0/device-e2e-fixed-recording-id.mp4", "capture.mp4"], adb.RunCommands[0]);
    }

    [Fact]
    public async Task RunAsync_WaitLog_Uses_Logcat_Failure_Instead_Of_Timeout()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        adb.EnqueueLogResult(new AdbLogStreamResult("ready", string.Empty, null, 0, 15, timeProvider.GetUtcNow(), "adb logcat", 1, "device offline"));
        var app = new App(timeProvider, fileSystem, new DefaultProcessRunner(), new FakeDelay(timeProvider), new FakeAdbClientFactory(adb), console);

        var exitCode = await app.RunAsync(["wait-log", "--contains", "ready", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(1, exitCode);
        Assert.Equal("configuration_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("device offline", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private static string CreateUiDump(string text) =>
        $"<hierarchy><node text=\"{text}\" content-desc=\"\" resource-id=\"id/{text}\" class=\"android.widget.TextView\" enabled=\"true\" clickable=\"false\" bounds=\"[0,0][100,100]\" /></hierarchy>";

    private static ScreenState CreateScreenState(DateTimeOffset capturedAt, string text) =>
        new(capturedAt, 1, [new ScreenElement(text, null, $"id/{text}", "android.widget.TextView", true, true, 0, 0, 100, 100)]);
}

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}

internal sealed class FakeDelay(ManualTimeProvider timeProvider) : IDelay
{
    private readonly ManualTimeProvider _timeProvider = timeProvider;

    public List<int> Calls { get; } = [];

    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
        Calls.Add(milliseconds);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(milliseconds));
        return Task.CompletedTask;
    }
}

internal sealed class FakeConsole : IConsoleIO
{
    public List<string> OutputLines { get; } = [];

    public List<string> ErrorLines { get; } = [];

    private readonly Queue<string?> _inputLines = new();

    public void WriteLine(string value) => OutputLines.Add(value);

    public void WriteErrorLine(string value) => ErrorLines.Add(value);

    public string? ReadLine() => _inputLines.Count > 0 ? _inputLines.Dequeue() : null;

    public void EnqueueInput(params string[] lines)
    {
        foreach (var line in lines)
        {
            _inputLines.Enqueue(line);
        }
    }

    public JsonDocument ParseSingleOutputAsJson()
    {
        Assert.Single(OutputLines);
        return JsonDocument.Parse(OutputLines[0]);
    }
}

internal sealed class FakeUniqueIdGenerator(string value) : IUniqueIdGenerator
{
    private readonly string _value = value;

    public string NewId() => _value;
}

internal sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

    public void AddFile(string path, string content)
    {
        CreateDirectory(Path.GetDirectoryName(path) ?? "/");
        _files[path] = content;
    }

    public void CreateDirectory(string path) => _directories.Add(path);

    public Task WriteAllTextAsync(string path, string text, System.Text.Encoding encoding, CancellationToken cancellationToken = default)
    {
        AddFile(path, text);
        return Task.CompletedTask;
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(_files[path]);

    public bool FileExists(string path) => _files.ContainsKey(path);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        if (!overwrite && _files.ContainsKey(destinationPath))
        {
            throw new IOException($"Destination file '{destinationPath}' exists.");
        }

        AddFile(destinationPath, _files[sourcePath]);
    }

    public string GetTempPath() => "/tmp";
}

internal sealed class FakeAdbClient : IAdbClient
{
    private readonly Queue<ProcessResult> _shellResults = new();
    private readonly Queue<ProcessResult> _runResults = new();
    private readonly Queue<string[]> _logLines = new();
    private readonly Queue<AdbLogStreamResult> _logResults = new();

    public List<string> ShellCommands { get; } = [];

    public List<string[]> RunCommands { get; } = [];

    public List<(string ContainsText, DateTimeOffset Since, int TimeoutSec)> LogRequests { get; } = [];

    public void EnqueueShellResult(ProcessResult result) => _shellResults.Enqueue(result);

    public void EnqueueRunResult(ProcessResult result) => _runResults.Enqueue(result);

    public void EnqueueLogLines(params string[] lines) => _logLines.Enqueue(lines);

    public void EnqueueLogResult(AdbLogStreamResult result) => _logResults.Enqueue(result);

    public Task<AdbCommandResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        var finalArgs = args.ToArray();
        RunCommands.Add(finalArgs);
        var result = _runResults.Count > 0 ? _runResults.Dequeue() : new ProcessResult(0, string.Empty, string.Empty);
        return Task.FromResult(new AdbCommandResult("adb", null, finalArgs, result));
    }

    public Task<AdbCommandResult> ShellAsync(string command, CancellationToken cancellationToken = default)
    {
        ShellCommands.Add(command);
        var result = _shellResults.Count > 0 ? _shellResults.Dequeue() : new ProcessResult(0, string.Empty, string.Empty);
        return Task.FromResult(new AdbCommandResult("adb", null, ["shell", command], result));
    }

    public Task<AdbLogStreamResult> MonitorLogAsync(string containsText, DateTimeOffset since, int timeoutSec, CancellationToken cancellationToken = default)
    {
        LogRequests.Add((containsText, since, timeoutSec));
        if (_logResults.Count > 0)
        {
            return Task.FromResult(_logResults.Dequeue());
        }

        var lines = _logLines.Count > 0 ? _logLines.Dequeue() : [];
        var logOutput = string.Join(Environment.NewLine, lines);
        if (lines.Length > 0)
        {
            logOutput += Environment.NewLine;
        }

        var matchedLine = lines.FirstOrDefault(line => line.Contains(containsText, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(new AdbLogStreamResult(containsText, logOutput, matchedLine, lines.Length, timeoutSec, since, "adb logcat", 0, string.Empty));
    }
}

internal sealed class FakeAdbClientFactory(IAdbClient adbClient) : IAdbClientFactory
{
    private readonly IAdbClient _adbClient = adbClient;

    public IAdbClient Create(string executable, string? serial, IProcessRunner processRunner) => _adbClient;
}

internal sealed class FakeEnvironmentVariables(Dictionary<string, string> variables) : IEnvironmentVariables
{
    private readonly Dictionary<string, string> _variables = variables;

    public string? GetEnvironmentVariable(string variable) =>
        _variables.TryGetValue(variable, out var value) ? value : null;
}

internal sealed class FakeDeviceHostFactory(IDeviceHost deviceHost) : IDeviceHostFactory
{
    private readonly IDeviceHost _deviceHost = deviceHost;

    public IDeviceHost Create(DeviceHostConfiguration configuration, ArtifactSession artifacts) => _deviceHost;
}

internal sealed class FakeDeviceHost(params ScreenState[] screenStates) : IDeviceHost
{
    private readonly Queue<ScreenState> _screenStates = new(screenStates);

    public List<string> TapTextRequests { get; } = [];

    public Task<object> GetDevicesAsync() => Task.FromResult<object>(new { devices = Array.Empty<object>() });

    public Task<object> PreflightAsync(string? packageName) => Task.FromResult<object>(new { package = packageName });

    public Task<ScreenState> GetScreenStateAsync() =>
        Task.FromResult(_screenStates.Count > 1 ? _screenStates.Dequeue() : _screenStates.Peek());

    public Task<object> TapAsync(string x, string y) => Task.FromResult<object>(new { x, y });

    public Task<object> TelemetryTailAsync(int tail) => Task.FromResult<object>(new { tail, event_count = 0 });

    public Task<object> TelemetryWatchAsync(int timeoutSec) => Task.FromResult<object>(new { timeout_sec = timeoutSec, event_count = 0 });

    public Task<object> RecordAsync(string output, int timeLimitSec) => Task.FromResult<object>(new { output, time_limit_sec = timeLimitSec });

    public Task<ScreenElement> WaitVisibleAsync(string text, int timeoutSec) =>
        Task.FromResult(new ScreenElement(text, null, $"id/{text}", "android.widget.TextView", true, true, 0, 0, 100, 100));

    public Task<object> TapTextAsync(string text, int timeoutSec)
    {
        TapTextRequests.Add(text);
        return Task.FromResult<object>(new { text, timeout_sec = timeoutSec });
    }

    public Task<object> TypeTextAsync(string text) => Task.FromResult<object>(new { text });

    public Task<object> KeyEventAsync(string code) => Task.FromResult<object>(new { code });

    public Task<object> WaitForLogAsync(string text, int timeoutSec) => Task.FromResult<object>(new { text, timeout_sec = timeoutSec });

    public Task<DeviceFingerprint> WriteDeviceFingerprintAsync() =>
        Task.FromResult(new DeviceFingerprint("device-fingerprint.v1", DateTimeOffset.UtcNow, "SER", "Model", "16", "36", "fingerprint", "arm64-v8a", "focus"));

    public Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception) =>
        Task.FromResult(new FailureArtifactBundle("device-e2e-lab-failure-bundle.v1", DateTimeOffset.UtcNow, request.Scope, request.Name, request.File, request.StepIndex, request.StepName, request.Action, exception.GetType().FullName ?? exception.GetType().Name, exception.Message, Array.Empty<FailureArtifact>(), Array.Empty<FailureCaptureError>()));

    public Task<object> LogcatAsync(int tail) => Task.FromResult<object>(new { tail });
}