using System.Text.Json;
using System.Runtime.InteropServices;
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
        Assert.Contains("Workflow index:", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("luotsi help quickstart", console.ErrorLines[0], StringComparison.Ordinal);
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
    public async Task RunAsync_Help_Command_Writes_Quickstart_Topic()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["help", "quickstart"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(console.OutputLines);
        Assert.Single(console.ErrorLines);
        Assert.Contains("Luotsi help: quickstart", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("luotsi doctor --device <adb serial>", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("luotsi scenario-init --file scenarios/smoke.json", console.ErrorLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Help_Command_Writes_Replay_Topic()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["help", "replay"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(console.OutputLines);
        Assert.Single(console.ErrorLines);
        Assert.Contains("Luotsi help: replay", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("luotsi replay summarize --artifacts <artifact-root> [--format json|jsonl]", console.ErrorLines[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("workflows")]
    [InlineData("start")]
    [InlineData("getting-started")]
    public async Task RunAsync_Help_Command_Normalizes_Quickstart_Aliases(string alias)
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["help", alias]);

        Assert.Equal(0, exitCode);
        Assert.Empty(console.OutputLines);
        Assert.Single(console.ErrorLines);
        Assert.Contains("Luotsi help: quickstart", console.ErrorLines[0], StringComparison.Ordinal);
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
    public async Task RunAsync_Version_Command_Returns_Runtime_And_Install_Metadata()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var environment = CreateInstalledLuotsiEnvironment(fileSystem, "v0.1.0-rc.4");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            Environment = environment
        });

        var exitCode = await app.RunAsync(["version"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("version", envelope.RootElement.GetProperty("command").GetString());
        var data = envelope.RootElement.GetProperty("data");
        Assert.NotEqual("unknown", data.GetProperty("runtime_version").GetString());
        Assert.Equal("v0.1.0-rc.4", data.GetProperty("installed_tag").GetString());
        Assert.Equal("win-x64", data.GetProperty("rid").GetString());
        Assert.True(data.GetProperty("installed_manifest_present").GetBoolean());
        Assert.True(data.GetProperty("helper_apk_present").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_Update_DryRun_Returns_Installer_Command_Without_Running_Process()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        var environment = CreateInstalledLuotsiEnvironment(fileSystem, "v0.1.0-rc.3");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            Environment = environment,
            ProcessRunner = processRunner
        });

        var exitCode = await app.RunAsync(["update", "--version", "0.1.0-rc.4", "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("dry_run", data.GetProperty("status").GetString());
        Assert.Equal("v0.1.0-rc.3", data.GetProperty("current_tag").GetString());
        Assert.Equal("v0.1.0-rc.4", data.GetProperty("target").GetString());
        Assert.Equal("stable", data.GetProperty("channel").GetString());
        Assert.Empty(processRunner.Calls);
    }

    [Fact]
    public async Task RunAsync_Update_Uses_Custom_Install_Root_From_Environment()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var installRoot = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? @"D:\Tools\Luotsi"
            : "/opt/luotsi";
        AddInstalledLuotsiManifest(fileSystem, installRoot, "v0.1.0-rc.3");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            Environment = new FakeEnvironmentVariables(new Dictionary<string, string>
            {
                ["LUOTSI_INSTALL_ROOT"] = installRoot
            }),
            ProcessRunner = new FakeProcessRunner()
        });

        var exitCode = await app.RunAsync(["update", "--version", "v0.1.0-rc.4", "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal(installRoot, envelope.RootElement.GetProperty("data").GetProperty("install_root").GetString());
    }

    [Fact]
    public async Task RunAsync_Update_Rejects_Unsafe_Version_Tag()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var environment = CreateInstalledLuotsiEnvironment(fileSystem, "v0.1.0-rc.3");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            Environment = environment,
            ProcessRunner = new FakeProcessRunner()
        });

        var exitCode = await app.RunAsync(["update", "--version", "v0.1.0;Remove-Item", "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("release tag", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Update_Starts_Detached_Updater_On_Windows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        var environment = CreateInstalledLuotsiEnvironment(fileSystem, "v0.1.0-rc.3");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            Environment = environment,
            ProcessRunner = processRunner
        });

        var exitCode = await app.RunAsync(["update", "--version", "v0.1.0-rc.4", "--detach"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal("update_started", envelope.RootElement.GetProperty("data").GetProperty("status").GetString());
        var call = Assert.Single(processRunner.Calls);
        Assert.Contains("Start-Process", string.Join(" ", call.Args), StringComparison.Ordinal);
        Assert.Contains("Wait-Process", string.Join(" ", call.Args), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Update_On_Windows_Requires_Detach_For_NonDryRun()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        var environment = CreateInstalledLuotsiEnvironment(fileSystem, "v0.1.0-rc.3");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            Environment = environment,
            ProcessRunner = processRunner
        });

        var exitCode = await app.RunAsync(["update", "--version", "v0.1.0-rc.4"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("requires --detach", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(processRunner.Calls);
    }

    [Fact]
    public async Task RunAsync_Update_Prerelease_Channel_Requires_Explicit_Version()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var environment = CreateInstalledLuotsiEnvironment(fileSystem, "v0.1.0-rc.3");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            Environment = environment,
            ProcessRunner = new FakeProcessRunner()
        });

        var exitCode = await app.RunAsync(["update", "--channel", "prerelease", "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("--version <tag>", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
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
        Assert.Contains("Available topics: quickstart", console.ErrorLines[0], StringComparison.Ordinal);
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
    public async Task RunAsync_ReplaySummarize_Returns_Condensed_Replay_Summary()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
                var replayRoot = SeedReplaySummaryArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "summarize", "--artifacts", replayRoot]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("replay", envelope.RootElement.GetProperty("command").GetString());
        Assert.Equal(replayRoot, envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString());
        Assert.Equal(ResultSchemas.SessionReplaySummary, envelope.RootElement.GetProperty("data").GetProperty("schema").GetString());
        Assert.Equal(1, envelope.RootElement.GetProperty("data").GetProperty("session_count").GetInt32());
        Assert.Equal(1, envelope.RootElement.GetProperty("data").GetProperty("failure_count").GetInt32());

        var session = envelope.RootElement.GetProperty("data").GetProperty("sessions")[0];
        Assert.Equal("view", session.GetProperty("session_kind").GetString());
        Assert.Equal(6000, session.GetProperty("duration_ms").GetInt64());
        Assert.True(session.GetProperty("has_failure_signals").GetBoolean());

        var highlights = session.GetProperty("timeline_highlights").EnumerateArray().ToArray();
        Assert.Contains(highlights, static highlight =>
            highlight.GetProperty("type").GetString() == SessionEventTypes.View.ReconnectRequested &&
            highlight.GetProperty("detail").GetString()!.Contains("source=toolbar", StringComparison.Ordinal));
        Assert.Contains(highlights, static highlight =>
            highlight.GetProperty("type").GetString() == SessionEventTypes.View.ShareClientConnected &&
            highlight.GetProperty("detail").GetString()!.Contains("observer_count=1", StringComparison.Ordinal));
        Assert.Contains(highlights, static highlight =>
            highlight.GetProperty("type").GetString() == SessionEventTypes.View.Stats &&
            highlight.GetProperty("detail").GetString()!.Contains("decode_fps=29.5", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ReplaySummarize_Reads_Failed_Scenario_Run_Artifacts()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        fileSystem.AddFile("/tmp/scenario.json", """
        {
                    "name": "broken scenario",
          "steps": [
                        { "action": "waitVisible", "text": "Target" }
          ]
        }
        """);

        var runConsole = new FakeConsole();
        var runApp = new App(new AppDependencies
        {
            Console = runConsole,
            FileSystem = fileSystem,
            TimeProvider = timeProvider,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(CreateReplaySummarizeFailingHostWithRichArtifacts())
        });

        var runExitCode = await runApp.RunAsync(["run", "--file", "/tmp/scenario.json", "--artifacts", "/tmp/test-artifacts", "--report-json", "/tmp/report.json"]);
        using var runEnvelope = runConsole.ParseSingleOutputAsJson();
        var artifactRoot = runEnvelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();

        Assert.Equal(1, runExitCode);
        Assert.False(runEnvelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.NotNull(artifactRoot);

        var replayConsole = new FakeConsole();
        var replayApp = new App(new AppDependencies
        {
            Console = replayConsole,
            FileSystem = fileSystem,
            TimeProvider = timeProvider
        });

        var replayExitCode = await replayApp.RunAsync(["replay", "summarize", "--artifacts", artifactRoot]);
        using var replayEnvelope = replayConsole.ParseSingleOutputAsJson();

        Assert.Equal(0, replayExitCode);
        Assert.True(replayEnvelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, replayEnvelope.RootElement.GetProperty("data").GetProperty("session_count").GetInt32());
        Assert.Equal(1, replayEnvelope.RootElement.GetProperty("data").GetProperty("failure_count").GetInt32());

        var session = replayEnvelope.RootElement.GetProperty("data").GetProperty("sessions")[0];
        Assert.Equal("run", session.GetProperty("session_kind").GetString());
        Assert.Equal("/tmp/scenario.json", session.GetProperty("target").GetString());
        Assert.True(session.GetProperty("has_failure_signals").GetBoolean());
        Assert.Equal("failure-capsule.json", session.GetProperty("failure_capsule_path").GetString());

        var failureCapsule = session.GetProperty("failure_capsule");
        Assert.Equal("failure-capsule.json", failureCapsule.GetProperty("path").GetString());
        Assert.Equal("/tmp/report.json", failureCapsule.GetProperty("reports").GetProperty("json_path").GetString());
        Assert.Contains(failureCapsule.GetProperty("screenshots").EnumerateArray(), artifact =>
            artifact.GetProperty("path").GetString() == "failure.png");
        Assert.Contains(failureCapsule.GetProperty("failure_bundles").EnumerateArray(), bundle =>
            bundle.GetProperty("path").GetString() == "failure.json");
        var failedScenario = Assert.Single(failureCapsule.GetProperty("scenarios").EnumerateArray());
        Assert.Equal("broken scenario", failedScenario.GetProperty("scenario").GetString());
        Assert.Equal("waitVisible", failedScenario.GetProperty("failed_step").GetProperty("name").GetString());

        var highlights = session.GetProperty("timeline_highlights").EnumerateArray().ToArray();
        Assert.Contains(highlights, static highlight =>
            highlight.GetProperty("type").GetString() == "scenario_step_started" &&
            highlight.GetProperty("detail").GetString()!.Contains("phase=main", StringComparison.Ordinal));
        Assert.Contains(highlights, static highlight =>
            highlight.GetProperty("type").GetString() == "scenario_step_failed" &&
            highlight.GetProperty("detail").GetString()!.Contains("artifacts=screenshot: failure.png", StringComparison.Ordinal));
        Assert.Contains(highlights, static highlight =>
            highlight.GetProperty("type").GetString() == "scenario_run_ended" &&
            highlight.GetProperty("detail").GetString()!.Contains("failure_bundles=failure.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ReplaySummarize_FormatJson_Writes_Bare_Summary_Object()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplaySummaryArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "summarize", "--artifacts", replayRoot, "--format", "json"]);
        using var output = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Empty(console.ErrorLines);
        Assert.False(output.RootElement.TryGetProperty("ok", out _));
        Assert.Equal(ResultSchemas.SessionReplaySummary, output.RootElement.GetProperty("schema").GetString());
        Assert.Equal(replayRoot, output.RootElement.GetProperty("artifact_root").GetString());
        Assert.Equal(1, output.RootElement.GetProperty("session_count").GetInt32());
        Assert.Equal(1, output.RootElement.GetProperty("failure_count").GetInt32());
    }

    [Fact]
    public async Task RunAsync_ReplaySummarize_FormatJson_Includes_Failure_Capsule_Summary_When_Present()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "broken scenario",
          "steps": [
            { "action": "waitVisible", "text": "Target" }
          ]
        }
        """);

        var runConsole = new FakeConsole();
        var runApp = new App(new AppDependencies
        {
            Console = runConsole,
            FileSystem = fileSystem,
            TimeProvider = timeProvider,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(CreateReplaySummarizeFailingHostWithRichArtifacts())
        });

        var runExitCode = await runApp.RunAsync(["run", "--file", "/tmp/scenario.json", "--artifacts", "/tmp/test-artifacts", "--report-json", "/tmp/report.json"]);
        using var runEnvelope = runConsole.ParseSingleOutputAsJson();
        var artifactRoot = runEnvelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();

        Assert.Equal(1, runExitCode);
        Assert.NotNull(artifactRoot);

        var replayConsole = new FakeConsole();
        var replayApp = new App(new AppDependencies
        {
            Console = replayConsole,
            FileSystem = fileSystem,
            TimeProvider = timeProvider
        });

        var replayExitCode = await replayApp.RunAsync(["replay", "summarize", "--artifacts", artifactRoot!, "--format", "json"]);
        using var output = replayConsole.ParseSingleOutputAsJson();

        Assert.Equal(0, replayExitCode);
        var session = output.RootElement.GetProperty("sessions")[0];
        var failureCapsule = session.GetProperty("failure_capsule");
        Assert.Equal("failure-capsule.json", failureCapsule.GetProperty("path").GetString());
        Assert.Equal("/tmp/report.json", failureCapsule.GetProperty("reports").GetProperty("json_path").GetString());
        Assert.Contains(failureCapsule.GetProperty("failure_bundles").EnumerateArray(), bundle =>
            bundle.GetProperty("path").GetString() == "failure.json");
    }

    [Fact]
    public async Task RunAsync_ReplaySummarize_FormatJsonl_Writes_Summary_And_Session_Lines()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplaySummaryArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "summarize", "--artifacts", replayRoot, "--format", "jsonl"]);
        using var summaryLine = JsonDocument.Parse(console.OutputLines[0]);
        using var sessionLine = JsonDocument.Parse(console.OutputLines[1]);

        Assert.Equal(0, exitCode);
        Assert.Empty(console.ErrorLines);
        Assert.Equal(2, console.OutputLines.Count);
        Assert.Equal(ResultSchemas.SessionReplaySummary, summaryLine.RootElement.GetProperty("schema").GetString());
        Assert.Equal("summary", summaryLine.RootElement.GetProperty("type").GetString());
        Assert.Equal(replayRoot, summaryLine.RootElement.GetProperty("artifact_root").GetString());
        Assert.Equal(1, summaryLine.RootElement.GetProperty("session_count").GetInt32());
        Assert.Equal(1, summaryLine.RootElement.GetProperty("failure_count").GetInt32());
        Assert.Equal(ResultSchemas.SessionReplaySummary, sessionLine.RootElement.GetProperty("schema").GetString());
        Assert.Equal("session", sessionLine.RootElement.GetProperty("type").GetString());
        Assert.Equal(replayRoot, sessionLine.RootElement.GetProperty("artifact_root").GetString());
        Assert.Equal("view", sessionLine.RootElement.GetProperty("session").GetProperty("session_kind").GetString());
        Assert.True(sessionLine.RootElement.GetProperty("session").GetProperty("has_failure_signals").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_ReplaySummarize_Invalid_Format_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplaySummaryArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "summarize", "--artifacts", replayRoot, "--format", "yaml"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("--format must be json or jsonl", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
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
        Assert.Empty(host.ForwardRemoveRequests);
        Assert.Empty(host.ReverseRemoveRequests);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Contains("Ran `adb reconnect offline`", data.GetProperty("applied_fixes")[0].GetString(), StringComparison.Ordinal);
        Assert.Contains("Skipped stale Luotsi port cleanup", data.GetProperty("applied_fixes")[1].GetString(), StringComparison.Ordinal);
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
        var fileSystem = new FakeFileSystem();
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
            FileSystem = fileSystem,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["inspect", "--artifacts", "/tmp/test-artifacts"]);

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

        var replayPath = Path.Join("/tmp/test-artifacts", "20260515-120000-inspect", "session-replay.json");
        var timelinePath = Path.Join("/tmp/test-artifacts", "20260515-120000-inspect", "session-timeline.jsonl");
        Assert.True(fileSystem.FileExists(replayPath));
        Assert.True(fileSystem.FileExists(timelinePath));

        using var replay = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(replayPath));
        Assert.Equal(ResultSchemas.SessionReplay, replay.RootElement.GetProperty("schema").GetString());
        Assert.Equal("inspect", replay.RootElement.GetProperty("sessionKind").GetString());
        Assert.Equal("client_exit", replay.RootElement.GetProperty("reason").GetString());
        Assert.Equal(5, replay.RootElement.GetProperty("eventCount").GetInt32());

        var timeline = await fileSystem.ReadAllTextAsync(timelinePath);
        Assert.Contains(SessionEventTypes.Inspect.SessionStarted, timeline, StringComparison.Ordinal);
        Assert.Contains(SessionEventTypes.Inspect.ScreenSnapshot, timeline, StringComparison.Ordinal);
        Assert.Contains(SessionEventTypes.Inspect.CommandResult, timeline, StringComparison.Ordinal);
        Assert.Contains(SessionEventTypes.Inspect.ScreenDelta, timeline, StringComparison.Ordinal);
        Assert.Contains(SessionEventTypes.Inspect.SessionEnded, timeline, StringComparison.Ordinal);
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

    private static FakeEnvironmentVariables CreateInstalledLuotsiEnvironment(FakeFileSystem fileSystem, string tag)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var installRoot = isWindows
            ? @"C:\Users\Test\AppData\Local\Luotsi"
            : "/home/test/.local/share/luotsi";
        AddInstalledLuotsiManifest(fileSystem, installRoot, tag);

        return new FakeEnvironmentVariables(isWindows
            ? new Dictionary<string, string> { ["LOCALAPPDATA"] = @"C:\Users\Test\AppData\Local" }
            : new Dictionary<string, string> { ["HOME"] = "/home/test" });
    }

    private static void AddInstalledLuotsiManifest(FakeFileSystem fileSystem, string installRoot, string tag)
    {
        var currentRoot = Path.Join(installRoot, "versions", tag);
        var commandPath = Path.Join(installRoot, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "luotsi.cmd" : "luotsi");
        var helperApk = Path.Join(currentRoot, "Luotsi.ViewServer.Android", "app", "build", "outputs", "apk", "release", "app-release.apk");

        fileSystem.AddFile(Path.Join(installRoot, "install.json"), $$"""
        {
          "tag": "{{tag}}",
          "version": "{{tag.TrimStart('v')}}",
          "rid": "win-x64",
          "install_root": "{{JsonEncodedText.Encode(installRoot)}}",
          "current_root": "{{JsonEncodedText.Encode(currentRoot)}}",
          "command_path": "{{JsonEncodedText.Encode(commandPath)}}",
          "helper_apk_path": "{{JsonEncodedText.Encode(helperApk)}}"
        }
        """);
        fileSystem.AddFile(helperApk, "apk");
    }

    private static string SeedReplaySummaryArtifacts(FakeFileSystem fileSystem)
    {
        var replayRoot = "/tmp/replay-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"view_started","session_id":"view-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"view_share_started","session_id":"view-session","occurred_at":"2026-05-18T10:00:01Z","endpoint":"127.0.0.1:9000","observer_count":0}
        {"type":"view_share_client_connected","session_id":"view-session","occurred_at":"2026-05-18T10:00:02Z","endpoint":"127.0.0.1:9000","remote_endpoint":"10.0.0.25:40122","observer_count":1,"reason":"observer_joined"}
        {"type":"view_reconnect_requested","session_id":"view-session","occurred_at":"2026-05-18T10:00:03Z","device":"192.168.0.134:5555","source":"toolbar","reason":"manual_retry"}
        {"type":"view_reconnected","session_id":"view-session","reconnected_at":"2026-05-18T10:00:04Z","device":"192.168.0.134:5555","capture_backend":"mediaprojection","requested_capture_backend":"auto","connection":{"codec":"h264","width":1600,"height":900,"transport":"adb-forward"}}
        {"type":"view_stats","session_id":"view-session","observed_at":"2026-05-18T10:00:05Z","stats":{"decoded_frames":120,"presented_frames":118,"dropped_frames":2,"decode_fps":29.5,"present_fps":29.0,"end_to_end_latency_ms":142}}
        {"type":"view_error","session_id":"view-session","occurred_at":"2026-05-18T10:00:06Z","error":{"category":"transport","message":"Unexpected end of stream"}}
        {"type":"view_ended","session_id":"view-session","ended_at":"2026-05-18T10:00:06Z","reason":"error"}
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "session-replay.json"), """
        {
          "schema": "luotsi-session-replay.v1",
          "sessionKind": "view",
          "sessionId": "view-session",
          "startedAt": "2026-05-18T10:00:00Z",
          "endedAt": "2026-05-18T10:00:06Z",
          "reason": "error",
          "exitCode": 1,
          "target": "192.168.0.134:5555",
          "timelineFileName": "session-timeline.jsonl",
          "eventCount": 8,
          "eventTypes": ["view_started", "view_share_started", "view_share_client_connected", "view_reconnect_requested", "view_reconnected", "view_stats", "view_error", "view_ended"]
        }
        """);
        return replayRoot;
    }

    private static FakeDeviceHost CreateReplaySummarizeFailingHostWithRichArtifacts() =>
        new()
        {
            WaitVisibleException = new InvalidOperationException("not visible"),
            FailureArtifacts = new FailureArtifactBundle(
                ResultSchemas.FailureBundle,
                DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                "scenario",
                "broken scenario",
                "/tmp/scenario.json",
                1,
                "waitVisible",
                "waitVisible",
                typeof(InvalidOperationException).FullName!,
                "not visible",
                [
                    new FailureArtifact("screenshot", "failure.png"),
                    new FailureArtifact("logcat", "failure-logcat.txt"),
                    new FailureArtifact("hierarchy", "failure-hierarchy.xml"),
                    new FailureArtifact("screen_state", "failure-screen-state.json")
                ],
                [])
            {
                MetadataFile = "failure.json"
            }
        };

}
