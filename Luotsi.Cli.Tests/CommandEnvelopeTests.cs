using System.Text.Json;
using System.Runtime.InteropServices;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Cli.Routing;
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
    public async Task RunAsync_ReplayOpen_DryRun_Refreshes_Index_And_Returns_Opener_Command()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        var replayRoot = SeedReplaySummaryArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            ProcessRunner = processRunner,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "open", "--artifacts", replayRoot, "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.True(exitCode == 0, string.Join(Environment.NewLine, console.OutputLines.Concat(console.ErrorLines)));
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(ResultSchemas.ReplayOpen, data.GetProperty("schema").GetString());
        Assert.Equal(replayRoot, data.GetProperty("artifact_root").GetString());
        Assert.False(data.GetProperty("opened").GetBoolean());
        Assert.EndsWith("index.html", data.GetProperty("index_html_path").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "index.html")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "index.md")));
        Assert.Empty(processRunner.Calls);
    }

    [Fact]
    public async Task RunAsync_ReplayOpen_Opens_Refreshed_Index_With_Platform_Opener()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        var replayRoot = SeedReplaySummaryArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            ProcessRunner = processRunner,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "open", "--artifacts", replayRoot]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.True(exitCode == 0, string.Join(Environment.NewLine, console.OutputLines.Concat(console.ErrorLines)));
        Assert.True(envelope.RootElement.GetProperty("data").GetProperty("opened").GetBoolean());
        var call = Assert.Single(processRunner.Calls);
        Assert.Contains(call.Args, arg => arg.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "index.html")));
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_Writes_Valid_Draft_From_Inspect_Timeline()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedInspectReplayDraftArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--output", "/tmp/draft.json", "--name", "draft smoke", "--write-json", "--write-markdown"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(ResultSchemas.ScenarioDraft, data.GetProperty("schema").GetString());
        Assert.Equal("/tmp/draft.json", data.GetProperty("output").GetString());
        Assert.Equal(Path.Join(replayRoot, "scenario-draft-summary.json"), data.GetProperty("json_path").GetString());
        Assert.Equal(Path.Join(replayRoot, "scenario-draft.md"), data.GetProperty("markdown_path").GetString());
        Assert.Equal("draft smoke", data.GetProperty("scenario").GetProperty("name").GetString());
        Assert.Equal(4, data.GetProperty("scenario").GetProperty("steps").GetArrayLength());
        Assert.Equal(4, data.GetProperty("step_origins").GetArrayLength());
        Assert.Equal("inspect_command", data.GetProperty("step_origins")[0].GetProperty("source").GetString());
        Assert.Equal("wait_visible", data.GetProperty("step_origins")[0].GetProperty("command").GetString());
        Assert.Equal("session-timeline.jsonl", data.GetProperty("step_origins")[0].GetProperty("source_path").GetString());
        Assert.Equal(1, data.GetProperty("step_origins")[0].GetProperty("sequence").GetInt32());
        Assert.Equal(DateTimeOffset.Parse("2026-05-18T10:00:01Z", System.Globalization.CultureInfo.InvariantCulture), data.GetProperty("step_origins")[0].GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal("luotsi replay timeline --artifacts <artifact-root> --source-path session-timeline.jsonl --sequence 1 --context 2", data.GetProperty("step_origins")[0].GetProperty("source_command").GetString());
        var reviewItems = data.GetProperty("review_items").EnumerateArray().ToArray();
        Assert.Contains(reviewItems, item =>
            item.GetProperty("category").GetString() == "selector" &&
            item.GetProperty("command").GetString() == "luotsi screen-state");
        var sourceSummary = Assert.Single(data.GetProperty("source_summaries").EnumerateArray());
        Assert.Equal("inspect_command", sourceSummary.GetProperty("source").GetString());
        Assert.Equal(4, sourceSummary.GetProperty("step_count").GetInt32());
        Assert.Equal(0, sourceSummary.GetProperty("normalization_count").GetInt32());
        var suggestedCommands = data.GetProperty("suggested_commands").EnumerateArray().ToArray();
        Assert.Contains(suggestedCommands, command =>
            command.GetProperty("command").GetString() == "luotsi scenario-validate --file /tmp/draft.json");
        Assert.Contains(suggestedCommands, command =>
            command.GetProperty("command").GetString()!.Contains("replay scrub", StringComparison.Ordinal));
        Assert.Contains(suggestedCommands, command =>
            command.GetProperty("command").GetString() == "luotsi replay timeline --artifacts <artifact-root> --source-path session-timeline.jsonl --sequence 1 --context 2");
        Assert.True(fileSystem.FileExists("/tmp/draft.json"));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "scenario-draft-summary.json")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "scenario-draft.md")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "index.md")));
        var review = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft.md"));
        Assert.Contains("# Luotsi Scenario Draft", review, StringComparison.Ordinal);
        Assert.Contains("draft smoke", review, StringComparison.Ordinal);
        Assert.Contains("## Review Checklist", review, StringComparison.Ordinal);
        Assert.Contains("luotsi screen-state", review, StringComparison.Ordinal);
        Assert.Contains("## Source Summary", review, StringComparison.Ordinal);
        Assert.Contains("## Step Origins", review, StringComparison.Ordinal);
        Assert.Contains("## Next Commands", review, StringComparison.Ordinal);
        Assert.Contains("luotsi scenario-validate --file /tmp/draft.json", review, StringComparison.Ordinal);
        Assert.Contains("session-timeline.jsonl", review, StringComparison.Ordinal);
        Assert.Contains("luotsi replay timeline --artifacts", review, StringComparison.Ordinal);
        Assert.Contains("inspect_command", review, StringComparison.Ordinal);

        var validateConsole = new FakeConsole();
        var validateApp = new App(new AppDependencies
        {
            Console = validateConsole,
            FileSystem = fileSystem
        });
        var validateExitCode = await validateApp.RunAsync(["scenario-validate", "--file", "/tmp/draft.json"]);

        Assert.Equal(0, validateExitCode);
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_Promotes_ScreenDelta_Text_Into_Waits()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/inspect-screen-delta-replay-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"session_started","session_id":"inspect-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"tap_text","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","data":{"text":"Sign in"}}
        {"type":"screen_delta","session_id":"inspect-session","delta":{"added":[{"text":"Welcome"},{"content_description":"Open menu"}]}}
        {"type":"session_ended","session_id":"inspect-session","ended_at":"2026-05-18T10:00:09Z","reason":"client_exit"}
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--output", "/tmp/screen-delta-draft.json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var steps = envelope.RootElement.GetProperty("data").GetProperty("scenario").GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal("tapText", steps[0].GetProperty("action").GetString());
        Assert.Equal("Sign in", steps[0].GetProperty("text").GetString());
        Assert.Equal("waitVisible", steps[1].GetProperty("action").GetString());
        Assert.Equal("Welcome", steps[1].GetProperty("text").GetString());
        Assert.Equal("waitVisible", steps[2].GetProperty("action").GetString());
        Assert.Equal("Open menu", steps[2].GetProperty("text").GetString());

        var validateConsole = new FakeConsole();
        var validateApp = new App(new AppDependencies
        {
            Console = validateConsole,
            FileSystem = fileSystem
        });
        var validateExitCode = await validateApp.RunAsync(["scenario-validate", "--file", "/tmp/screen-delta-draft.json"]);

        Assert.Equal(0, validateExitCode);
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_Deduplicates_Adjacent_Inferred_Waits()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/inspect-duplicate-waits-replay-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"wait_visible","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","data":{"text":"Welcome"}}
        {"type":"screen_delta","session_id":"inspect-session","delta":{"added":[{"text":"Welcome"}]}}
        {"type":"command_result","session_id":"inspect-session","id":"2","command":"telemetry_tail","ok":true,"started_at":"2026-05-18T10:00:03Z","ended_at":"2026-05-18T10:00:04Z","data":{"events":[{"event":"step","step":"STEP_IDLE"},{"event":"step","step":"STEP_IDLE"}]}}
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--output", "/tmp/dedup-draft.json", "--write-markdown"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        var steps = data.GetProperty("scenario").GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(2, steps.Length);
        Assert.Equal("waitVisible", steps[0].GetProperty("action").GetString());
        Assert.Equal("Welcome", steps[0].GetProperty("text").GetString());
        Assert.Equal("waitStep", steps[1].GetProperty("action").GetString());
        Assert.Equal("STEP_IDLE", steps[1].GetProperty("step").GetString());
        Assert.Equal(2, data.GetProperty("step_origins").GetArrayLength());
        Assert.Equal(3, data.GetProperty("source_summaries").GetArrayLength());
        var normalizations = data.GetProperty("normalizations").EnumerateArray().ToArray();
        Assert.Equal(2, normalizations.Length);
        Assert.All(normalizations, normalization => Assert.Equal("duplicate_wait", normalization.GetProperty("kind").GetString()));
        Assert.All(normalizations, normalization => Assert.Equal("session-timeline.jsonl", normalization.GetProperty("source_path").GetString()));
        Assert.Equal(1, normalizations[0].GetProperty("sequence").GetInt32());
        Assert.Equal("luotsi replay timeline --artifacts <artifact-root> --source-path session-timeline.jsonl --sequence 1 --context 2", normalizations[0].GetProperty("source_command").GetString());
        Assert.Contains(data.GetProperty("review_items").EnumerateArray(), item =>
            item.GetProperty("category").GetString() == "normalization" &&
            item.GetProperty("command").GetString()!.Contains("--source-path session-timeline.jsonl --sequence 1", StringComparison.Ordinal) &&
            item.GetProperty("message").GetString()!.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Welcome", normalizations[0].GetProperty("detail").GetString(), StringComparison.Ordinal);
        var review = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft.md"));
        Assert.Contains("## Review Checklist", review, StringComparison.Ordinal);
        Assert.Contains("## Normalizations", review, StringComparison.Ordinal);
        Assert.Contains("duplicate_wait", review, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_Promotes_Telemetry_Into_Semantic_Waits()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/inspect-telemetry-replay-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"session_started","session_id":"inspect-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"telemetry_tail","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","data":{"events":[{"event":"step","step":"STEP_IDLE"},{"event":"action_ready","step":"STEP_IDLE","action":"sign_in"},{"event":"domain_warning"}]}}
        {"type":"session_ended","session_id":"inspect-session","ended_at":"2026-05-18T10:00:09Z","reason":"client_exit"}
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--output", "/tmp/telemetry-draft.json", "--write-json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var steps = envelope.RootElement.GetProperty("data").GetProperty("scenario").GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal("waitStep", steps[0].GetProperty("action").GetString());
        Assert.Equal("STEP_IDLE", steps[0].GetProperty("step").GetString());
        Assert.Equal("waitActionReady", steps[1].GetProperty("action").GetString());
        Assert.Equal("sign_in", steps[1].GetProperty("text").GetString());
        Assert.Equal("STEP_IDLE", steps[1].GetProperty("step").GetString());
        Assert.Equal("assertEvent", steps[2].GetProperty("action").GetString());
        Assert.Equal("domain_warning", steps[2].GetProperty("event").GetString());
        Assert.Contains(envelope.RootElement.GetProperty("data").GetProperty("suggestions").EnumerateArray(), suggestion => suggestion.GetProperty("kind").GetString() == "telemetry");

        var validateConsole = new FakeConsole();
        var validateApp = new App(new AppDependencies
        {
            Console = validateConsole,
            FileSystem = fileSystem
        });
        var validateExitCode = await validateApp.RunAsync(["scenario-validate", "--file", "/tmp/telemetry-draft.json"]);

        Assert.Equal(0, validateExitCode);
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_Invalid_Timeline_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedInspectReplayDraftArtifacts(fileSystem);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"command_result","command":"tap_text","ok":true,"data":{"text":"Sign in"}}
        not-json
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("invalid JSON", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_ReplaySearch_Returns_Text_Matches_From_Artifact_Root()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplaySearchArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "search", "--artifacts", replayRoot, "--contains", "not visible"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(ResultSchemas.ReplaySearch, data.GetProperty("schema").GetString());
        Assert.Equal("not visible", data.GetProperty("query").GetString());
        Assert.Equal(3, data.GetProperty("match_count").GetInt32());
        Assert.False(data.GetProperty("truncated").GetBoolean());
        var matches = data.GetProperty("matches").EnumerateArray().ToArray();
        Assert.Contains(matches, match =>
            match.GetProperty("path").GetString() == "failure-capsule.json" &&
            match.GetProperty("kind").GetString() == "failure");
        Assert.Contains(matches, match =>
            match.GetProperty("path").GetString() == "session-timeline.jsonl" &&
            match.GetProperty("kind").GetString() == "timeline");
        Assert.Contains(matches, match =>
            match.GetProperty("path").GetString() == "logs/failure-logcat.txt" &&
            match.GetProperty("line").GetInt32() == 2);
    }

    [Fact]
    public async Task RunAsync_ReplaySearch_Respects_Limit()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplaySearchArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "search", "--artifacts", replayRoot, "--contains", "not visible", "--limit", "1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("match_count").GetInt32());
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Single(data.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task RunAsync_ReplayCapsule_Returns_Primary_Failure_And_Command_Hints()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "capsule", "--artifacts", replayRoot, "--write-readme", "--write-json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(ResultSchemas.ReplayCapsule, data.GetProperty("schema").GetString());
        Assert.Equal(1, data.GetProperty("session_count").GetInt32());
        Assert.Equal(1, data.GetProperty("failure_count").GetInt32());
        Assert.True(data.GetProperty("has_failure_capsule").GetBoolean());
        Assert.False(data.GetProperty("scenario_draft_available").GetBoolean());
        Assert.Contains("No inspect/view action", data.GetProperty("scenario_draft_reason").GetString(), StringComparison.Ordinal);
        Assert.Equal(Path.Join(replayRoot, "replay-capsule.md"), data.GetProperty("readme_path").GetString());
        Assert.Equal(Path.Join(replayRoot, "replay-capsule-summary.json"), data.GetProperty("json_path").GetString());
        var primaryFailure = data.GetProperty("primary_failure");
        Assert.Equal("login smoke", primaryFailure.GetProperty("scenario").GetString());
        Assert.Equal("wait login button", primaryFailure.GetProperty("step").GetString());
        Assert.Equal("waitVisible", primaryFailure.GetProperty("action").GetString());
        Assert.Equal("not visible", primaryFailure.GetProperty("message").GetString());
        Assert.Contains("--source-path session-timeline.jsonl --sequence 1 --context 3", primaryFailure.GetProperty("source_command").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, data.GetProperty("artifact_counts").GetProperty("screenshots").GetInt32());
        Assert.Equal(1, data.GetProperty("artifact_counts").GetProperty("logs").GetInt32());
        var failureTimeline = data.GetProperty("failure_timeline").EnumerateArray().ToArray();
        Assert.Single(failureTimeline);
        Assert.Equal("scenario_step_failed", failureTimeline[0].GetProperty("type").GetString());
        Assert.Contains("not visible", failureTimeline[0].GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("login smoke", failureTimeline[0].GetProperty("scenario").GetString());
        Assert.Contains("--source-path session-timeline.jsonl --sequence 1 --context 3", failureTimeline[0].GetProperty("source_command").GetString(), StringComparison.Ordinal);
        var manifest = data.GetProperty("artifact_manifest").EnumerateArray().ToArray();
        Assert.Contains(manifest, artifact =>
            artifact.GetProperty("path").GetString() == "session-timeline.jsonl" &&
            artifact.GetProperty("kind").GetString() == "timeline" &&
            artifact.GetProperty("role").GetString() == "session");
        Assert.Contains(manifest, artifact =>
            artifact.GetProperty("path").GetString() == "failure-capsule.json" &&
            artifact.GetProperty("kind").GetString() == "failure_capsule" &&
            artifact.GetProperty("role").GetString() == "failure");
        Assert.Contains(manifest, artifact =>
            artifact.GetProperty("path").GetString() == "failures/wait-login-button.png" &&
            artifact.GetProperty("kind").GetString() == "screenshot" &&
            artifact.GetProperty("role").GetString() == "failure" &&
            artifact.GetProperty("session").GetString() == "failures");
        Assert.Contains(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString()!.Contains("replay search", StringComparison.Ordinal));
        Assert.Contains(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString()!.Contains("replay timeline", StringComparison.Ordinal) &&
            command.GetProperty("command").GetString()!.Contains("--failures --context 3", StringComparison.Ordinal));
        Assert.Contains(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString()!.Contains("replay scrub", StringComparison.Ordinal) &&
            command.GetProperty("command").GetString()!.Contains("--failures --context 3", StringComparison.Ordinal));
        Assert.Contains(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString()!.Contains("replay graph", StringComparison.Ordinal) &&
            command.GetProperty("command").GetString()!.Contains("--write-json --write-markdown", StringComparison.Ordinal));
        Assert.DoesNotContain(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString()!.Contains("scenario-draft", StringComparison.Ordinal));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-capsule.md")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-capsule-summary.json")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "index.md")));
        var readme = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-capsule.md"));
        Assert.Contains("## Primary Failure", readme, StringComparison.Ordinal);
        Assert.Contains("not visible", readme, StringComparison.Ordinal);
        Assert.Contains("Reopen:", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft available: `False`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft reason:", readme, StringComparison.Ordinal);
        Assert.Contains("## Failure Timeline", readme, StringComparison.Ordinal);
        Assert.Contains("scenario_step_failed", readme, StringComparison.Ordinal);
        Assert.Contains("--source-path session-timeline.jsonl --sequence 1 --context 3", readme, StringComparison.Ordinal);
        Assert.Contains("## Artifact Manifest", readme, StringComparison.Ordinal);
        Assert.Contains("failures/wait-login-button.png", readme, StringComparison.Ordinal);
        Assert.Contains("luotsi replay timeline", readme, StringComparison.Ordinal);
        Assert.Contains("luotsi replay scrub", readme, StringComparison.Ordinal);
        Assert.Contains("luotsi replay graph", readme, StringComparison.Ordinal);
        using var jsonSummary = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-capsule-summary.json")));
        Assert.Equal(ResultSchemas.ReplayCapsule, jsonSummary.RootElement.GetProperty("schema").GetString());
    }

    [Fact]
    public async Task RunAsync_ReplayCapsule_Suggests_ScenarioDraft_When_Action_Timeline_Exists()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var inspectRoot = Path.Join(replayRoot, "inspect");
        fileSystem.CreateDirectory(inspectRoot);
        fileSystem.AddFile(Path.Join(inspectRoot, "session-timeline.jsonl"), """
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"tap_text","ok":true,"data":{"text":"Sign in"}}
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "capsule", "--artifacts", replayRoot]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("scenario_draft_available").GetBoolean());
        Assert.Contains("command_result:tap_text", data.GetProperty("scenario_draft_reason").GetString(), StringComparison.Ordinal);
        Assert.Contains(data.GetProperty("suggested_commands").EnumerateArray(), command =>
        {
            var value = command.GetProperty("command").GetString();
            return value is not null &&
                value.Contains("scenario-draft", StringComparison.Ordinal) &&
                value.Contains("--write-json --write-markdown", StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task RunAsync_ReplayCapsule_Links_Existing_Scenario_Draft_Artifacts()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        fileSystem.AddFile(Path.Join(replayRoot, "scenario-draft-summary.json"), """
        {
          "schema": "luotsi-scenario-draft.v1",
          "confidence": "medium",
          "scenario": {
            "steps": [
              { "action": "waitVisible" },
              { "action": "tapText" }
            ]
          },
          "warnings": ["Review selectors."],
          "reviewItems": [
            {
              "severity": "info",
              "category": "selector",
              "stepIndex": 1,
              "message": "Review wait selector.",
              "command": "luotsi replay timeline --artifacts <artifact-root> --source-path session-timeline.jsonl --sequence 1 --context 2"
            },
            {
              "severity": "warning",
              "category": "coordinate",
              "stepIndex": 2,
              "message": "Coordinate tap needs layout metadata."
            }
          ],
          "normalizations": [
            { "kind": "duplicate_wait" }
          ]
        }
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "scenario-draft.md"), "# Luotsi Scenario Draft\n\n## Review Checklist\n");
        fileSystem.AddFile(Path.Join(replayRoot, "draft-scenario.json"), "{}");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "capsule", "--artifacts", replayRoot, "--write-readme"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        var draftArtifacts = data.GetProperty("scenario_draft_artifacts");
        Assert.Equal("scenario-draft-summary.json", draftArtifacts.GetProperty("summary_path").GetString());
        Assert.Equal("scenario-draft.md", draftArtifacts.GetProperty("markdown_path").GetString());
        Assert.Equal("draft-scenario.json", draftArtifacts.GetProperty("scenario_path").GetString());
        var draftSummary = data.GetProperty("scenario_draft_summary");
        Assert.Equal("medium", draftSummary.GetProperty("confidence").GetString());
        Assert.Equal(2, draftSummary.GetProperty("step_count").GetInt32());
        Assert.Equal(1, draftSummary.GetProperty("warning_count").GetInt32());
        Assert.Equal(2, draftSummary.GetProperty("review_item_count").GetInt32());
        Assert.Equal(1, draftSummary.GetProperty("normalization_count").GetInt32());
        Assert.Equal("Review selectors.", draftSummary.GetProperty("warnings")[0].GetString());
        var reviewItems = draftSummary.GetProperty("review_items").EnumerateArray().ToArray();
        Assert.Equal("selector", reviewItems[0].GetProperty("category").GetString());
        Assert.Equal("Review wait selector.", reviewItems[0].GetProperty("message").GetString());
        Assert.Contains("--source-path session-timeline.jsonl", reviewItems[0].GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.Contains(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString()!.Contains("Review Checklist", StringComparison.Ordinal));
        var readme = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-capsule.md"));
        Assert.Contains("Scenario draft summary: `scenario-draft-summary.json`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft review: `scenario-draft.md`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft file: `draft-scenario.json`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft confidence: `medium`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft review items: `2`", readme, StringComparison.Ordinal);
        Assert.Contains("### Scenario Draft Warning Preview", readme, StringComparison.Ordinal);
        Assert.Contains("Review selectors.", readme, StringComparison.Ordinal);
        Assert.Contains("### Scenario Draft Review Preview", readme, StringComparison.Ordinal);
        Assert.Contains("Review wait selector.", readme, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayTimeline_Returns_Filtered_Failure_Events()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "timeline", "--artifacts", replayRoot, "--failures"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(ResultSchemas.ReplayTimeline, data.GetProperty("schema").GetString());
        Assert.Equal(1, data.GetProperty("event_count").GetInt32());
        Assert.Equal(1, data.GetProperty("scanned_file_count").GetInt32());
        var evt = Assert.Single(data.GetProperty("events").EnumerateArray());
        Assert.Equal("session-timeline.jsonl", evt.GetProperty("path").GetString());
        Assert.Equal(1, evt.GetProperty("sequence").GetInt32());
        Assert.Equal("scenario_step_failed", evt.GetProperty("type").GetString());
        Assert.True(evt.GetProperty("failure_relevant").GetBoolean());
        Assert.Contains("error_message=not visible", evt.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayScrub_Returns_Focused_Event_And_Navigation_Commands()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "scrub", "--artifacts", replayRoot, "--failures", "--context", "1", "--write-json", "--write-markdown"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(ResultSchemas.ReplayScrub, data.GetProperty("schema").GetString());
        Assert.Equal(Path.Join(replayRoot, "replay-scrub.json"), data.GetProperty("json_path").GetString());
        Assert.Equal(Path.Join(replayRoot, "replay-scrub.md"), data.GetProperty("markdown_path").GetString());
        Assert.Equal(1, data.GetProperty("focus_index").GetInt32());
        Assert.Equal("scenario_step_failed", data.GetProperty("focus_event").GetProperty("type").GetString());
        Assert.Equal("scenario_run_started", data.GetProperty("previous_event").GetProperty("type").GetString());
        Assert.Equal("scenario_run_ended", data.GetProperty("next_event").GetProperty("type").GetString());
        Assert.Contains(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString()!.Contains("replay timeline", StringComparison.Ordinal) &&
            command.GetProperty("command").GetString()!.Contains("--source-path session-timeline.jsonl --sequence 1", StringComparison.Ordinal));
        Assert.Contains(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString()!.Contains("replay search", StringComparison.Ordinal) &&
            command.GetProperty("command").GetString()!.Contains("not visible", StringComparison.Ordinal));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-scrub.json")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-scrub.md")));
        var markdown = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-scrub.md"));
        Assert.Contains("## Focused Event", markdown, StringComparison.Ordinal);
        Assert.Contains("scenario_step_failed", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi replay timeline --artifacts <artifact-root> --source-path session-timeline.jsonl --sequence 1 --context 5", markdown, StringComparison.Ordinal);
        Assert.Contains("| Property | Value |", markdown, StringComparison.Ordinal);
        Assert.Contains("| error.message | not visible |", markdown, StringComparison.Ordinal);
        Assert.Contains("## Scrub Window", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayTimeline_Filters_By_Detail_Text()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "timeline", "--artifacts", replayRoot, "--contains", "wait login button"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("event_count").GetInt32());
        var evt = Assert.Single(data.GetProperty("events").EnumerateArray());
        Assert.Equal("scenario_step_failed", evt.GetProperty("type").GetString());
        Assert.Contains("step=wait login button", evt.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayTimeline_Filters_By_Source_Path_And_Sequence()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "timeline", "--artifacts", replayRoot, "--source-path", "session-timeline.jsonl", "--sequence", "1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        var evt = Assert.Single(data.GetProperty("events").EnumerateArray());
        Assert.Equal("session-timeline.jsonl", evt.GetProperty("path").GetString());
        Assert.Equal(1, evt.GetProperty("sequence").GetInt32());
        Assert.Equal("scenario_step_failed", evt.GetProperty("type").GetString());
    }

    [Fact]
    public async Task RunAsync_ReplayTimeline_Filters_By_Timestamp_Window()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync([
            "replay",
            "timeline",
            "--artifacts",
            replayRoot,
            "--since",
            "2026-05-18T10:00:01Z",
            "--until",
            "2026-05-18T10:00:02Z"
        ]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("event_count").GetInt32());
        var evt = Assert.Single(data.GetProperty("events").EnumerateArray());
        Assert.Equal("scenario_step_failed", evt.GetProperty("type").GetString());
    }

    [Fact]
    public async Task RunAsync_ReplayTimeline_Includes_Context_Around_Filtered_Events()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "timeline", "--artifacts", replayRoot, "--failures", "--context", "1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(3, data.GetProperty("event_count").GetInt32());
        var events = data.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal("scenario_run_started", events[0].GetProperty("type").GetString());
        Assert.Equal("scenario_step_failed", events[1].GetProperty("type").GetString());
        Assert.Equal("scenario_run_ended", events[2].GetProperty("type").GetString());
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Returns_Semantic_Debug_Seed_Graph()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot, "--write-json", "--write-jsonl", "--write-markdown"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(ResultSchemas.ReplayGraph, data.GetProperty("schema").GetString());
        Assert.True(data.GetProperty("node_count").GetInt32() >= 6);
        Assert.True(data.GetProperty("edge_count").GetInt32() >= 4);
        Assert.True(data.GetProperty("total_node_count").GetInt32() >= data.GetProperty("node_count").GetInt32());
        Assert.True(data.GetProperty("total_edge_count").GetInt32() >= data.GetProperty("edge_count").GetInt32());
        Assert.Equal(data.GetProperty("node_count").GetInt32(), data.GetProperty("matched_node_count").GetInt32());
        Assert.Equal(data.GetProperty("edge_count").GetInt32(), data.GetProperty("matched_edge_count").GetInt32());
        Assert.False(data.GetProperty("truncated").GetBoolean());
        Assert.Equal(200, data.GetProperty("query").GetProperty("limit").GetInt32());
        Assert.True(data.GetProperty("insights").GetArrayLength() >= 1);
        Assert.Contains(data.GetProperty("taxonomy").GetProperty("node_kinds").EnumerateArray(), kind => kind.GetProperty("kind").GetString() == "failure");
        Assert.Contains(data.GetProperty("taxonomy").GetProperty("edge_kinds").EnumerateArray(), kind => kind.GetProperty("kind").GetString() == "transitions_to");
        Assert.Contains(data.GetProperty("taxonomy").GetProperty("query_examples").EnumerateArray(), example => example.GetProperty("kind").GetString() == "neighborhood");
        Assert.Contains("scenario_step_failed", data.GetProperty("agent_summary").GetProperty("what_failed").GetString(), StringComparison.Ordinal);
        Assert.Contains("action-to-failure", data.GetProperty("agent_summary").GetProperty("what_changed").GetString(), StringComparison.Ordinal);
        Assert.Contains("luotsi replay", data.GetProperty("agent_summary").GetProperty("what_can_act_on").GetString(), StringComparison.Ordinal);
        Assert.Contains(data.GetProperty("actions").EnumerateArray(), action => action.GetProperty("kind").GetString() == "scrub_failures");
        Assert.True(data.GetProperty("failure_paths").GetArrayLength() >= 1);
        Assert.Equal(Path.Join(replayRoot, "replay-graph.json"), data.GetProperty("json_path").GetString());
        Assert.Equal(Path.Join(replayRoot, "replay-graph.jsonl"), data.GetProperty("jsonl_path").GetString());
        Assert.Equal(Path.Join(replayRoot, "replay-graph.md"), data.GetProperty("markdown_path").GetString());
        Assert.Contains(data.GetProperty("nodes").EnumerateArray(), node => node.GetProperty("kind").GetString() == "failure");
        Assert.Contains(data.GetProperty("nodes").EnumerateArray(), node => node.GetProperty("kind").GetString() == "artifact");
        Assert.Contains(data.GetProperty("nodes").EnumerateArray(), node => node.GetProperty("kind").GetString() == "action");
        Assert.Equal(1, data.GetProperty("node_kinds").GetProperty("action").GetInt32());
        Assert.Contains(data.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "has_artifact");
        Assert.Contains(data.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "describes_action");
        Assert.Contains(data.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "transitions_to");
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-graph.json")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-graph.jsonl")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-graph.md")));
        Assert.False(fileSystem.FileExists(Path.Join(replayRoot, "replay-timeline.json")));
        Assert.False(fileSystem.FileExists(Path.Join(replayRoot, "replay-timeline.md")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "index.md")));
        var markdown = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-graph.md"));
        Assert.Contains("## Agent Summary", markdown, StringComparison.Ordinal);
        Assert.Contains("## What Failed", markdown, StringComparison.Ordinal);
        Assert.Contains("## What Agents Can Act On", markdown, StringComparison.Ordinal);
        Assert.Contains("## Failure Paths", markdown, StringComparison.Ordinal);
        Assert.Contains("## Transitions", markdown, StringComparison.Ordinal);
        Assert.Contains("## Query Examples", markdown, StringComparison.Ordinal);
        var jsonl = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-graph.jsonl"));
        Assert.Contains("\"type\":\"summary\"", jsonl, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"node\"", jsonl, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"edge\"", jsonl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Reports_Failure_Path()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        var path = Assert.Single(data.GetProperty("failure_paths").EnumerateArray(), item =>
            item.GetProperty("failure_node_id").GetString() == "failure:session-timeline.jsonl:1");
        Assert.Equal("event:session-timeline.jsonl:1", path.GetProperty("failure_event_node_id").GetString());
        Assert.Contains("scenario_step_failed", path.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains(path.GetProperty("node_ids").EnumerateArray(), node => node.GetString() == "failure:session-timeline.jsonl:1");
        Assert.Contains(path.GetProperty("edge_ids").EnumerateArray(), edge => edge.GetString()!.Contains("indicates", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Can_Write_Raw_Jsonl()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot, "--format", "jsonl"]);

        Assert.Equal(0, exitCode);
        Assert.True(console.OutputLines.Count > 3);
        using var summary = JsonDocument.Parse(console.OutputLines[0]);
        Assert.Equal(ResultSchemas.ReplayGraph, summary.RootElement.GetProperty("schema").GetString());
        Assert.Equal("summary", summary.RootElement.GetProperty("type").GetString());
        Assert.True(summary.RootElement.TryGetProperty("agent_summary", out _));
        Assert.Contains(console.OutputLines, line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.GetProperty("type").GetString() == "failure_path";
        });
        Assert.Contains(console.OutputLines, line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.GetProperty("type").GetString() == "node";
        });
        Assert.Contains(console.OutputLines, line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.GetProperty("type").GetString() == "edge";
        });
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Classifies_Action_To_Failure_Transitions()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot, "--edge-kind", "transitions_to"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        var transition = Assert.Single(data.GetProperty("edges").EnumerateArray(), edge =>
            edge.GetProperty("properties").TryGetProperty("category", out var category) &&
            category.GetString() == "action_to_failure");
        Assert.Equal("scenario_run_started", transition.GetProperty("properties").GetProperty("from_type").GetString());
        Assert.Equal("scenario_step_failed", transition.GetProperty("properties").GetProperty("to_type").GetString());
        Assert.Contains(data.GetProperty("insights").EnumerateArray(), insight => insight.GetProperty("kind").GetString() == "transition");
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Filters_By_Failure_And_Node_Kind()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot, "--failed", "--node-kind", "failure", "--limit", "10"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("query").GetProperty("failed_only").GetBoolean());
        Assert.Equal("failure", data.GetProperty("query").GetProperty("node_kind").GetString());
        Assert.Equal(10, data.GetProperty("query").GetProperty("limit").GetInt32());
        Assert.True(data.GetProperty("node_count").GetInt32() <= 10);
        Assert.Contains(data.GetProperty("nodes").EnumerateArray(), node => node.GetProperty("kind").GetString() == "failure");
        Assert.True(data.GetProperty("total_node_count").GetInt32() > data.GetProperty("node_count").GetInt32());
        Assert.False(data.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Reports_Truncated_Query_When_Limit_Caps_Matches()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot, "--limit", "1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("node_count").GetInt32());
        Assert.Equal(1, data.GetProperty("edge_count").GetInt32());
        Assert.True(data.GetProperty("matched_node_count").GetInt32() > data.GetProperty("node_count").GetInt32());
        Assert.True(data.GetProperty("matched_edge_count").GetInt32() > data.GetProperty("edge_count").GetInt32());
        Assert.True(data.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Returns_Node_Neighborhood()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot, "--node", "failure:session-timeline.jsonl:1", "--depth", "1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("failure:session-timeline.jsonl:1", data.GetProperty("query").GetProperty("node").GetString());
        Assert.Equal(1, data.GetProperty("query").GetProperty("depth").GetInt32());
        Assert.Contains(data.GetProperty("nodes").EnumerateArray(), node => node.GetProperty("id").GetString() == "failure:session-timeline.jsonl:1");
        Assert.Contains(data.GetProperty("nodes").EnumerateArray(), node => node.GetProperty("id").GetString() == "event:session-timeline.jsonl:1");
        Assert.Contains(data.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "indicates");
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Promotes_Inspect_Events_To_Semantic_Nodes()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/replay-graph-inspect-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"session_started","session_id":"inspect-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"wait_visible","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","data":{"text":"Sign in"}}
        {"type":"command_result","session_id":"inspect-session","id":"2","command":"take_screenshot","ok":true,"started_at":"2026-05-18T10:00:03Z","ended_at":"2026-05-18T10:00:04Z","data":{"label":"after-login"}}
        {"type":"command_result","session_id":"inspect-session","id":"3","command":"telemetry_tail","ok":true,"started_at":"2026-05-18T10:00:05Z","ended_at":"2026-05-18T10:00:06Z","data":{"event":"step","step":"STEP_IDLE"}}
        {"type":"session_ended","session_id":"inspect-session","ended_at":"2026-05-18T10:00:09Z","reason":"client_exit"}
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "session-replay.json"), """
        {
          "schema": "luotsi-session-replay.v1",
          "sessionKind": "inspect",
          "sessionId": "inspect-session",
          "startedAt": "2026-05-18T10:00:00Z",
          "endedAt": "2026-05-18T10:00:09Z",
          "reason": "client_exit",
          "exitCode": 0,
          "target": "emulator-5554",
          "timelineFileName": "session-timeline.jsonl",
          "eventCount": 5,
          "eventTypes": ["session_started", "command_result", "session_ended"]
        }
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("node_kinds").GetProperty("action").GetInt32() >= 3);
        Assert.True(data.GetProperty("node_kinds").GetProperty("selector").GetInt32() >= 1);
        Assert.Equal(1, data.GetProperty("node_kinds").GetProperty("screen_state").GetInt32());
        Assert.Equal(1, data.GetProperty("node_kinds").GetProperty("telemetry_signal").GetInt32());
        Assert.Contains(data.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "mentions_selector");
        Assert.Contains(data.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "observes_screen");
        Assert.Contains(data.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "observes_telemetry");
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Includes_ScenarioDraft_Provenance()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/replay-graph-draft-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"session_started","session_id":"inspect-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"tap_text","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","data":{"text":"Sign in"}}
        {"type":"session_ended","session_id":"inspect-session","ended_at":"2026-05-18T10:00:09Z","reason":"client_exit"}
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "session-replay.json"), """
        {
          "schema": "luotsi-session-replay.v1",
          "sessionKind": "inspect",
          "sessionId": "inspect-session",
          "startedAt": "2026-05-18T10:00:00Z",
          "endedAt": "2026-05-18T10:00:09Z",
          "reason": "client_exit",
          "exitCode": 0,
          "timelineFileName": "session-timeline.jsonl",
          "eventCount": 3,
          "eventTypes": ["session_started", "command_result", "session_ended"]
        }
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "scenario-draft-summary.json"), """
        {
          "schema": "luotsi-scenario-draft.v1",
          "artifact_root": "/tmp/replay-graph-draft-root",
          "output": "/tmp/draft.json",
          "confidence": "medium",
          "scenario": {
            "name": "draft from replay",
            "steps": [
              { "name": "tap Sign in", "action": "tapText", "text": "Sign in" }
            ]
          },
          "source_summaries": [
            { "source": "inspect_command", "step_count": 1, "normalization_count": 1, "event_types": ["command_result"], "confidence": "medium" }
          ],
          "step_origins": [
            { "step_index": 1, "source": "inspect_command", "event_type": "command_result", "command": "tap_text", "detail": "tap_text", "confidence": "medium" }
          ],
          "normalizations": [
            { "kind": "duplicate_wait", "detail": "Dropped adjacent duplicate waitVisible for `Sign in`.", "source": "screen_delta", "event_type": "screen_delta", "confidence": "medium" }
          ]
        }
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("node_kinds").GetProperty("scenario_draft").GetInt32());
        Assert.Equal(1, data.GetProperty("node_kinds").GetProperty("generated_step").GetInt32());
        Assert.True(data.GetProperty("node_kinds").GetProperty("draft_source").GetInt32() >= 1);
        Assert.Equal(1, data.GetProperty("node_kinds").GetProperty("draft_normalization").GetInt32());
        Assert.Contains(data.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "generates_step");
        Assert.Contains(data.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "derived_from");
        Assert.Contains(data.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "uses_source");
        Assert.Contains(data.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "applies_normalization");
    }

    [Fact]
    public async Task RunAsync_ReplayCluster_Groups_Failures_By_Normalized_Shape()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/replay-cluster-root";
        fileSystem.CreateDirectory(replayRoot);
        SeedClusterFailure(fileSystem, replayRoot, "run-a", "run-a-session", "2026-05-18T10:00:00Z", "not visible after 15 seconds");
        SeedClusterFailure(fileSystem, replayRoot, "run-b", "run-b-session", "2026-05-18T11:00:00Z", "not visible after 30 seconds");
        SeedClusterFailure(fileSystem, replayRoot, "run-c", "run-c-session", "2026-05-18T12:00:00Z", "permission denied");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "cluster", "--artifacts", replayRoot, "--write-json", "--write-markdown"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.True(exitCode == 0, string.Join(Environment.NewLine, console.OutputLines.Concat(console.ErrorLines)));
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(ResultSchemas.ReplayClusters, data.GetProperty("schema").GetString());
        Assert.Equal(3, data.GetProperty("session_count").GetInt32());
        Assert.Equal(3, data.GetProperty("failure_count").GetInt32());
        Assert.Equal(2, data.GetProperty("cluster_count").GetInt32());
        var clusters = data.GetProperty("clusters").EnumerateArray().ToArray();
        Assert.Equal(2, clusters[0].GetProperty("count").GetInt32());
        Assert.Equal("selector_or_screen_state", clusters[0].GetProperty("category").GetString());
        Assert.Equal("waitVisible", clusters[0].GetProperty("action").GetString());
        Assert.Contains("not visible after 30 seconds", clusters[0].GetProperty("message").GetString(), StringComparison.Ordinal);
        var hints = clusters[0].GetProperty("hints").EnumerateArray().ToArray();
        Assert.Contains(hints, hint => hint.GetProperty("kind").GetString() == "same_failure_shape");
        Assert.Contains(hints, hint => hint.GetProperty("kind").GetString() == "likely_repeated_selector_or_screen_state_failure");
        Assert.Contains(hints, hint =>
            hint.GetProperty("kind").GetString() == "open_latest_replay" &&
            hint.GetProperty("command").GetString() == "luotsi replay open --artifacts /tmp/replay-cluster-root\\run-b");
        Assert.Contains(hints, hint =>
            hint.GetProperty("kind").GetString() == "search_latest_failure_text" &&
            hint.GetProperty("command").GetString()!.Contains("not visible after 30 seconds", StringComparison.Ordinal));
        Assert.Equal(Path.Join(replayRoot, "replay-clusters.json"), data.GetProperty("json_path").GetString());
        Assert.Equal(Path.Join(replayRoot, "replay-clusters.md"), data.GetProperty("markdown_path").GetString());
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-clusters.json")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-clusters.md")));
    }

    [Fact]
    public async Task RunAsync_ReplayTimeline_FormatJsonl_Writes_Summary_And_Event_Lines()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "timeline", "--artifacts", replayRoot, "--failures", "--format", "jsonl", "--write-json", "--write-jsonl", "--write-markdown"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, console.OutputLines.Count);
        using var summaryLine = JsonDocument.Parse(console.OutputLines[0]);
        using var eventLine = JsonDocument.Parse(console.OutputLines[1]);
        Assert.Equal(ResultSchemas.ReplayTimeline, summaryLine.RootElement.GetProperty("schema").GetString());
        Assert.Equal("summary", summaryLine.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, summaryLine.RootElement.GetProperty("event_count").GetInt32());
        Assert.Equal(ResultSchemas.ReplayTimeline, eventLine.RootElement.GetProperty("schema").GetString());
        Assert.Equal("event", eventLine.RootElement.GetProperty("type").GetString());
        Assert.Equal("scenario_step_failed", eventLine.RootElement.GetProperty("event").GetProperty("type").GetString());
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-timeline.json")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-timeline.jsonl")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-timeline.md")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "index.md")));
        using var jsonArtifact = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-timeline.json")));
        Assert.Equal(ResultSchemas.ReplayTimeline, jsonArtifact.RootElement.GetProperty("schema").GetString());
        var jsonlArtifact = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-timeline.jsonl"));
        var jsonlLines = jsonlArtifact.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var jsonlSummaryLine = JsonDocument.Parse(jsonlLines[0]);
        using var jsonlEventLine = JsonDocument.Parse(jsonlLines[1]);
        Assert.Equal("summary", jsonlSummaryLine.RootElement.GetProperty("type").GetString());
        Assert.Equal("event", jsonlEventLine.RootElement.GetProperty("type").GetString());
        var markdownArtifact = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-timeline.md"));
        Assert.Contains("# Luotsi Replay Timeline", markdownArtifact, StringComparison.Ordinal);
        Assert.Contains("scenario_step_failed", markdownArtifact, StringComparison.Ordinal);
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
    public async Task LabClaim_Leases_Selected_Device_And_Release_Removes_Lease()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var claimExitCode = await app.RunAsync(["lab", "claim", "--device-query", "model=Pixel_9", "--owner", "ci-job-1", "--ttl-sec", "60"]);
        using var claimEnvelope = JsonDocument.Parse(console.OutputLines[0]);

        Assert.True(claimExitCode == 0, string.Join(Environment.NewLine, console.OutputLines.Concat(console.ErrorLines)));
        var claimData = claimEnvelope.RootElement.GetProperty("data");
        Assert.Equal("usb-1", claimData.GetProperty("serial").GetString());
        Assert.Equal("ci-job-1", claimData.GetProperty("owner").GetString());
        var leaseId = claimData.GetProperty("lease_id").GetString()!;
        Assert.True(fileSystem.FileExists(claimData.GetProperty("lease_file").GetString()!));

        var statusExitCode = await app.RunAsync(["lab", "status", "--device-query", "model=Pixel_9"]);
        using var statusEnvelope = JsonDocument.Parse(console.OutputLines[1]);

        Assert.Equal(0, statusExitCode);
        var decision = statusEnvelope.RootElement.GetProperty("data").GetProperty("decisions")[0];
        Assert.False(decision.GetProperty("selected").GetBoolean());
        Assert.Contains("leased by ci-job-1", decision.GetProperty("reason").GetString(), StringComparison.Ordinal);

        var leasesExitCode = await app.RunAsync(["lab", "leases"]);
        using var leasesEnvelope = JsonDocument.Parse(console.OutputLines[2]);

        Assert.Equal(0, leasesExitCode);
        Assert.Equal(1, leasesEnvelope.RootElement.GetProperty("data").GetProperty("count").GetInt32());

        var doctorExitCode = await app.RunAsync(["lab", "doctor", "--device-query", "model=Pixel_9"]);
        using var doctorEnvelope = JsonDocument.Parse(console.OutputLines[3]);

        Assert.Equal(0, doctorExitCode);
        Assert.Contains("lab leases", doctorEnvelope.RootElement.GetProperty("data").GetProperty("recommended_actions")[0].GetString(), StringComparison.Ordinal);

        var releaseExitCode = await app.RunAsync(["lab", "release", "--lease", leaseId]);
        using var releaseEnvelope = JsonDocument.Parse(console.OutputLines[4]);

        Assert.Equal(0, releaseExitCode);
        Assert.True(releaseEnvelope.RootElement.GetProperty("data").GetProperty("released").GetBoolean());
        var leasesAfterReleaseExitCode = await app.RunAsync(["lab", "leases"]);
        using var leasesAfterReleaseEnvelope = JsonDocument.Parse(console.OutputLines[5]);

        Assert.Equal(0, leasesAfterReleaseExitCode);
        Assert.Equal(0, leasesAfterReleaseEnvelope.RootElement.GetProperty("data").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task LabClaim_Does_Not_Overwrite_Active_Lease_For_Same_Serial()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-19T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var store = new LabLeaseStore(fileSystem, timeProvider);

        var firstLease = await store.ClaimAsync("usb-1", "ci-job-1", 60);
        var error = await Assert.ThrowsAsync<UsageException>(() => store.ClaimAsync("usb-1", "ci-job-2", 60));

        Assert.Contains("already leased by ci-job-1", error.Message, StringComparison.Ordinal);

        using var persistedLease = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(firstLease.LeaseFile));
        Assert.Equal("ci-job-1", persistedLease.RootElement.GetProperty("owner").GetString());
    }

    [Fact]
    public async Task LabRelease_BySerial_Removes_Lease_And_Unblocks_Plan()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var claimExitCode = await app.RunAsync(["lab", "claim", "--device-query", "model=Pixel_9", "--owner", "ci-job-1", "--ttl-sec", "60"]);
        var blockedPlanExitCode = await app.RunAsync(["lab", "plan", "--device-query", "model=Pixel_9"]);
        using var blockedPlanEnvelope = JsonDocument.Parse(console.OutputLines[1]);

        Assert.Equal(0, claimExitCode);
        Assert.Equal(0, blockedPlanExitCode);
        var blockedPlan = blockedPlanEnvelope.RootElement.GetProperty("data");
        Assert.Equal("blocked", blockedPlan.GetProperty("status").GetString());
        Assert.Equal("luotsi lab release --serial <serial>", blockedPlan.GetProperty("recommended_commands")[1].GetString());

        var releaseExitCode = await app.RunAsync(["lab", "release", "--serial", "usb-1"]);
        using var releaseEnvelope = JsonDocument.Parse(console.OutputLines[2]);

        Assert.Equal(0, releaseExitCode);
        var release = releaseEnvelope.RootElement.GetProperty("data");
        Assert.True(release.GetProperty("released").GetBoolean());
        Assert.Equal("usb-1", release.GetProperty("serial").GetString());

        var readyPlanExitCode = await app.RunAsync(["lab", "plan", "--device-query", "model=Pixel_9"]);
        using var readyPlanEnvelope = JsonDocument.Parse(console.OutputLines[3]);

        Assert.Equal(0, readyPlanExitCode);
        Assert.Equal("ready", readyPlanEnvelope.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task LabExtend_BySerial_Renews_Active_Lease()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var claimExitCode = await app.RunAsync(["lab", "claim", "--device-query", "model=Pixel_9", "--owner", "ci-job-1", "--ttl-sec", "60"]);
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var extendExitCode = await app.RunAsync(["lab", "extend", "--serial", "usb-1", "--ttl-sec", "120"]);
        using var extendEnvelope = JsonDocument.Parse(console.OutputLines[1]);

        Assert.Equal(0, claimExitCode);
        Assert.Equal(0, extendExitCode);
        var extend = extendEnvelope.RootElement.GetProperty("data");
        Assert.True(extend.GetProperty("extended").GetBoolean());
        Assert.Equal("usb-1", extend.GetProperty("serial").GetString());
        Assert.Equal("2026-05-15T12:01:00+00:00", extend.GetProperty("previous_expires_at").GetString());
        Assert.Equal("2026-05-15T12:02:30+00:00", extend.GetProperty("expires_at").GetString());

        var leasesExitCode = await app.RunAsync(["lab", "leases"]);
        using var leasesEnvelope = JsonDocument.Parse(console.OutputLines[2]);

        Assert.Equal(0, leasesExitCode);
        var lease = leasesEnvelope.RootElement.GetProperty("data").GetProperty("leases")[0];
        Assert.Equal("2026-05-15T12:02:30+00:00", lease.GetProperty("expires_at").GetString());
    }

    [Fact]
    public async Task LabQuarantine_Marks_Device_Unallocatable_Until_Unquarantined()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var quarantineExitCode = await app.RunAsync(["lab", "quarantine", "--device-query", "model=Pixel_9", "--reason", "screen flickers", "--owner", "lab-admin"]);
        using var quarantineEnvelope = JsonDocument.Parse(console.OutputLines[0]);

        Assert.Equal(0, quarantineExitCode);
        var quarantine = quarantineEnvelope.RootElement.GetProperty("data");
        Assert.Equal("usb-1", quarantine.GetProperty("serial").GetString());
        Assert.Equal("screen flickers", quarantine.GetProperty("reason").GetString());
        Assert.True(fileSystem.FileExists(quarantine.GetProperty("quarantine_file").GetString()!));

        var statusExitCode = await app.RunAsync(["lab", "status", "--device-query", "model=Pixel_9"]);
        using var statusEnvelope = JsonDocument.Parse(console.OutputLines[1]);

        Assert.Equal(0, statusExitCode);
        var decision = statusEnvelope.RootElement.GetProperty("data").GetProperty("decisions")[0];
        Assert.False(decision.GetProperty("selected").GetBoolean());
        Assert.Contains("quarantined by lab-admin", decision.GetProperty("reason").GetString(), StringComparison.Ordinal);

        var doctorExitCode = await app.RunAsync(["lab", "doctor", "--device-query", "model=Pixel_9"]);
        using var doctorEnvelope = JsonDocument.Parse(console.OutputLines[2]);

        Assert.Equal(0, doctorExitCode);
        Assert.Contains("lab quarantines", doctorEnvelope.RootElement.GetProperty("data").GetProperty("recommended_actions")[0].GetString(), StringComparison.Ordinal);

        var unquarantineExitCode = await app.RunAsync(["lab", "unquarantine", "--serial", "usb-1"]);
        using var unquarantineEnvelope = JsonDocument.Parse(console.OutputLines[3]);

        Assert.Equal(0, unquarantineExitCode);
        Assert.True(unquarantineEnvelope.RootElement.GetProperty("data").GetProperty("released").GetBoolean());
    }

    [Fact]
    public async Task LabPlan_Explains_Selected_And_Blocked_Device()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var planExitCode = await app.RunAsync(["lab", "plan", "--device-query", "model=Pixel_9"]);
        using var planEnvelope = JsonDocument.Parse(console.OutputLines[0]);

        Assert.Equal(0, planExitCode);
        var plan = planEnvelope.RootElement.GetProperty("data");
        Assert.Equal("ready", plan.GetProperty("status").GetString());
        Assert.Equal("usb-1", plan.GetProperty("selected_serial").GetString());
        Assert.Contains("would be selected", plan.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("luotsi lab claim", plan.GetProperty("recommended_commands")[0].GetString(), StringComparison.Ordinal);
        Assert.Equal("luotsi run --path <scenarios> --claim-device --device-query model=Pixel_9", plan.GetProperty("recommended_commands")[1].GetString());

        var claimExitCode = await app.RunAsync(["lab", "claim", "--device-query", "model=Pixel_9", "--owner", "ci-job-1"]);
        var blockedPlanExitCode = await app.RunAsync(["lab", "plan", "--device-query", "model=Pixel_9"]);
        using var blockedPlanEnvelope = JsonDocument.Parse(console.OutputLines[2]);

        Assert.Equal(0, claimExitCode);
        Assert.True(blockedPlanExitCode == 0, string.Join(Environment.NewLine, console.OutputLines.Concat(console.ErrorLines)));
        var blockedPlan = blockedPlanEnvelope.RootElement.GetProperty("data");
        Assert.Equal("blocked", blockedPlan.GetProperty("status").GetString());
        Assert.False(blockedPlan.TryGetProperty("selected_serial", out _));
        Assert.Contains("leased by ci-job-1", blockedPlan.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("luotsi lab leases", blockedPlan.GetProperty("recommended_commands")[0].GetString());
        Assert.Equal("luotsi lab release --serial <serial>", blockedPlan.GetProperty("recommended_commands")[1].GetString());
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

    private static string SeedReplayCapsuleArtifacts(FakeFileSystem fileSystem)
    {
        var replayRoot = "/tmp/replay-capsule-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.CreateDirectory(Path.Join(replayRoot, "failures"));
        fileSystem.CreateDirectory(Path.Join(replayRoot, "logs"));
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"scenario_run_started","started_at":"2026-05-18T10:00:00Z"}
        {"type":"scenario_step_failed","occurred_at":"2026-05-18T10:00:02Z","scenario":"login smoke","step":"wait login button","action":"waitVisible","error":{"message":"not visible"}}
        {"type":"scenario_run_ended","ended_at":"2026-05-18T10:00:03Z","status":"failed"}
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "session-replay.json"), """
        {
          "schema": "luotsi-session-replay.v1",
          "sessionKind": "run",
          "sessionId": "run-20260518100000000",
          "startedAt": "2026-05-18T10:00:00Z",
          "endedAt": "2026-05-18T10:00:03Z",
          "reason": "failed",
          "exitCode": 1,
          "target": "emulator-5554",
          "timelineFileName": "session-timeline.jsonl",
          "eventCount": 3,
          "eventTypes": ["scenario_run_started", "scenario_step_failed", "scenario_run_ended"]
        }
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "failure-capsule.json"), """
        {
          "schema": "luotsi-failure-capsule.v1",
          "generatedAt": "2026-05-18T10:00:03Z",
          "path": "/tmp/replay-capsule-root",
          "status": "failed",
          "replayMetadataPath": "session-replay.json",
          "replayTimelinePath": "session-timeline.jsonl",
          "reports": {
            "jsonPath": "scenario-results.json",
            "junitPath": "junit.xml"
          },
          "scenarios": [
            {
              "scenario": "login smoke",
              "scenarioId": "scenarios/login.json::login smoke",
              "status": "failed",
              "file": "scenarios/login.json",
              "failedStep": {
                "index": 1,
                "name": "wait login button",
                "action": "waitVisible",
                "phase": "main"
              },
              "artifacts": [
                {
                  "kind": "screenshot",
                  "path": "failures/wait-login-button.png",
                  "stepIndex": 1,
                  "stepName": "wait login button"
                },
                {
                  "kind": "logcat",
                  "path": "logs/failure-logcat.txt",
                  "stepIndex": 1,
                  "stepName": "wait login button"
                }
              ],
              "error": {
                "type": "System.InvalidOperationException",
                "message": "not visible",
                "category": "selector_or_screen_state"
              }
            }
          ],
          "screenshots": [
            {
              "kind": "screenshot",
              "path": "failures/wait-login-button.png",
              "stepIndex": 1,
              "stepName": "wait login button"
            }
          ],
          "logcat": [
            {
              "kind": "logcat",
              "path": "logs/failure-logcat.txt",
              "stepIndex": 1,
              "stepName": "wait login button"
            }
          ],
          "hierarchies": [],
          "screenStates": [],
          "failureBundles": []
        }
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "failures", "wait-login-button.png"), "png");
        fileSystem.AddFile(Path.Join(replayRoot, "logs", "failure-logcat.txt"), "not visible");
        fileSystem.AddFile(Path.Join(replayRoot, "scenario-results.json"), "{}");
        fileSystem.AddFile(Path.Join(replayRoot, "junit.xml"), "<testsuite />");
        return replayRoot;
    }

    private static void SeedClusterFailure(
        FakeFileSystem fileSystem,
        string replayRoot,
        string directory,
        string sessionId,
        string startedAt,
        string errorMessage)
    {
        var runRoot = Path.Join(replayRoot, directory);
        fileSystem.CreateDirectory(runRoot);
        fileSystem.AddFile(Path.Join(runRoot, "session-timeline.jsonl"), $$$"""
        {"type":"scenario_run_started","started_at":"{{{startedAt}}}"}
        {"type":"scenario_step_failed","occurred_at":"{{{startedAt}}}","scenario":"login smoke","step":"wait login button","action":"waitVisible","error":{"message":"{{{errorMessage}}}"}}
        {"type":"scenario_run_ended","ended_at":"{{{startedAt}}}","status":"failed"}
        """);
        fileSystem.AddFile(Path.Join(runRoot, "session-replay.json"), $$$"""
        {
          "schema": "luotsi-session-replay.v1",
          "sessionKind": "run",
          "sessionId": "{{{sessionId}}}",
          "startedAt": "{{{startedAt}}}",
          "endedAt": "{{{startedAt}}}",
          "reason": "failed",
          "exitCode": 1,
          "target": "emulator-5554",
          "timelineFileName": "session-timeline.jsonl",
          "eventCount": 3,
          "eventTypes": ["scenario_run_started", "scenario_step_failed", "scenario_run_ended"]
        }
        """);
        fileSystem.AddFile(Path.Join(runRoot, "failure-capsule.json"), $$$"""
        {
          "schema": "luotsi-failure-capsule.v1",
          "generatedAt": "{{{startedAt}}}",
          "path": "{{{runRoot}}}",
          "status": "failed",
          "replayMetadataPath": "session-replay.json",
          "replayTimelinePath": "session-timeline.jsonl",
          "reports": {
            "jsonPath": null,
            "junitPath": null
          },
          "scenarios": [
            {
              "scenario": "login smoke",
              "scenarioId": "scenarios/login.json::login smoke",
              "status": "failed",
              "file": "scenarios/login.json",
              "failedStep": {
                "index": 1,
                "name": "wait login button",
                "action": "waitVisible",
                "phase": "main"
              },
              "artifacts": [],
              "error": {
                "type": "System.InvalidOperationException",
                "message": "{{{errorMessage}}}",
                "category": "selector_or_screen_state"
              }
            }
          ],
          "screenshots": [],
          "logcat": [],
          "hierarchies": [],
          "screenStates": [],
          "failureBundles": []
        }
        """);
    }

    private static string SeedReplaySearchArtifacts(FakeFileSystem fileSystem)
    {
        var replayRoot = "/tmp/replay-search-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.CreateDirectory(Path.Join(replayRoot, "logs"));
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"scenario_step_failed","scenario":"login","step":"waitVisible","error":{"message":"not visible"}}
        {"type":"scenario_run_ended","status":"failed"}
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "failure-capsule.json"), """
        {
          "schema": "luotsi-failure-capsule.v1",
          "scenarios": [
            {
              "scenario": "login",
              "error": {
                "message": "not visible"
              }
            }
          ]
        }
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "logs", "failure-logcat.txt"), """
        first line
        E/App: button not visible yet
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "screenshot.png"), "not visible but binary-like extension should not be searched");
        return replayRoot;
    }

    private static string SeedInspectReplayDraftArtifacts(FakeFileSystem fileSystem)
    {
        var replayRoot = "/tmp/inspect-replay-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"session_started","session_id":"inspect-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"wait_visible","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","data":{"text":"Sign in"}}
        {"type":"command_result","session_id":"inspect-session","id":"2","command":"tap_text","ok":true,"started_at":"2026-05-18T10:00:03Z","ended_at":"2026-05-18T10:00:04Z","data":{"text":"Sign in"}}
        {"type":"command_result","session_id":"inspect-session","id":"3","command":"type_text","ok":true,"started_at":"2026-05-18T10:00:05Z","ended_at":"2026-05-18T10:00:06Z","data":{"text":"hello@example.com"}}
        {"type":"command_result","session_id":"inspect-session","id":"4","command":"take_screenshot","ok":true,"started_at":"2026-05-18T10:00:07Z","ended_at":"2026-05-18T10:00:08Z","data":{"label":"after-login"}}
        {"type":"scenario_step_passed","session_id":"inspect-session","action":"waitVisible","step":"original scenario event without args"}
        {"type":"session_ended","session_id":"inspect-session","ended_at":"2026-05-18T10:00:09Z","reason":"client_exit"}
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "session-replay.json"), """
        {
          "schema": "luotsi-session-replay.v1",
          "sessionKind": "inspect",
          "sessionId": "inspect-session",
          "startedAt": "2026-05-18T10:00:00Z",
          "endedAt": "2026-05-18T10:00:09Z",
          "reason": "client_exit",
          "exitCode": 0,
          "target": "emulator-5554",
          "timelineFileName": "session-timeline.jsonl",
          "eventCount": 6,
          "eventTypes": ["session_started", "command_result", "session_ended"]
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
