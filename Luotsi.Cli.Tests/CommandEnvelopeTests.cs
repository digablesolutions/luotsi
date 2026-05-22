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
    public async Task RunAsync_Help_Command_Writes_Command_Topic()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["help", "view"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(console.OutputLines);
        Assert.Single(console.ErrorLines);
        Assert.Contains("Luotsi help: view", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("luotsi view --device <adb serial>", console.ErrorLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Version_Flag_Writes_Version_And_Returns_Success()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["--version"]);

        Assert.Equal(0, exitCode);
        var line = Assert.Single(console.OutputLines);
        Assert.StartsWith("luotsi ", line, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown", line, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(console.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_Command_Help_Flag_Writes_Command_Topic()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["run", "--help"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(console.OutputLines);
        Assert.Single(console.ErrorLines);
        Assert.Contains("Luotsi help: run", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("--events-jsonl <file>", console.ErrorLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Unknown_Help_Topic_Returns_Usage_Exit_Code()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["help", "nope"]);

        Assert.Equal(2, exitCode);
        Assert.Empty(console.OutputLines);
        Assert.Single(console.ErrorLines);
        Assert.Contains("Unknown help topic 'nope'", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("view", console.ErrorLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Help_Topics_Cover_All_Known_Commands()
    {
        var missing = CliOptions.KnownCommandNames
            .Where(static command => !string.Equals(command, "help", StringComparison.OrdinalIgnoreCase))
            .Where(static command => !Help.TryGetTopic(command, out _))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(missing);
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
    public async Task LabStatus_Returns_Device_Decisions()
    {
        var console = new FakeConsole();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        host.ConnectedDevices.Add(new DeviceInfo("wifi-1:5555", "offline", "product:p model:Old_Box device:box"));
        host.ForwardEntries.Add(new PortForwardEntry("usb-1", "tcp:37123", "localabstract:luotsi_view_old"));
        host.ReverseEntries.Add(new PortReverseEntry("usb-1", "localabstract:device-e2e-old", "tcp:8080"));
        var app = new App(new AppDependencies
        {
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["lab", "status", "--device-query", "model=Pixel_9"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("available").GetInt32());
        Assert.True(data.GetProperty("decisions")[0].GetProperty("selected").GetBoolean());
        Assert.False(data.GetProperty("decisions")[1].GetProperty("selected").GetBoolean());
    }

    [Fact]
    public async Task LabDoctor_Flags_Ambiguous_And_Offline_Devices()
    {
        var console = new FakeConsole();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        host.ConnectedDevices.Add(new DeviceInfo("usb-2", "device", "product:p model:Pixel_8 device:shiba usb:1-2"));
        host.ConnectedDevices.Add(new DeviceInfo("wifi-1:5555", "offline", "product:p model:Old_Box device:box"));
        var app = new App(new AppDependencies
        {
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["lab", "doctor"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("attention_required", data.GetProperty("status").GetString());
        Assert.Contains("Multiple available devices", data.GetProperty("findings")[1].GetString(), StringComparison.Ordinal);
        Assert.Contains("adb reconnect offline", data.GetProperty("recommended_actions")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LabDoctor_Fix_Reconnects_Offline_Devices_And_Reports_Capabilities()
    {
        var console = new FakeConsole();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        host.ConnectedDevices.Add(new DeviceInfo("wifi-1:5555", "offline", "product:p model:Old_Box device:box"));
        host.ForwardEntries.Add(new PortForwardEntry("usb-1", "tcp:37123", "localabstract:luotsi_view_old"));
        host.ReverseEntries.Add(new PortReverseEntry("usb-1", "localabstract:device-e2e-old", "tcp:8080"));
        var app = new App(new AppDependencies
        {
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["lab", "doctor", "--fix"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal(["offline"], host.AdbReconnectTargets);
        Assert.Equal(["tcp:37123"], host.ForwardRemoveRequests);
        Assert.Equal(["localabstract:device-e2e-old"], host.ReverseRemoveRequests);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Contains("Ran `adb reconnect offline`", data.GetProperty("applied_fixes")[0].GetString(), StringComparison.Ordinal);
        Assert.Contains("Removed stale Luotsi forward", data.GetProperty("applied_fixes")[1].GetString(), StringComparison.Ordinal);
        Assert.Equal(4, data.GetProperty("probes").GetArrayLength());
        Assert.Equal("server-status", data.GetProperty("probes")[0].GetProperty("name").GetString());
        var capabilities = data.GetProperty("inventory").GetProperty("decisions")[0].GetProperty("capabilities").EnumerateArray().Select(static value => value.GetString()).ToArray();
        Assert.Contains("adb", capabilities);
        Assert.Contains("physical", capabilities);
        Assert.Contains("model:Pixel_9", capabilities);
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
            "{\"id\":\"2\",\"command\":\"logcat\",\"tail\":25}",
            "{\"id\":\"3\",\"command\":\"screenshot\",\"label\":\"inspect-shot\"}",
            "{\"id\":\"4\",\"command\":\"record\",\"output\":\"inspect.mp4\",\"time_limit_sec\":3}",
            "{\"id\":\"5\",\"command\":\"exit\"}");
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

        using var logcatResult = JsonDocument.Parse(console.OutputLines[3]);
        Assert.Equal(SessionEventTypes.Inspect.CommandResult, logcatResult.RootElement.GetProperty("type").GetString());
        Assert.True(logcatResult.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("logcat", logcatResult.RootElement.GetProperty("command").GetString());

        using var screenshotResult = JsonDocument.Parse(console.OutputLines[4]);
        Assert.Equal(SessionEventTypes.Inspect.CommandResult, screenshotResult.RootElement.GetProperty("type").GetString());
        Assert.True(screenshotResult.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("screenshot", screenshotResult.RootElement.GetProperty("command").GetString());

        using var recordResult = JsonDocument.Parse(console.OutputLines[5]);
        Assert.Equal(SessionEventTypes.Inspect.CommandResult, recordResult.RootElement.GetProperty("type").GetString());
        Assert.True(recordResult.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("record", recordResult.RootElement.GetProperty("command").GetString());

        using var sessionEnded = JsonDocument.Parse(console.OutputLines[6]);
        Assert.Equal(SessionEventTypes.Inspect.SessionEnded, sessionEnded.RootElement.GetProperty("type").GetString());
        Assert.Equal([25], host.LogcatRequests);
        Assert.Equal(["inspect-shot"], host.TakeScreenshotRequests);
        Assert.Equal(["inspect.mp4|3"], host.RecordRequests);
    }

    [Fact]
    public async Task RunAsync_Inspect_Stops_When_Initial_Screen_State_Throws_Fatal_Exception()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Ignored"))
        {
            ScreenStateException = new OutOfMemoryException("fatal")
        };
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["inspect"]);

        Assert.Equal(1, exitCode);
        Assert.Equal(2, console.OutputLines.Count);

        using var sessionStarted = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(SessionEventTypes.Inspect.SessionStarted, sessionStarted.RootElement.GetProperty("type").GetString());

        using var sessionError = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(SessionEventTypes.Inspect.SessionError, sessionError.RootElement.GetProperty("type").GetString());
        Assert.Contains("fatal", sessionError.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
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
