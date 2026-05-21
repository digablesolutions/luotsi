using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Models;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task GetScreenStateAsync_Writes_Invalid_Dump_Artifact_On_Parse_Failure()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["screen-state"]), fileSystem, timeProvider);
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, "not-xml", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "still-not-xml", string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "UI hierchary dumped to: /dev/tty", string.Empty));
        var runner = new DeviceRunner(adb, artifacts, timeProvider, new FakeDelay(timeProvider), fileSystem);

        var error = await Assert.ThrowsAsync<ScreenStateUnavailableException>(runner.GetScreenStateAsync);

        Assert.Equal("screen_state_unavailable", error.CategoryOverride);
        Assert.Contains("did not contain parseable XML", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(DeviceArtifactNames.HierarchyDumpAttemptsJson, error.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.FileExists(Path.Join(artifacts.Root, DeviceArtifactNames.HierarchyXml)));
        Assert.True(fileSystem.FileExists(Path.Join(artifacts.Root, DeviceArtifactNames.InvalidHierarchyXml)));
        Assert.True(fileSystem.FileExists(Path.Join(artifacts.Root, DeviceArtifactNames.HierarchyDumpAttemptsJson)));
    }


    [Fact]
    public async Task GetScreenStateAsync_Prefers_File_Backed_UiDump()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["screen-state"]), fileSystem, timeProvider);
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("Target"), string.Empty));
        var runner = new DeviceRunner(adb, artifacts, timeProvider, new FakeDelay(timeProvider), fileSystem);

        var state = await runner.GetScreenStateAsync();

        Assert.Contains(state.Elements, element => element.Text == "Target");
        Assert.Contains("uiautomator dump '/data/local/tmp/luotsi-window.xml'", adb.ShellCommands[0], StringComparison.Ordinal);
        Assert.Empty(adb.RunCommands);
    }


    [Fact]
    public async Task GetScreenStateAsync_Falls_Back_To_ExecOut_UiDump_And_Strips_Prefix_Noise()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["screen-state"]), fileSystem, timeProvider);
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, "UI hierchary dumped to: /data/local/tmp/luotsi-window.xml", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "UI hierchary dumped to: /sdcard/window_dump.xml", string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "UI hierchary dumped to: /dev/tty\n<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>" + CreateUiDump("Target") + "\nUI hierchary dumped to: /dev/tty", string.Empty));
        var runner = new DeviceRunner(adb, artifacts, timeProvider, new FakeDelay(timeProvider), fileSystem);

        var state = await runner.GetScreenStateAsync();

        Assert.Contains(state.Elements, element => element.Text == "Target");
        Assert.Equal(["exec-out", "uiautomator", "dump", "/dev/tty"], adb.RunCommands[0]);
        Assert.True(fileSystem.FileExists(Path.Join(artifacts.Root, DeviceArtifactNames.HierarchyDumpAttemptsJson)));
    }


    [Fact]
    public void IsRetryableHierarchyDumpFailure_Uses_ScreenStateUnavailableException_Type()
    {
        Assert.True(AndroidScreenCaptureService.IsRetryableHierarchyDumpFailure(new ScreenStateUnavailableException("parse failure")));
        Assert.False(AndroidScreenCaptureService.IsRetryableHierarchyDumpFailure(new InvalidOperationException("parse failure")));
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
    public async Task WaitVisibleAsync_Matches_Cp437_Mojibake_Text_Against_Unicode_Query()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        const string mojibake = "Kirjaudu sis\u251C\u00F1\u251C\u00F1n";
        adb.EnqueueShellResult(new ProcessResult(
            0,
            CreateUiDumpWithNodes(
                CreateUiNode(text: string.Empty, contentDescription: mojibake, className: "android.view.View", clickable: false, left: 772, top: 615, right: 1148, bottom: 711)),
            string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["wait-visible"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        var element = await runner.WaitVisibleAsync("Kirjaudu sisään", 1);

        Assert.Equal(mojibake, element.ContentDescription);
        Assert.Equal(615, element.Top);
    }


    [Fact]
    public async Task WaitVisibleAsync_Retries_Transient_Invalid_Dumps()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var delay = new FakeDelay(timeProvider);
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, "not-xml", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "still-not-xml", string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "UI hierchary dumped to: /dev/tty", string.Empty));
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
        Assert.Single(adb.ShellCommands, static command => command.Contains("uiautomator dump", StringComparison.Ordinal));
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
        Assert.Equal(2, adb.ShellCommands.Count(static command => command.Contains("uiautomator dump", StringComparison.Ordinal)));
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

        Assert.Equal(2, adb.ShellCommands.Count(static command => command.Contains("uiautomator dump", StringComparison.Ordinal)));
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
        adb.EnqueueShellResult(new ProcessResult(0, "still-not-xml", string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "UI hierchary dumped to: /dev/tty", string.Empty));
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
    public async Task AssertScreenshotAsync_Captures_Dimensions_And_Sha256()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.AttachFileSystem(fileSystem);
        var idGenerator = new FakeUniqueIdGenerator("fixed-shot-id");
        var png = CreatePngHeader(320, 240);
        adb.AddRemoteFile("/sdcard/device-e2e-fixed-shot-id.png", png);
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem, idGenerator);
        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)).ToLowerInvariant();

        var result = await runner.AssertScreenshotAsync("home", 320, 240, expectedHash);

        Assert.Equal("home", result.Label);
        Assert.Equal("home-screenshot.png", result.File);
        Assert.Equal(320, result.Width);
        Assert.Equal(240, result.Height);
        Assert.Equal(expectedHash, result.Sha256);
    }


    [Fact]
    public async Task WaitForStepAsync_Uses_Incremental_Telemetry_Parsing()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var parser = new CountingTelemetryParser();
        adb.EnqueueLogLines("05-15 12:00:00.000 I/Luotsi: LUOTSI_DEVICE_TELEMETRY {\"schema\":\"luotsi-device-telemetry.v1\",\"seq\":15,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:00Z\",\"event\":\"step\",\"step\":\"STEP_IDLE\"}");
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
        adb.EnqueueLogLines("05-15 12:00:03.000 I/Luotsi: LUOTSI_DEVICE_TELEMETRY {\"schema\":\"luotsi-device-telemetry.v1\",\"seq\":16,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:03Z\",\"event\":\"action_ready\",\"step\":\"STEP_IDLE\",\"action\":\"sign_in\"}");
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["telemetry-watch"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem, telemetryParser: parser);

        var result = await runner.TelemetryWatchAsync(5);

        Assert.Equal(1, result.EventCount);
        Assert.Equal(0, parser.ParseLogCallCount);
        Assert.Equal(1, parser.ParseLineCallCount);
    }


    [Fact]
    public async Task PreflightAsync_Writes_Device_Fingerprint_Artifact()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, CreateDeviceFingerprintShellOutput("SER123", "Pixel 9", "16", "36", "google/pixel/device", "arm64-v8a,x86_64", "mCurrentFocus=App"), string.Empty));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["preflight"]), fileSystem, timeProvider);
        var runner = new DeviceRunner(adb, artifacts, timeProvider, new FakeDelay(timeProvider), fileSystem);

        var result = await runner.PreflightAsync(null);
        var json = SerializeToJsonElement(result);
        var artifactRoot = artifacts.Root;

        Assert.Equal("Pixel 9", json.GetProperty("model").GetString());
        Assert.Equal("google/pixel/device", json.GetProperty("fingerprint").GetString());
        Assert.Single(adb.ShellCommands);
        Assert.True(fileSystem.FileExists(Path.Combine(artifactRoot, "device-fingerprint.json")));
    }

    [Fact]
    public async Task ReadPreflightAsync_Does_Not_Write_Device_Fingerprint_Artifact()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, CreateDeviceFingerprintShellOutput("SER123", "Pixel 9", "16", "36", "google/pixel/device", "arm64-v8a,x86_64", "mCurrentFocus=App"), string.Empty));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["preflight"]), fileSystem, timeProvider);
        var runner = new DeviceRunner(adb, artifacts, timeProvider, new FakeDelay(timeProvider), fileSystem);

        var result = await runner.ReadPreflightAsync(null);
        var json = SerializeToJsonElement(result);
        var artifactRoot = artifacts.Root;

        Assert.Equal("Pixel 9", json.GetProperty("model").GetString());
        Assert.Equal("google/pixel/device", json.GetProperty("fingerprint").GetString());
        Assert.Single(adb.ShellCommands);
        Assert.False(fileSystem.FileExists(Path.Join(artifactRoot, "device-fingerprint.json")));
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
            ["LUOTSI_EMULATED_STORAGE_TARGET"] = "/sdcard",
            ["LUOTSI_EMULATED_STORAGE_SOURCE"] = "/mnt/shell/emulated/0"
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
    public async Task AssertAppVersionAsync_Uses_Luotsi_Target_Package_Environment_Fallback()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, "versionName=1.0.0\nversionCode=123", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDumpWithNodes(CreateUiNode("v1.0.0+123", string.Empty, "android.widget.TextView", false, 900, 0, 1080, 80)), string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "Physical size: 1080x1920", string.Empty));
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LUOTSI_TARGET_PACKAGE"] = "dev.luotsi.staging"
        });
        var runner = new DeviceRunner(
            adb,
            ArtifactSession.Create(CliOptions.Parse(["assert-app-version"]), fileSystem, timeProvider),
            timeProvider,
            new FakeDelay(timeProvider),
            fileSystem,
            environment: environment);

        var result = await runner.AssertAppVersionAsync(null, 140, 300);

        Assert.Equal("dev.luotsi.staging", result.Package);
        Assert.Contains("dumpsys package 'dev.luotsi.staging'", adb.ShellCommands[0], StringComparison.Ordinal);
    }


}
