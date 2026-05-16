using System.Text.Json;
using VisitLab.Cli;
using VisitLab.Cli.Artifacts;
using VisitLab.Cli.Cli;
using VisitLab.Cli.Errors;
using VisitLab.Cli.Hosts.Android;
using VisitLab.Cli.Infrastructure;
using VisitLab.Cli.Models;
using VisitLab.Cli.Scenarios;
using VisitLab.Cli.Telemetry;
using VisitLab.Cli.View;
using Xunit;

namespace VisitLab.Cli.Tests;

public sealed class AppTests
{
    private static readonly JsonSerializerOptions TestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void Parse_Allows_Global_Options_Before_Command()
    {
        var options = CliOptions.Parse(["--device", "abc", "devices"]);

        Assert.Equal("devices", options.Command);
        Assert.Equal("abc", options.Get("device"));
    }

    [Fact]
    public void Parse_Allows_Global_Options_Before_View_Command()
    {
        var options = CliOptions.Parse(["--device", "abc", "view"]);

        Assert.Equal("view", options.Command);
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
        Assert.Equal("visit-lab-command.v1", envelope.RootElement.GetProperty("schema").GetString());
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
        var (fileName, args) = OperatingSystem.IsWindows()
            ? ("powershell.exe", ["-NoLogo", "-NoProfile", "-Command", "[Console]::Out.Write('ok')"])
            : ("/bin/sh", new[] { "-c", "printf 'ok'" });
        var result = await new DefaultProcessRunner().RunAsync(fileName, args);

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
        var envelope = SerializeToJsonElement(result);

        Assert.Equal("basic", envelope.GetProperty("scenario").GetString());
        Assert.Equal("passed", envelope.GetProperty("status").GetString());
        Assert.Equal(0, envelope.GetProperty("timing").GetProperty("prologue_ms").GetInt32());
        Assert.Equal(250, envelope.GetProperty("timing").GetProperty("steps_ms").GetInt32());
        Assert.Equal(2, envelope.GetProperty("steps").GetArrayLength());
        Assert.Equal("sleep", envelope.GetProperty("steps")[0].GetProperty("action").GetString());
                Assert.Equal(250, envelope.GetProperty("steps")[0].GetProperty("timing").GetProperty("harness_delay_ms").GetInt32());
                Assert.Equal(250, envelope.GetProperty("steps")[0].GetProperty("timing").GetProperty("configured_delay_ms").GetInt32());
                Assert.Equal(0, envelope.GetProperty("steps")[0].GetProperty("timing").GetProperty("non_delay_ms").GetInt32());
        Assert.Equal("keyevent", envelope.GetProperty("steps")[1].GetProperty("action").GetString());
                Assert.Equal(0, envelope.GetProperty("steps")[1].GetProperty("timing").GetProperty("harness_delay_ms").GetInt32());
    }

        [Fact]
        public async Task RunScenarioAsync_TapPoint_Timing_Reports_PostTap_Delay()
        {
                var fileSystem = new FakeFileSystem();
                var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
                var delay = new FakeDelay(timeProvider);
                var adb = new FakeAdbClient();
                var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, delay, fileSystem);
                var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, delay);
                var scenarioPath = "/tmp/tap-point.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "tap-point-delay",
                    "steps": [
                        { "name": "tap target", "action": "tapPoint", "x": 10, "y": 20, "postTapDelayMs": 150 }
                    ]
                }
                """);

                var result = await scenarios.RunAsync(scenarioPath);
                var json = SerializeToJsonElement(result);

                Assert.Equal(150, json.GetProperty("steps")[0].GetProperty("timing").GetProperty("harness_delay_ms").GetInt32());
                Assert.Equal(150, json.GetProperty("steps")[0].GetProperty("timing").GetProperty("configured_delay_ms").GetInt32());
                Assert.Equal(150, json.GetProperty("steps")[0].GetProperty("result").GetProperty("post_tap_delay_ms").GetInt32());
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
        public async Task RunScenarioAsync_Empty_Steps_Throws_UsageException()
        {
                var fileSystem = new FakeFileSystem();
                var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
                var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
                var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
                var scenarioPath = "/tmp/empty-steps.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "empty",
                    "steps": []
                }
                """);

                var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

                Assert.Contains("must define at least one step", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RunScenarioAsync_Negative_Sleep_Throws_UsageException()
        {
                var fileSystem = new FakeFileSystem();
                var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
                var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
                var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
                var scenarioPath = "/tmp/negative-sleep.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "broken-sleep",
                    "steps": [
                        { "name": "pause", "action": "sleep", "milliseconds": -1 }
                    ]
                }
                """);

                var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

                Assert.Contains("milliseconds must be zero or greater", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RunScenarioAsync_Missing_Action_Text_Fails_Before_Device_Work()
        {
                var fileSystem = new FakeFileSystem();
                var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
                var adb = new FakeAdbClient();
                var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
                var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
                var scenarioPath = "/tmp/missing-wait-visible-text.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "broken-missing-text",
                    "steps": [
                        { "name": "pause", "action": "sleep", "milliseconds": 10 },
                        { "name": "wait for sign in", "action": "waitVisible", "timeoutSec": 15 }
                    ]
                }
                """);

                var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

                Assert.Contains("waitVisible requires text", error.Message, StringComparison.Ordinal);
                Assert.Empty(adb.ShellCommands);
                Assert.Empty(adb.RunCommands);
        }

        [Fact]
        public async Task RunScenarioAsync_Invalid_AssertEvent_Regex_Fails_Before_Device_Work()
        {
                var fileSystem = new FakeFileSystem();
                var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
                var adb = new FakeAdbClient();
                var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
                var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
                var scenarioPath = "/tmp/invalid-assert-event-regex.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "broken-regex",
                    "steps": [
                        { "name": "printing succeeded", "action": "assertEvent", "event": "PRINTING_SUCCESSFUL", "detailsPattern": "[", "timeoutSec": 15 }
                    ]
                }
                """);

                var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

                Assert.Contains("assertEvent detailsPattern is not a valid regular expression", error.Message, StringComparison.Ordinal);
                Assert.Empty(adb.ShellCommands);
                Assert.Empty(adb.RunCommands);
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
    public async Task GetScreenStateAsync_Uses_ExecOut_UiDump_And_Strips_Prefix_Noise()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["screen-state"]), fileSystem, timeProvider);
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, "UI hierchary dumped to: /dev/tty\n<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>" + CreateUiDump("Target") + "\nUI hierchary dumped to: /dev/tty", string.Empty));
        var runner = new DeviceRunner(adb, artifacts, timeProvider, new FakeDelay(timeProvider), fileSystem);

        var state = await runner.GetScreenStateAsync();

        Assert.Contains(state.Elements, element => element.Text == "Target");
        Assert.Equal(["exec-out", "uiautomator", "dump", "/dev/tty"], adb.RunCommands[0]);
    }

    [Fact]
    public async Task WaitVisibleAsync_Preserves_Per_Attempt_Snapshots_Without_Real_Delay()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var delay = new FakeDelay(timeProvider);
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["wait-visible", "--poll-artifacts", "per-attempt"]), fileSystem, timeProvider);
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
    public async Task WaitVisibleAsync_Writes_Only_Final_Snapshot_By_Default()
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
        Assert.False(fileSystem.FileExists(Path.Combine(artifacts.Root, "wait-visible-001-hierarchy.xml")));
        Assert.False(fileSystem.FileExists(Path.Combine(artifacts.Root, "wait-visible-002-hierarchy.xml")));
        Assert.True(fileSystem.FileExists(Path.Combine(artifacts.Root, "wait-visible-003-hierarchy.xml")));
        Assert.Equal(2, delay.Calls.Count);
    }

    [Fact]
    public async Task RunAsync_Invalid_Poll_Artifacts_Value_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(console: console);

        var exitCode = await app.RunAsync(["devices", "--poll-artifacts", "loud"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task WaitVisibleAsync_Prefers_Exact_Button_Over_Containing_Header()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(
            0,
            CreateUiDumpWithNodes(
                CreateUiNode(text: string.Empty, contentDescription: "Enter your name to sign out", className: "android.view.View", clickable: false, left: 693, top: 210, right: 1227, bottom: 261),
                CreateUiNode(text: string.Empty, contentDescription: "Sign out", className: "android.widget.ImageView", clickable: false, left: 850, top: 453, right: 1070, bottom: 522)),
            string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["wait-visible"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        var element = await runner.WaitVisibleAsync("Sign out", 1);

        Assert.Equal("Sign out", element.ContentDescription);
        Assert.Equal(453, element.Top);
    }

    [Fact]
    public async Task WaitVisibleAsync_Prefers_Result_Row_Over_EditText_Query_Echo()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(
            0,
            CreateUiDumpWithNodes(
                CreateUiNode(text: "Ggg Systam164344", contentDescription: string.Empty, className: "android.widget.EditText", clickable: true, left: 636, top: 270, right: 1284, bottom: 348),
                CreateUiNode(text: string.Empty, contentDescription: "Ggg Systam164344\n15.05.2026 • 13:44 • Host: Perttu Sliden", className: "android.widget.ImageView", clickable: true, left: 636, top: 332, right: 1284, bottom: 452)),
            string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["wait-visible"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        var element = await runner.WaitVisibleAsync("Ggg Systam164344", 1);

        Assert.Contains("Host: Perttu Sliden", element.ContentDescription, StringComparison.Ordinal);
        Assert.Equal("android.widget.ImageView", element.ClassName);
    }

    [Fact]
    public async Task WaitVisibleAsync_Retries_Transient_Invalid_Dumps()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var delay = new FakeDelay(timeProvider);
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, "not-xml", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("Target"), string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["wait-visible"]), fileSystem, timeProvider), timeProvider, delay, fileSystem);

        var element = await runner.WaitVisibleAsync("Target", 2);

        Assert.Equal("Target", element.Text);
        Assert.Single(delay.Calls);
    }

    [Fact]
    public async Task WaitVisibleAsync_Blank_Text_Throws_UsageException()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["wait-visible"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        var error = await Assert.ThrowsAsync<UsageException>(() => runner.WaitVisibleAsync("   ", 2));

        Assert.Contains("waitVisible requires non-empty text", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TapPointAsync_Uses_Cached_Display_Size_ForRepeated_Relative_Taps()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, "Physical size: 1080x1920", string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["tap"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        await runner.TapPointAsync("first", null, null, 0.5, 0.5, 0);
        await runner.TapPointAsync("second", null, null, 0.25, 0.25, 0);

        Assert.Equal(1, adb.ShellCommands.Count(static command => command == "wm size"));
    }

    [Fact]
    public async Task TapTextAsync_Reuses_Recent_UiDump_Across_Adjacent_Reads()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("Target"), string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        var element = await runner.WaitVisibleAsync("Target", 1);
        var tap = await runner.TapTextAsync("Target", 1);

        Assert.Equal("Target", element.Text);
        Assert.Equal(50, tap.X);
        Assert.Single(adb.RunCommands, static command =>
            command.Length == 4 &&
            string.Equals(command[0], "exec-out", StringComparison.Ordinal) &&
            string.Equals(command[1], "uiautomator", StringComparison.Ordinal) &&
            string.Equals(command[2], "dump", StringComparison.Ordinal) &&
            string.Equals(command[3], "/dev/tty", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetScreenStateAsync_Refreshes_UiDump_After_Cache_Ttl_Expires()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("First"), string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("Second"), string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["screen-state"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        var first = await runner.GetScreenStateAsync();
        timeProvider.Advance(TimeSpan.FromMilliseconds(251));
        var second = await runner.GetScreenStateAsync();

        Assert.Contains(first.Elements, element => element.Text == "First");
        Assert.Contains(second.Elements, element => element.Text == "Second");
        Assert.Equal(2, adb.RunCommands.Count(static command =>
            command.Length == 4 &&
            string.Equals(command[0], "exec-out", StringComparison.Ordinal) &&
            string.Equals(command[1], "uiautomator", StringComparison.Ordinal) &&
            string.Equals(command[2], "dump", StringComparison.Ordinal) &&
            string.Equals(command[3], "/dev/tty", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task WaitVisibleAsync_Does_Not_Reuse_UiDump_After_Tap_Invalidates_Cache()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("Target"), string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("Target"), string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["wait-visible"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        await runner.WaitVisibleAsync("Target", 1);
        await runner.TapAsync("10", "20");
        await runner.WaitVisibleAsync("Target", 1);

        Assert.Equal(2, adb.RunCommands.Count(static command =>
            command.Length == 4 &&
            string.Equals(command[0], "exec-out", StringComparison.Ordinal) &&
            string.Equals(command[1], "uiautomator", StringComparison.Ordinal) &&
            string.Equals(command[2], "dump", StringComparison.Ordinal) &&
            string.Equals(command[3], "/dev/tty", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AssertTextInputReadyAsync_Retries_Transient_Invalid_Dumps()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var delay = new FakeDelay(timeProvider);
                var artifacts = ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider);
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, "not-xml", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, """
        <hierarchy>
          <node text="" content-desc="" resource-id="input/name" class="android.widget.EditText" enabled="true" clickable="true" focused="true" bounds="[0,0][100,100]" />
        </hierarchy>
        """, string.Empty));
                var runner = new DeviceRunner(adb, artifacts, timeProvider, delay, fileSystem);

        var result = await runner.AssertTextInputReadyAsync(requireKeyboard: false, timeoutSec: 2);
        var json = SerializeToJsonElement(result);

        Assert.True(json.GetProperty("keyboard_visible").GetBoolean());
        Assert.Single(delay.Calls);
        Assert.True(fileSystem.FileExists(Path.Combine(artifacts.Root, "hierarchy-invalid.xml")));
    }

    [Fact]
    public async Task AssertTextInputReadyAsync_Caches_Keyboard_Visibility_Between_Polls()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var delay = new FakeDelay(timeProvider);
        var adb = new FakeAdbClient();
        const string focusedInputDump = """
        <hierarchy>
          <node text="" content-desc="" resource-id="input/name" class="android.widget.EditText" enabled="true" clickable="true" focused="true" bounds="[0,0][100,100]" />
        </hierarchy>
        """;
        adb.EnqueueShellResult(new ProcessResult(0, focusedInputDump, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, focusedInputDump, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, focusedInputDump, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, focusedInputDump, string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, delay, fileSystem);

        await Assert.ThrowsAsync<TimeoutException>(() => runner.AssertTextInputReadyAsync(requireKeyboard: true, timeoutSec: 1));

        Assert.Equal(2, adb.ShellCommands.Count(static command => command.StartsWith("dumpsys input_method", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RunAsync_Invalid_Telemetry_Tail_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(console: console);

        var exitCode = await app.RunAsync(["telemetry-tail", "--tail", "0"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task AssertEventAsync_Invalid_Regex_Throws_UsageException()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["assert-event"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        var error = await Assert.ThrowsAsync<UsageException>(() => runner.AssertEventAsync("device_ready", [], "[", 2));

        Assert.Contains("detailsPattern is not a valid regular expression", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssertEventAsync_Uses_Streaming_Log_Monitor()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueLogLines(
            "I/flutter (17495): unrelated",
            "I/flutter (17495): Log.PRINTING_SUCCESSFUL: [Main Isolate] Printing successful");
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["assert-event"]), fileSystem, timeProvider);
        var runner = new DeviceRunner(adb, artifacts, timeProvider, new FakeDelay(timeProvider), fileSystem);
                var observedSince = timeProvider.GetUtcNow().AddSeconds(-2);

                var result = await runner.AssertEventAsync("PRINTING_SUCCESSFUL", [], null, 5, observedSince);

        Assert.Equal("I/flutter (17495): Log.PRINTING_SUCCESSFUL: [Main Isolate] Printing successful", result.MatchedLine);
        Assert.Empty(adb.RunCommands);
        Assert.Single(adb.StreamingLogRequests);
                Assert.Equal(observedSince, adb.StreamingLogRequests[0].Since);
        Assert.True(adb.StreamingLogRequests[0].HasStopCondition);
        Assert.False(adb.StreamingLogRequests[0].HasLineObserver);
        Assert.True(fileSystem.FileExists(Path.Combine(artifacts.Root, "assert-event.txt")));
        Assert.True(fileSystem.FileExists(Path.Combine(artifacts.Root, "assert-event.json")));
    }

        [Fact]
        public async Task RunScenarioAsync_AssertEvent_Can_Observe_From_Previous_Step()
        {
                var fileSystem = new FakeFileSystem();
                var initialTime = DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
                var timeProvider = new ManualTimeProvider(initialTime);
                var delay = new FakeDelay(timeProvider);
                var adb = new FakeAdbClient();
                adb.EnqueueShellResult(new ProcessResult(0, CreateDeviceFingerprintShellOutput("SER123", "Pixel 9", "16", "36", "google/pixel/device", "arm64-v8a", "mCurrentFocus=App"), string.Empty));
                adb.EnqueueLogLines("I/flutter (17495): Log.PRINTING_SUCCESSFUL: [Main Isolate] Printing successful");
                var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, delay, fileSystem);
                var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, delay);
                var scenarioPath = "/tmp/assert-event-lookback.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "assert-event-lookback",
                    "steps": [
                        { "name": "pause", "action": "sleep", "milliseconds": 250 },
                        { "name": "printing succeeded", "action": "assertEvent", "event": "PRINTING_SUCCESSFUL", "timeoutSec": 5, "observeFromPreviousStep": true }
                    ]
                }
                """);

                var result = await scenarios.RunAsync(scenarioPath);
                var envelope = SerializeToJsonElement(result);

                Assert.Equal(initialTime, adb.StreamingLogRequests[0].Since);
                Assert.Equal(250, envelope.GetProperty("timing").GetProperty("steps_ms").GetInt32());
                Assert.Equal(0, envelope.GetProperty("steps")[1].GetProperty("timing").GetProperty("harness_delay_ms").GetInt32());
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
        var json = SerializeToJsonElement(result);

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
    public async Task RunAsync_TelemetryWatch_Streams_And_Collects_Events()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var delay = new FakeDelay(timeProvider);
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        adb.EnqueueLogLines("05-15 12:00:03.000 I/Test: DEVICE_TEST_TELEMETRY {\"schema\":\"systam-device-test-telemetry.v1\",\"seq\":2,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:03Z\",\"event\":\"action_ready\",\"step\":\"STEP_IDLE\",\"action\":\"sign_in\"}");
        var app = new App(timeProvider, fileSystem, new DefaultProcessRunner(), delay, new FakeAdbClientFactory(adb), console);

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
        adb.EnqueueLogLines("05-15 12:00:00.000 I/Test: DEVICE_TEST_TELEMETRY {\"schema\":\"systam-device-test-telemetry.v1\",\"seq\":10,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:00Z\",\"event\":\"step\",\"step\":\"STEP_IDLE\"}");
        var app = new App(timeProvider, fileSystem, new DefaultProcessRunner(), new FakeDelay(timeProvider), new FakeAdbClientFactory(adb), console);

        var exitCode = await app.RunAsync(["wait-step", "--step", "idle", "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("STEP_IDLE", envelope.RootElement.GetProperty("data").GetProperty("step").GetString());
        var artifactRoot = envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();
        Assert.NotNull(artifactRoot);
        Assert.True(fileSystem.FileExists(Path.Combine(artifactRoot!, "wait-step.txt")));
        Assert.True(fileSystem.FileExists(Path.Combine(artifactRoot, "wait-step.json")));
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
        adb.EnqueueLogLines("05-15 12:00:00.000 I/Test: DEVICE_TEST_TELEMETRY {\"schema\":\"systam-device-test-telemetry.v1\",\"seq\":11,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:00Z\",\"event\":\"action_ready\",\"step\":\"STEP_IDLE\",\"action\":\"sign_in\"}");
        var app = new App(timeProvider, fileSystem, new DefaultProcessRunner(), new FakeDelay(timeProvider), new FakeAdbClientFactory(adb), console);

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
    public async Task WaitForStepAsync_Uses_Incremental_Telemetry_Parsing()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var parser = new CountingTelemetryParser();
        adb.EnqueueLogLines("05-15 12:00:00.000 I/Test: DEVICE_TEST_TELEMETRY {\"schema\":\"systam-device-test-telemetry.v1\",\"seq\":15,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:00Z\",\"event\":\"step\",\"step\":\"STEP_IDLE\"}");
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["wait-step"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem, telemetryParser: parser);

        var result = await runner.WaitForStepAsync("idle", 5);

        Assert.Equal("STEP_IDLE", result.Step);
        Assert.Equal(0, parser.ParseLogCallCount);
        Assert.Equal(1, parser.ParseLineCallCount);
    }

    [Fact]
    public async Task TelemetryWatchAsync_Uses_Incremental_Telemetry_Parsing()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var parser = new CountingTelemetryParser();
        adb.EnqueueLogLines("05-15 12:00:03.000 I/Test: DEVICE_TEST_TELEMETRY {\"schema\":\"systam-device-test-telemetry.v1\",\"seq\":16,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:03Z\",\"event\":\"action_ready\",\"step\":\"STEP_IDLE\",\"action\":\"sign_in\"}");
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["telemetry-watch"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem, telemetryParser: parser);

        var result = await runner.TelemetryWatchAsync(5);

        Assert.Equal(1, result.EventCount);
        Assert.Equal(0, parser.ParseLogCallCount);
        Assert.Equal(1, parser.ParseLineCallCount);
    }

    [Fact]
    public async Task RunScenarioAsync_WaitStep_Action_Uses_Step_Field()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, CreateDeviceFingerprintShellOutput("SER123", "Pixel 9", "16", "36", "google/pixel/device", "arm64-v8a", "mCurrentFocus=App"), string.Empty));
        adb.EnqueueLogLines("05-15 12:00:00.000 I/Test: DEVICE_TEST_TELEMETRY {\"schema\":\"systam-device-test-telemetry.v1\",\"seq\":12,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:00Z\",\"event\":\"step\",\"step\":\"STEP_IDLE\"}");
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
        var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
        var scenarioPath = "/tmp/wait-step.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "semantic-step",
          "steps": [
            { "name": "wait for idle", "action": "waitStep", "step": "idle", "timeoutSec": 5 }
          ]
        }
        """);

        var result = await scenarios.RunAsync(scenarioPath);
        var json = SerializeToJsonElement(result);

        Assert.Equal("semantic-step", json.GetProperty("scenario").GetString());
        Assert.Equal("passed", json.GetProperty("status").GetString());
        Assert.Equal("waitStep", json.GetProperty("steps")[0].GetProperty("action").GetString());
        Assert.Equal("STEP_IDLE", json.GetProperty("steps")[0].GetProperty("result").GetProperty("step").GetString());
    }

    [Fact]
    public void ScenarioCatalog_Files_Are_Valid_Json()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var scenarioDirectory = Path.Combine(repoRoot, "scenarios");

        Assert.True(Directory.Exists(scenarioDirectory), $"Scenario directory was not found: {scenarioDirectory}");

        var files = Directory.GetFiles(scenarioDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var scenario = JsonSerializer.Deserialize<ScenarioFile>(text, AppJson.Options);

            Assert.NotNull(scenario);
            Assert.False(string.IsNullOrWhiteSpace(scenario!.Name));
            Assert.NotEmpty(scenario.Steps);
        }
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
    public async Task RunAsync_View_Without_Device_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(console: console);

        var exitCode = await app.RunAsync(["view"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("view", envelope.RootElement.GetProperty("command").GetString());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
    }

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

        var exitCode = await session.RunAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", true, "capture.mkv", 1600, 60, "8M", true, false));

        Assert.Equal(0, exitCode);
        Assert.Equal(2, console.OutputLines.Count);

        using var started = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal("view_started", started.RootElement.GetProperty("type").GetString());
        Assert.Equal("192.168.0.134:5555", started.RootElement.GetProperty("device").GetString());
        Assert.Equal("ffmpeg", started.RootElement.GetProperty("decoder").GetString());
        Assert.Equal("h264", started.RootElement.GetProperty("connection").GetProperty("codec").GetString());
        Assert.Equal(1080, started.RootElement.GetProperty("connection").GetProperty("width").GetInt32());
        Assert.True(started.RootElement.GetProperty("headless").GetBoolean());
        Assert.Equal("capture.mkv", started.RootElement.GetProperty("record_path").GetString());
        Assert.True(started.RootElement.GetProperty("overlay_screen_state").GetBoolean());

        using var ended = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal("view_ended", ended.RootElement.GetProperty("type").GetString());
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
        Assert.Contains(console.OutputLines, line => line.Contains("view_started", StringComparison.Ordinal));
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
        Assert.Equal("view_ended", ended.RootElement.GetProperty("type").GetString());
        Assert.Equal("window_closed", ended.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RunAsync_View_Uses_Injected_ViewSessionFactory()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(23);
        var factory = new FakeViewSessionFactory(session);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: factory);

        var exitCode = await app.RunAsync([
            "view",
            "--device", "192.168.0.134:5555",
            "--decoder", "wmf",
            "--headless",
            "--record", "capture.mkv",
            "--max-size", "1280",
            "--max-fps", "30",
            "--video-bit-rate", "12M",
            "--overlay-screen-state",
            "--overlay-telemetry"]);

        Assert.Equal(23, exitCode);
        Assert.Same(host, factory.LastDeviceHost);
        Assert.NotNull(factory.LastArtifacts);
        var options = Assert.Single(session.Options);
        Assert.Equal("192.168.0.134:5555", options.DeviceSelector);
        Assert.Equal("h264", options.Codec);
        Assert.Equal("wmf", options.Decoder);
        Assert.True(options.Headless);
        Assert.Equal("capture.mkv", options.RecordPath);
        Assert.Equal(1280, options.MaxSize);
        Assert.Equal(30, options.MaxFps);
        Assert.Equal("12M", options.VideoBitRate);
        Assert.True(options.OverlayScreenState);
        Assert.True(options.OverlayTelemetry);
    }

    [Fact]
    public async Task PreflightAsync_Writes_Device_Fingerprint_Artifact()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, CreateDeviceFingerprintShellOutput("SER123", "Pixel 9", "16", "36", "google/pixel/device", "arm64-v8a,x86_64", "mCurrentFocus=App"), string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["preflight"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        var result = await runner.PreflightAsync(null);
        var json = SerializeToJsonElement(result);
        var artifactRoot = Path.Combine("/tmp", "visit-lab", "20260515-120000-preflight");

        Assert.Equal("Pixel 9", json.GetProperty("model").GetString());
        Assert.Equal("google/pixel/device", json.GetProperty("fingerprint").GetString());
        Assert.Single(adb.ShellCommands);
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
                adb.EnqueueShellResult(new ProcessResult(0, CreateDeviceFingerprintShellOutput("SER123", "Pixel 9", "16", "36", "google/pixel/device", "arm64-v8a", "mCurrentFocus=App"), string.Empty));
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
        Assert.Equal("visit-lab-failure-bundle.v1", failureArtifacts.GetProperty("schema").GetString());
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
            ["DEVICE_E2E_EMULATED_STORAGE_SOURCE"] = "/mnt/shell/emulated/0"
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

    private static string CreateDeviceFingerprintShellOutput(string serial, string model, string androidRelease, string sdk, string fingerprint, string abi, string currentFocus) =>
        string.Join(
            "\n",
            "__VISIT_LAB_DEVICE_FINGERPRINT__SERIAL__",
            serial,
            "__VISIT_LAB_DEVICE_FINGERPRINT__MODEL__",
            model,
            "__VISIT_LAB_DEVICE_FINGERPRINT__ANDROID_RELEASE__",
            androidRelease,
            "__VISIT_LAB_DEVICE_FINGERPRINT__SDK__",
            sdk,
            "__VISIT_LAB_DEVICE_FINGERPRINT__FINGERPRINT__",
            fingerprint,
            "__VISIT_LAB_DEVICE_FINGERPRINT__ABI__",
            abi,
            "__VISIT_LAB_DEVICE_FINGERPRINT__CURRENT_FOCUS__",
            currentFocus);

    private static JsonElement SerializeToJsonElement<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, TestJsonOptions));
        return document.RootElement.Clone();
    }

    private static string CreateUiDumpWithNodes(params string[] nodes) => $"<hierarchy>{string.Join(string.Empty, nodes)}</hierarchy>";

    private static string CreateUiNode(string text, string contentDescription, string className, bool clickable, int left, int top, int right, int bottom) =>
        $"<node text=\"{text}\" content-desc=\"{contentDescription}\" resource-id=\"\" class=\"{className}\" enabled=\"true\" clickable=\"{clickable.ToString().ToLowerInvariant()}\" bounds=\"[{left},{top}][{right},{bottom}]\" />";

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
        DelayMetrics.RecordDelay(milliseconds);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(milliseconds));
        return Task.CompletedTask;
    }
}

internal sealed class FakeConsole : IConsoleIo
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

    public List<(DateTimeOffset Since, int TimeoutSec, bool HasStopCondition, bool HasLineObserver)> StreamingLogRequests { get; } = [];

    public void EnqueueShellResult(ProcessResult result) => _shellResults.Enqueue(result);

    public void EnqueueRunResult(ProcessResult result) => _runResults.Enqueue(result);

    public void EnqueueLogLines(params string[] lines) => _logLines.Enqueue(lines);

    public void EnqueueLogResult(AdbLogStreamResult result) => _logResults.Enqueue(result);

    public Task<AdbCommandResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        var finalArgs = args.ToArray();
        RunCommands.Add(finalArgs);
                var result = _runResults.Count > 0
                        ? _runResults.Dequeue()
                        : finalArgs.Length == 4 &&
                            string.Equals(finalArgs[0], "exec-out", StringComparison.Ordinal) &&
                            string.Equals(finalArgs[1], "uiautomator", StringComparison.Ordinal) &&
                            string.Equals(finalArgs[2], "dump", StringComparison.Ordinal) &&
                            string.Equals(finalArgs[3], "/dev/tty", StringComparison.Ordinal) &&
                            _shellResults.Count > 0
                                ? _shellResults.Dequeue()
                                : new ProcessResult(0, string.Empty, string.Empty);
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

    public Task<AdbLogStreamResult> MonitorLogAsync(DateTimeOffset since, int timeoutSec, Func<string, bool>? stopWhen = null, Action<string>? observeLine = null, CancellationToken cancellationToken = default)
    {
        StreamingLogRequests.Add((since, timeoutSec, stopWhen is not null, observeLine is not null));
        if (_logResults.Count > 0)
        {
            return Task.FromResult(_logResults.Dequeue());
        }

        var lines = _logLines.Count > 0 ? _logLines.Dequeue() : [];
        var outputLines = new List<string>();
        string? matchedLine = null;

        foreach (var line in lines)
        {
            outputLines.Add(line);
            observeLine?.Invoke(line);
            if (matchedLine is null && stopWhen?.Invoke(line) is true)
            {
                matchedLine = line;
                break;
            }
        }

        var logOutput = string.Join(Environment.NewLine, outputLines);
        if (outputLines.Count > 0)
        {
            logOutput += Environment.NewLine;
        }

        return Task.FromResult(new AdbLogStreamResult(string.Empty, logOutput, matchedLine, outputLines.Count, timeoutSec, since, "adb logcat", 0, string.Empty));
    }
}

internal sealed class CountingTelemetryParser : ITelemetryParser
{
    private readonly DeviceTestTelemetryParser _inner = new();

    public int ParseLogCallCount { get; private set; }

    public int ParseLineCallCount { get; private set; }

    public TelemetryParseResult ParseLog(string logOutput)
    {
        ParseLogCallCount++;
        return _inner.ParseLog(logOutput);
    }

    public TelemetryLineParseResult ParseLine(string line)
    {
        ParseLineCallCount++;
        return _inner.ParseLine(line);
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

    public List<(string? Label, double? XRatio, double? YRatio, int PostTapDelayMs)> TapPointRequests { get; } = [];

    public Task<DeviceListResult> GetDevicesAsync() => Task.FromResult(new DeviceListResult([]));

    public Task<PreflightResult> PreflightAsync(string? packageName) => Task.FromResult(new PreflightResult("Model", "16", "36", "focus", packageName, null, "fingerprint", "arm64-v8a", "SER"));

    public Task<ScreenState> GetScreenStateAsync() =>
        Task.FromResult(_screenStates.Count > 1 ? _screenStates.Dequeue() : _screenStates.Peek());

    public Task<TapResult> TapAsync(string x, string y) => Task.FromResult(new TapResult(int.Parse(x), int.Parse(y)));

    public Task<TelemetryResult> TelemetryTailAsync(int tail) => Task.FromResult(new TelemetryResult(0, 0, 0, 0, [], []));

    public Task<TelemetryResult> TelemetryWatchAsync(int timeoutSec) => Task.FromResult(new TelemetryResult(0, 0, 0, 0, [], []));

    public Task<WaitNotVisibleResult> WaitNotVisibleAsync(string text, int timeoutSec) => Task.FromResult(new WaitNotVisibleResult(text, 1, false));

    public Task<TapPointResult> TapPointAsync(string? label, int? x, int? y, double? xRatio, double? yRatio, int postTapDelayMs)
    {
        TapPointRequests.Add((label, xRatio, yRatio, postTapDelayMs));
        return Task.FromResult(new TapPointResult(label, x ?? 0, y ?? 0, xRatio, yRatio, postTapDelayMs));
    }

    public Task<DoubleTapHeaderLogoResult> DoubleTapHeaderLogoAsync() => Task.FromResult(new DoubleTapHeaderLogoResult("header_logo", 0, 0, 160));

    public Task<TelemetryMatchResult> WaitForStepAsync(string step, int timeoutSec) => Task.FromResult(new TelemetryMatchResult(step, null, string.Empty, "step", default));

    public Task<TelemetryMatchResult> WaitForActionReadyAsync(string action, string? step, int timeoutSec) => Task.FromResult(new TelemetryMatchResult(step, action, string.Empty, "action_ready", default));

    public Task<ResetLogResult> ResetLogAsync() => Task.FromResult(new ResetLogResult(true));

    public Task<AssertEventResult> AssertEventAsync(string name, IReadOnlyList<string> contains, string? detailsPattern, int timeoutSec, DateTimeOffset? since = null) =>
        Task.FromResult(new AssertEventResult(name, contains, detailsPattern, string.Empty));

    public Task<TakeScreenshotResult> TakeScreenshotAsync(string label) => Task.FromResult(new TakeScreenshotResult(label, $"{label}.png"));

    public Task<CaptureArtifactsResult> CaptureArtifactsAsync(string label) => Task.FromResult(new CaptureArtifactsResult(label, $"{label}.png", $"{label}.txt", $"{label}.json", $"{label}.xml"));

    public Task<AssertTextInputReadyResult> AssertTextInputReadyAsync(bool requireKeyboard, int timeoutSec) =>
        Task.FromResult(new AssertTextInputReadyResult(requireKeyboard, true, null, null, null));

    public Task<AssertBelowResult> AssertBelowAsync(string text, string referenceText, int maxGapPx) =>
        Task.FromResult(new AssertBelowResult(text, referenceText, 8, maxGapPx));

    public Task<AssertAlignedResult> AssertAlignedAsync(string text, string referenceText, int maxDeltaPx) =>
        Task.FromResult(new AssertAlignedResult(text, referenceText, 4, maxDeltaPx));

    public Task<AssertAppVersionResult> AssertAppVersionAsync(string? packageName, int maxTopInsetPx, int maxRightInsetPx) =>
        Task.FromResult(new AssertAppVersionResult(packageName ?? string.Empty, "v1.0.0", 0, 0, maxTopInsetPx, maxRightInsetPx));

    public Task<RecordResult> RecordAsync(string output, int timeLimitSec) => Task.FromResult(new RecordResult(output, timeLimitSec));

    public Task<ScreenElement> WaitVisibleAsync(string text, int timeoutSec) =>
        Task.FromResult(new ScreenElement(text, null, $"id/{text}", "android.widget.TextView", true, true, 0, 0, 100, 100));

    public Task<TapResult> TapTextAsync(string text, int timeoutSec)
    {
        TapTextRequests.Add(text);
        return Task.FromResult(new TapResult(50, 50));
    }

    public Task<TypeTextResult> TypeTextAsync(string text) => Task.FromResult(new TypeTextResult(text));

    public Task<TypePinResult> TypePinAsync(string pin, int perDigitDelayMs) => Task.FromResult(new TypePinResult(pin.Length, perDigitDelayMs));

    public Task<KeyEventResult> KeyEventAsync(string code) => Task.FromResult(new KeyEventResult(code));

    public Task<WaitLogResult> WaitForLogAsync(string text, int timeoutSec) => Task.FromResult(new WaitLogResult(text, timeoutSec, text, 1));

    public Task<DeviceFingerprint> WriteDeviceFingerprintAsync() =>
        Task.FromResult(new DeviceFingerprint("device-fingerprint.v1", DateTimeOffset.UtcNow, "SER", "Model", "16", "36", "fingerprint", "arm64-v8a", "focus"));

    public Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception) =>
        Task.FromResult(new FailureArtifactBundle("visit-lab-failure-bundle.v1", DateTimeOffset.UtcNow, request.Scope, request.Name, request.File, request.StepIndex, request.StepName, request.Action, exception.GetType().FullName ?? exception.GetType().Name, exception.Message, [], []));

    public Task<LogcatResult> LogcatAsync(int tail) => Task.FromResult(new LogcatResult([]));
}

internal sealed class FakeViewSession(int exitCode) : IViewSession
{
    public List<ViewOptions> Options { get; } = [];

    public Task<int> RunAsync(ViewOptions options, CancellationToken cancellationToken = default)
    {
        Options.Add(options);
        return Task.FromResult(exitCode);
    }
}

internal sealed class FakeViewSessionFactory(IViewSession viewSession) : IViewSessionFactory
{
    private readonly IViewSession _viewSession = viewSession;

    public IDeviceHost? LastDeviceHost { get; private set; }

    public ArtifactSession? LastArtifacts { get; private set; }

    public IViewSession Create(IDeviceHost deviceHost, ArtifactSession artifacts)
    {
        LastDeviceHost = deviceHost;
        LastArtifacts = artifacts;
        return _viewSession;
    }
}

internal sealed class FakeViewRendererFactory(IViewRenderer renderer) : IViewRendererFactory
{
    private readonly IViewRenderer _renderer = renderer;

    public IViewRenderer? Create(ViewOptions options, IDeviceHost deviceHost) => options.Headless ? null : _renderer;
}

internal sealed class FakeViewTransportBootstrap(ViewConnectionInfo connectionInfo) : IViewTransportBootstrap
{
    private readonly ViewConnectionInfo _connectionInfo = connectionInfo;

    public Task<ViewConnectionInfo> StartAsync(ViewStartRequest request, CancellationToken cancellationToken = default) => Task.FromResult(_connectionInfo);

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeViewBackend(string name = "stub") : IViewBackend
{
    private readonly string _name = name;

    public List<ViewPacket> Packets { get; } = [];

    public string Name => _name;

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default)
    {
        await foreach (var packet in packets.WithCancellation(cancellationToken))
        {
            Packets.Add(packet);
            if (packet.PacketType == ViewPacketType.StreamEnd)
            {
                return;
            }
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class BlockingViewBackend : IViewBackend
{
    public string Name => "blocking";

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default) => Task.Delay(Timeout.Infinite, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ClosingViewRenderer : IViewRenderer
{
    private readonly TaskCompletionSource _closedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task InitializeAsync(ViewDisplayInfo displayInfo, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task PresentAsync(ViewFrame frame, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UpdateStatsAsync(ViewStats stats, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task WaitForCloseAsync(CancellationToken cancellationToken = default) => _closedSource.Task.WaitAsync(cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Close() => _closedSource.TrySetResult();
}

internal sealed class FakeViewBackendFactory : IViewBackendFactory
{
    private readonly IReadOnlyDictionary<string, IViewBackend> _backends;

    public FakeViewBackendFactory(IViewBackend backend)
        : this(new Dictionary<string, IViewBackend>(StringComparer.OrdinalIgnoreCase)
        {
            ["ffmpeg"] = backend
        })
    {
    }

    public FakeViewBackendFactory(IReadOnlyDictionary<string, IViewBackend> backends)
    {
        _backends = backends;
    }

    public List<string> RequestedDecoders { get; } = [];

    public IViewBackend Create(ViewOptions options)
    {
        RequestedDecoders.Add(options.Decoder);
        if (_backends.TryGetValue(options.Decoder, out var backend))
        {
            return backend;
        }

        throw new InvalidOperationException($"No fake backend configured for decoder '{options.Decoder}'.");
    }
}

internal sealed class FakeViewStreamConnector(params Stream[] streams) : IViewStreamConnector
{
    private readonly Queue<Stream> _streams = new(streams);

    public int ConnectCallCount { get; private set; }

    public Task<IViewStreamConnection> ConnectAsync(ViewConnectionInfo connectionInfo, CancellationToken cancellationToken = default)
    {
        ConnectCallCount++;
        if (_streams.Count == 0)
        {
            throw new InvalidOperationException("No fake view streams remain.");
        }

        var stream = _streams.Count > 1 ? _streams.Dequeue() : _streams.Peek();
        return Task.FromResult<IViewStreamConnection>(new FakeViewStreamConnection(stream));
    }
}

internal sealed class FakeViewStreamConnection(Stream stream) : IViewStreamConnection
{
    public Stream Stream { get; } = stream;

    public ValueTask DisposeAsync()
    {
        Stream.Dispose();
        return ValueTask.CompletedTask;
    }
}