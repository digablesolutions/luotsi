using System.Text.Json;
using System.Runtime.InteropServices;
using System.IO.Compression;
using Luotsi.Cli.Artifacts;
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
    public async Task RunAsync_Help_Command_Writes_Lab_Inventory_And_Admission_Flags()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["help", "lab"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(console.OutputLines);
        Assert.Single(console.ErrorLines);
        Assert.Contains("luotsi lab inventory list", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("--device-pool <pool>", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("--require-capabilities <csv>", console.ErrorLines[0], StringComparison.Ordinal);
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
        Assert.Contains("luotsi replay open --last --artifacts artifacts --dry-run", console.ErrorLines[0], StringComparison.Ordinal);
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
        Assert.Contains("luotsi replay open --last [--artifacts <directory>] [--dry-run] [--write-json] [--write-markdown]", console.ErrorLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Help_Command_Writes_Artifacts_Topic()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["help", "artifacts"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(console.OutputLines);
        Assert.Single(console.ErrorLines);
        Assert.Contains("Luotsi help: artifacts", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("luotsi artifacts list [--artifacts <directory>] [--limit 20]", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("luotsi artifacts info (<artifact-root-or-run-id> | --last [--artifacts <directory>])", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("luotsi artifacts open (<artifact-root-or-run-id> | --last [--artifacts <directory>]) [--dry-run]", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("luotsi artifacts pack <artifact-root-or-run-id>", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("[--redact lab-safe|off]", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("luotsi artifacts verify <artifact.zip>", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("[--require-lab-safe]", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("luotsi artifacts unpack <artifact.zip>", console.ErrorLines[0], StringComparison.Ordinal);
        Assert.Contains("luotsi-artifact-package.json", console.ErrorLines[0], StringComparison.Ordinal);
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
        Assert.Equal("installed", data.GetProperty("view_extras").GetString());
        Assert.True(data.GetProperty("ffmpeg_staged").GetBoolean());
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
        Assert.True(envelope.RootElement.GetProperty("data").TryGetProperty("detached_installer_stdout_log", out _));
        Assert.True(envelope.RootElement.GetProperty("data").TryGetProperty("detached_installer_stderr_log", out _));
        var call = Assert.Single(processRunner.Calls);
        Assert.Contains("Start-Process", string.Join(" ", call.Args), StringComparison.Ordinal);
        Assert.Contains("-EncodedCommand", string.Join(" ", call.Args), StringComparison.Ordinal);
        Assert.DoesNotContain("Wait-Process", string.Join(" ", call.Args), StringComparison.Ordinal);
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
        Assert.Contains("--output-dir <directory>", console.ErrorLines[0], StringComparison.Ordinal);
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
        var commands = envelope.RootElement.GetProperty("data").GetProperty("commands").EnumerateArray().ToArray();
        Assert.Contains(commands, command =>
            command.GetProperty("kind").GetString() == "open_replay_front_door" &&
            command.GetProperty("command").GetString() == $"luotsi replay open --artifacts {replayRoot}");
        Assert.Contains(commands, command => command.GetProperty("kind").GetString() == "graph_failures");
        Assert.Contains(commands, command => command.GetProperty("kind").GetString() == "cluster_failures");

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
        Assert.Contains(summaryLine.RootElement.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "open_replay_front_door");
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
        Assert.False(data.TryGetProperty("json_path", out _));
        Assert.False(data.TryGetProperty("markdown_path", out _));
        Assert.Equal(1, data.GetProperty("session_count").GetInt32());
        Assert.Equal(1, data.GetProperty("failure_count").GetInt32());
        Assert.Equal("scrub_failure", data.GetProperty("recommended_next_action").GetProperty("kind").GetString());
        Assert.Contains(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "capsule" &&
            command.GetProperty("command").GetString() == $"luotsi replay capsule --artifacts {replayRoot} --write-readme --write-json");
        Assert.Contains(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "scrub" &&
            command.GetProperty("command").GetString() == $"luotsi replay scrub --artifacts {replayRoot} --failures --context 3 --write-markdown");
        Assert.Contains(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "graph");
        Assert.Contains(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "scenario_draft");
        Assert.Equal("error=transport: Unexpected end of stream", data.GetProperty("primary_failure").GetProperty("message").GetString());
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
    public async Task RunAsync_ReplayOpen_WriteArtifacts_PersistsFrontDoorSummary()
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

        var exitCode = await app.RunAsync(["replay", "open", "--artifacts", replayRoot, "--dry-run", "--write-json", "--write-markdown"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(Path.Join(replayRoot, "replay-open-summary.json"), data.GetProperty("json_path").GetString());
        Assert.Equal(Path.Join(replayRoot, "replay-open.md"), data.GetProperty("markdown_path").GetString());
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-open-summary.json")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-open.md")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "index.html")));
        var markdown = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-open.md"));
        Assert.Contains("# Luotsi Replay Front Door", markdown, StringComparison.Ordinal);
        Assert.Contains("Scrub the failure window", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi replay graph", markdown, StringComparison.Ordinal);
        var indexMarkdown = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "index.md"));
        Assert.Contains("[replay-open.md](replay-open.md)", indexMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayOpen_WithSessionWithoutFailure_RecommendsCapsule()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        var replayRoot = SeedReplaySummaryArtifactsWithoutFailure(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            ProcessRunner = processRunner,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "open", "--artifacts", replayRoot, "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("session_count").GetInt32());
        Assert.Equal(0, data.GetProperty("failure_count").GetInt32());
        Assert.False(data.TryGetProperty("primary_failure", out _));
        Assert.Equal("write_capsule", data.GetProperty("recommended_next_action").GetProperty("kind").GetString());
        Assert.Contains(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "capsule");
        Assert.Contains(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "timeline");
        Assert.DoesNotContain(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "scrub");
        Assert.DoesNotContain(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "cluster");
        Assert.Empty(processRunner.Calls);
    }

    [Fact]
    public async Task RunAsync_ReplayOpen_WithoutReplayMetadata_RecommendsArtifactInspection()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        var replayRoot = "/tmp/replay-empty-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "logcat.txt"), "ordinary artifact");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            ProcessRunner = processRunner,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "open", "--artifacts", replayRoot, "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("session_count").GetInt32());
        Assert.Equal(0, data.GetProperty("failure_count").GetInt32());
        Assert.False(data.TryGetProperty("primary_failure", out _));
        var nextAction = data.GetProperty("recommended_next_action");
        Assert.Equal("inspect_artifacts", nextAction.GetProperty("kind").GetString());
        Assert.Equal($"luotsi artifacts open {replayRoot}", nextAction.GetProperty("command").GetString());
        Assert.Contains(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "capsule");
        Assert.Contains(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "pack_artifacts");
        Assert.DoesNotContain(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "timeline");
        Assert.DoesNotContain(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "scenario_draft");
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "index.html")));
        Assert.Empty(processRunner.Calls);
    }

    [Fact]
    public async Task RunAsync_ReplayOpen_Last_Resolves_Latest_Root_From_Search_Root()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        var searchRoot = Path.Join("/tmp", "artifacts");
        var firstRoot = Path.Join(searchRoot, "20260526-110000-view");
        var secondRoot = Path.Join(searchRoot, "20260526-120000-run");
        fileSystem.CreateDirectory(searchRoot);
        fileSystem.AddFile(Path.Join(firstRoot, "session-timeline.jsonl"), "{\"type\":\"session_started\"}");
        fileSystem.AddFile(Path.Join(secondRoot, "session-timeline.jsonl"), "{\"type\":\"session_started\"}");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            ProcessRunner = processRunner,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "open", "--last", "--artifacts", searchRoot, "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal(secondRoot, envelope.RootElement.GetProperty("data").GetProperty("artifact_root").GetString());
        Assert.Equal(secondRoot, envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString());
        Assert.Empty(processRunner.Calls);
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
        Assert.Empty(data.GetProperty("normalizations").EnumerateArray());
        var suggestions = data.GetProperty("suggestions").EnumerateArray().ToArray();
        Assert.DoesNotContain(suggestions, suggestion =>
            suggestion.GetProperty("message").GetString()!.Contains("preceded by waitVisible", StringComparison.Ordinal));
        var sourceSummary = Assert.Single(data.GetProperty("source_summaries").EnumerateArray());
        Assert.Equal("inspect_command", sourceSummary.GetProperty("source").GetString());
        Assert.Equal(4, sourceSummary.GetProperty("step_count").GetInt32());
        Assert.Equal(0, sourceSummary.GetProperty("normalization_count").GetInt32());
        var nextActions = data.GetProperty("next_actions").EnumerateArray().ToArray();
        Assert.Equal("review_draft", nextActions[0].GetProperty("kind").GetString());
        Assert.Equal($"luotsi replay open --artifacts {replayRoot}", nextActions[0].GetProperty("command").GetString());
        Assert.Contains(nextActions, action =>
            action.GetProperty("kind").GetString() == "validate_scenario" &&
            action.GetProperty("command").GetString() == "luotsi scenario-validate --file /tmp/draft.json");
        var runHandoff = data.GetProperty("run_handoff");
        Assert.Equal("needs_validation", runHandoff.GetProperty("status").GetString());
        Assert.False(runHandoff.TryGetProperty("preflight_command", out _));
        Assert.False(runHandoff.TryGetProperty("dry_run_command", out _));
        Assert.False(runHandoff.TryGetProperty("run_command", out _));
        Assert.Contains(nextActions, action =>
            action.GetProperty("kind").GetString() == "audit_provenance" &&
            action.GetProperty("command").GetString() == $"luotsi replay graph --artifacts {replayRoot} --node-kind scenario_draft --write-markdown");
        Assert.Contains(nextActions, action =>
            action.GetProperty("kind").GetString() == "reopen_source_event" &&
            action.GetProperty("command").GetString() == "luotsi replay timeline --artifacts <artifact-root> --source-path session-timeline.jsonl --sequence 1 --context 2");
        var suggestedCommands = data.GetProperty("suggested_commands").EnumerateArray().ToArray();
        Assert.Contains(suggestedCommands, command =>
            command.GetProperty("command").GetString() == $"luotsi replay capsule --artifacts {replayRoot} --write-readme --write-json");
        Assert.Contains(suggestedCommands, command =>
            command.GetProperty("command").GetString() == $"luotsi replay graph --artifacts {replayRoot} --node-kind scenario_draft --write-markdown");
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
        Assert.DoesNotContain("luotsi screen-state", review, StringComparison.Ordinal);
        Assert.Contains("## Recommended Next Actions", review, StringComparison.Ordinal);
        Assert.Contains("review_draft", review, StringComparison.Ordinal);
        Assert.Contains("validate_scenario", review, StringComparison.Ordinal);
        Assert.Contains("## Run Handoff", review, StringComparison.Ordinal);
        Assert.Contains("Status: `needs_validation`", review, StringComparison.Ordinal);
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
    public async Task RunAsync_ReplayScenarioDraft_Validate_Writes_Status_And_Omits_Validate_NextAction()
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

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--output", "/tmp/validated-draft.json", "--validate", "--write-json", "--write-markdown"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        var validation = data.GetProperty("validation");
        Assert.Equal("validated", validation.GetProperty("status").GetString());
        Assert.Equal("luotsi scenario-validate --file /tmp/validated-draft.json", validation.GetProperty("command").GetString());
        Assert.Equal("Static scenario validation passed.", validation.GetProperty("message").GetString());
        Assert.False(validation.TryGetProperty("error", out _));
        Assert.Equal("emulator-5554", data.GetProperty("scenario").GetProperty("metadata").GetProperty("device").GetProperty("serial").GetString());
        var deviceProvenance = data.GetProperty("device_provenance");
        Assert.Equal("emulator-5554", deviceProvenance.GetProperty("serial").GetString());
        Assert.Equal("session_replay.target", deviceProvenance.GetProperty("source").GetString());
        Assert.Equal("inspect", deviceProvenance.GetProperty("session_kind").GetString());
        Assert.Equal("inspect-session", deviceProvenance.GetProperty("session_id").GetString());
        Assert.Equal("session-replay.json", deviceProvenance.GetProperty("source_path").GetString());
        Assert.DoesNotContain(data.GetProperty("next_actions").EnumerateArray(), action =>
            action.GetProperty("kind").GetString() == "validate_scenario");
        var runHandoff = data.GetProperty("run_handoff");
        Assert.Equal("ready", runHandoff.GetProperty("status").GetString());
        Assert.Equal("luotsi preflight --device emulator-5554 --package <app.id>", runHandoff.GetProperty("preflight_command").GetString());
        Assert.Equal("luotsi run --path /tmp/validated-draft.json --dry-run", runHandoff.GetProperty("dry_run_command").GetString());
        Assert.Equal("luotsi run --file /tmp/validated-draft.json --device emulator-5554", runHandoff.GetProperty("run_command").GetString());
        Assert.False(runHandoff.TryGetProperty("claimed_run_command", out _));
        Assert.Contains(data.GetProperty("next_actions").EnumerateArray(), action =>
            action.GetProperty("kind").GetString() == "dry_run_scenario" &&
            action.GetProperty("command").GetString() == "luotsi run --path /tmp/validated-draft.json --dry-run");
        Assert.Contains(data.GetProperty("next_actions").EnumerateArray(), action =>
            action.GetProperty("kind").GetString() == "preflight_device" &&
            action.GetProperty("command").GetString() == "luotsi preflight --device emulator-5554 --package <app.id>");
        Assert.DoesNotContain(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString() == "luotsi scenario-validate --file /tmp/validated-draft.json");
        Assert.Contains(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString() == "luotsi run --path /tmp/validated-draft.json --dry-run");
        Assert.Contains(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString() == "luotsi preflight --device emulator-5554 --package <app.id>");
        Assert.Contains(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString() == "luotsi run --file /tmp/validated-draft.json --device emulator-5554");

        using var summary = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft-summary.json")));
        Assert.Equal("validated", summary.RootElement.GetProperty("validation").GetProperty("status").GetString());
        Assert.Equal("luotsi scenario-validate --file /tmp/validated-draft.json", summary.RootElement.GetProperty("validation").GetProperty("command").GetString());
        Assert.Equal("emulator-5554", summary.RootElement.GetProperty("deviceProvenance").GetProperty("serial").GetString());
        Assert.Equal("ready", summary.RootElement.GetProperty("runHandoff").GetProperty("status").GetString());
        Assert.Equal("luotsi preflight --device emulator-5554 --package <app.id>", summary.RootElement.GetProperty("runHandoff").GetProperty("preflightCommand").GetString());
        Assert.Equal("luotsi run --path /tmp/validated-draft.json --dry-run", summary.RootElement.GetProperty("runHandoff").GetProperty("dryRunCommand").GetString());
        Assert.False(summary.RootElement.GetProperty("runHandoff").TryGetProperty("claimedRunCommand", out _));

        var markdown = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft.md"));
        Assert.Contains("## Validation", markdown, StringComparison.Ordinal);
        Assert.Contains("Status: `validated`", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi scenario-validate --file /tmp/validated-draft.json", markdown, StringComparison.Ordinal);
        Assert.Contains("## Device Provenance", markdown, StringComparison.Ordinal);
        Assert.Contains("Serial: `emulator-5554`", markdown, StringComparison.Ordinal);
        Assert.Contains("## Run Handoff", markdown, StringComparison.Ordinal);
        Assert.Contains("Status: `ready`", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi preflight --device emulator-5554 --package <app.id>", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi run --path /tmp/validated-draft.json --dry-run", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi run --file /tmp/validated-draft.json --device emulator-5554", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("--claim-device", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_Infers_Package_And_Device_Metadata_And_Handoff()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/package-draft-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"session_started","session_id":"inspect-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"wait_visible","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","data":{"text":"Sign in","package":"dev.luotsi.app"}}
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
          "eventCount": 3,
          "eventTypes": ["session_started", "command_result", "session_ended"]
        }
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--output", "/tmp/package-draft.json", "--validate", "--write-json", "--write-markdown"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("dev.luotsi.app", data.GetProperty("scenario").GetProperty("metadata").GetProperty("package").GetString());
        Assert.Equal("emulator-5554", data.GetProperty("scenario").GetProperty("metadata").GetProperty("device").GetProperty("serial").GetString());
        var packageProvenance = data.GetProperty("package_provenance");
        Assert.Equal("dev.luotsi.app", packageProvenance.GetProperty("package").GetString());
        Assert.Equal("data.package", packageProvenance.GetProperty("source").GetString());
        Assert.Equal("command_result", packageProvenance.GetProperty("event_type").GetString());
        Assert.Equal("wait_visible", packageProvenance.GetProperty("command").GetString());
        Assert.Equal("session-timeline.jsonl", packageProvenance.GetProperty("source_path").GetString());
        Assert.Equal(1, packageProvenance.GetProperty("sequence").GetInt32());
        Assert.Equal(DateTimeOffset.Parse("2026-05-18T10:00:01Z", System.Globalization.CultureInfo.InvariantCulture), packageProvenance.GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal("luotsi replay timeline --artifacts <artifact-root> --source-path session-timeline.jsonl --sequence 1 --context 2", packageProvenance.GetProperty("source_command").GetString());
        var deviceProvenance = data.GetProperty("device_provenance");
        Assert.Equal("emulator-5554", deviceProvenance.GetProperty("serial").GetString());
        Assert.Equal("session_replay.target", deviceProvenance.GetProperty("source").GetString());
        Assert.Equal("inspect", deviceProvenance.GetProperty("session_kind").GetString());
        Assert.Equal("inspect-session", deviceProvenance.GetProperty("session_id").GetString());
        Assert.Equal("session-replay.json", deviceProvenance.GetProperty("source_path").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-05-18T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture), deviceProvenance.GetProperty("started_at").GetDateTimeOffset());

        var runHandoff = data.GetProperty("run_handoff");
        Assert.Equal("ready", runHandoff.GetProperty("status").GetString());
        Assert.Equal("luotsi preflight --device emulator-5554 --package dev.luotsi.app", runHandoff.GetProperty("preflight_command").GetString());
        Assert.Equal("luotsi run --path /tmp/package-draft.json --dry-run", runHandoff.GetProperty("dry_run_command").GetString());
        Assert.Equal("luotsi run --file /tmp/package-draft.json --device emulator-5554 --package dev.luotsi.app", runHandoff.GetProperty("run_command").GetString());
        Assert.Equal("luotsi run --file /tmp/package-draft.json --device emulator-5554 --package dev.luotsi.app --claim-device --claim-wait-sec 60", runHandoff.GetProperty("claimed_run_command").GetString());
        Assert.Contains(data.GetProperty("next_actions").EnumerateArray(), action =>
            action.GetProperty("kind").GetString() == "preflight_device" &&
            action.GetProperty("command").GetString() == "luotsi preflight --device emulator-5554 --package dev.luotsi.app");
        Assert.Contains(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString() == "luotsi run --file /tmp/package-draft.json --device emulator-5554 --package dev.luotsi.app --claim-device --claim-wait-sec 60");
        Assert.Contains(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString() == "luotsi run --file /tmp/package-draft.json --device emulator-5554 --package dev.luotsi.app");

        using var persistedScenario = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/package-draft.json"));
        Assert.Equal("dev.luotsi.app", persistedScenario.RootElement.GetProperty("metadata").GetProperty("package").GetString());
        Assert.Equal("emulator-5554", persistedScenario.RootElement.GetProperty("metadata").GetProperty("device").GetProperty("serial").GetString());
        using var persistedSummary = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft-summary.json")));
        Assert.Equal("dev.luotsi.app", persistedSummary.RootElement.GetProperty("packageProvenance").GetProperty("package").GetString());
        Assert.Equal("data.package", persistedSummary.RootElement.GetProperty("packageProvenance").GetProperty("source").GetString());
        Assert.Equal("emulator-5554", persistedSummary.RootElement.GetProperty("deviceProvenance").GetProperty("serial").GetString());
        Assert.Equal("luotsi preflight --device emulator-5554 --package dev.luotsi.app", persistedSummary.RootElement.GetProperty("runHandoff").GetProperty("preflightCommand").GetString());
        Assert.Equal("luotsi run --file /tmp/package-draft.json --device emulator-5554 --package dev.luotsi.app --claim-device --claim-wait-sec 60", persistedSummary.RootElement.GetProperty("runHandoff").GetProperty("claimedRunCommand").GetString());
        Assert.Equal("luotsi run --file /tmp/package-draft.json --device emulator-5554 --package dev.luotsi.app", persistedSummary.RootElement.GetProperty("runHandoff").GetProperty("runCommand").GetString());

        var markdown = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft.md"));
        Assert.Contains("## Package Provenance", markdown, StringComparison.Ordinal);
        Assert.Contains("Package: `dev.luotsi.app`", markdown, StringComparison.Ordinal);
        Assert.Contains("Source: `data.package`", markdown, StringComparison.Ordinal);
        Assert.Contains("## Device Provenance", markdown, StringComparison.Ordinal);
        Assert.Contains("Serial: `emulator-5554`", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi preflight --device emulator-5554 --package dev.luotsi.app", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi run --file /tmp/package-draft.json --device emulator-5554 --package dev.luotsi.app --claim-device --claim-wait-sec 60", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi run --file /tmp/package-draft.json --device emulator-5554 --package dev.luotsi.app", markdown, StringComparison.Ordinal);

        var markdownIndex = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, ArtifactSession.ArtifactIndexFileName));
        Assert.Contains("package=dev.luotsi.app", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("device=emulator-5554", markdownIndex, StringComparison.Ordinal);
        var detail = new ArtifactEvidenceDetailReader(replayRoot, fileSystem).TryBuild("scenario-draft-summary.json");
        Assert.NotNull(detail);
        Assert.Contains("package=dev.luotsi.app", detail, StringComparison.Ordinal);
        Assert.Contains("device=emulator-5554", detail, StringComparison.Ordinal);

        var capsuleConsole = new FakeConsole();
        var capsuleApp = new App(new AppDependencies
        {
            Console = capsuleConsole,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var capsuleExitCode = await capsuleApp.RunAsync(["replay", "capsule", "--artifacts", replayRoot, "--write-json"]);
        using var capsuleEnvelope = capsuleConsole.ParseSingleOutputAsJson();

        Assert.Equal(0, capsuleExitCode);
        var capsulePackageProvenance = capsuleEnvelope.RootElement.GetProperty("data").GetProperty("scenario_draft_summary").GetProperty("package_provenance");
        Assert.Equal("dev.luotsi.app", capsulePackageProvenance.GetProperty("package").GetString());
        Assert.Equal("data.package", capsulePackageProvenance.GetProperty("source").GetString());
        var capsuleDeviceProvenance = capsuleEnvelope.RootElement.GetProperty("data").GetProperty("scenario_draft_summary").GetProperty("device_provenance");
        Assert.Equal("emulator-5554", capsuleDeviceProvenance.GetProperty("serial").GetString());
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_Does_Not_Infer_Device_From_Ambiguous_Replay_Targets()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/ambiguous-device-draft-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"session_started","session_id":"inspect-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"wait_visible","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","data":{"text":"Sign in","package":"dev.luotsi.app"}}
        {"type":"session_ended","session_id":"inspect-session","ended_at":"2026-05-18T10:00:09Z","reason":"client_exit"}
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "first", "session-replay.json"), """
        {
          "schema": "luotsi-session-replay.v1",
          "sessionKind": "inspect",
          "sessionId": "first-session",
          "startedAt": "2026-05-18T10:00:00Z",
          "target": "emulator-5554"
        }
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "second", "session-replay.json"), """
        {
          "schema": "luotsi-session-replay.v1",
          "sessionKind": "inspect",
          "sessionId": "second-session",
          "startedAt": "2026-05-18T10:00:01Z",
          "target": "emulator-5556"
        }
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--output", "/tmp/ambiguous-device-draft.json", "--validate", "--write-json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("dev.luotsi.app", data.GetProperty("package_provenance").GetProperty("package").GetString());
        Assert.False(data.TryGetProperty("device_provenance", out _));
        Assert.False(data.GetProperty("scenario").GetProperty("metadata").TryGetProperty("device", out _));
        var runHandoff = data.GetProperty("run_handoff");
        Assert.Equal("luotsi preflight --device <serial> --package dev.luotsi.app", runHandoff.GetProperty("preflight_command").GetString());
        Assert.Equal("luotsi run --file /tmp/ambiguous-device-draft.json --device <serial> --package dev.luotsi.app", runHandoff.GetProperty("run_command").GetString());
        Assert.False(runHandoff.TryGetProperty("claimed_run_command", out _));

        using var summary = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft-summary.json")));
        Assert.False(summary.RootElement.TryGetProperty("deviceProvenance", out _));
        Assert.Equal("dev.luotsi.app", summary.RootElement.GetProperty("packageProvenance").GetProperty("package").GetString());
        Assert.False(summary.RootElement.GetProperty("runHandoff").TryGetProperty("claimedRunCommand", out _));
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_Does_Not_Infer_Device_From_NonSerial_Replay_Target()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/path-target-draft-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"session_started","session_id":"run-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"command_result","session_id":"run-session","id":"1","command":"wait_visible","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","data":{"text":"Sign in"}}
        {"type":"session_ended","session_id":"run-session","ended_at":"2026-05-18T10:00:09Z","reason":"client_exit"}
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "session-replay.json"), """
        {
          "schema": "luotsi-session-replay.v1",
          "sessionKind": "run",
          "sessionId": "run-session",
          "startedAt": "2026-05-18T10:00:00Z",
          "endedAt": "2026-05-18T10:00:09Z",
          "reason": "completed",
          "exitCode": 0,
          "target": "/tmp/scenario.json",
          "timelineFileName": "session-timeline.jsonl",
          "eventCount": 3,
          "eventTypes": ["session_started", "command_result", "session_ended"]
        }
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--output", "/tmp/path-target-draft.json", "--validate", "--write-json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.False(data.TryGetProperty("device_provenance", out _));
        Assert.False(data.GetProperty("scenario").GetProperty("metadata").TryGetProperty("device", out _));
        var runHandoff = data.GetProperty("run_handoff");
        Assert.Equal("luotsi preflight --device <serial> --package <app.id>", runHandoff.GetProperty("preflight_command").GetString());
        Assert.Equal("luotsi run --file /tmp/path-target-draft.json --device <serial>", runHandoff.GetProperty("run_command").GetString());
        Assert.False(runHandoff.TryGetProperty("claimed_run_command", out _));

        using var summary = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft-summary.json")));
        Assert.False(summary.RootElement.TryGetProperty("deviceProvenance", out _));
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_Validate_Without_Output_Returns_Usage_Error()
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

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--validate"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("--validate requires --output", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_Validate_Failure_Preserves_Draft_Output()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/invalid-validated-draft-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"session_started","session_id":"inspect-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"tap_point","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","data":{"x":-1,"y":24,"label":"bad tap"}}
        {"type":"session_ended","session_id":"inspect-session","ended_at":"2026-05-18T10:00:09Z","reason":"client_exit"}
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--file", "/tmp/invalid-draft.json", "--validate", "--write-json", "--write-markdown"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(fileSystem.FileExists("/tmp/invalid-draft.json"));
        using var draft = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/invalid-draft.json"));
        Assert.Equal(-1, draft.RootElement.GetProperty("steps")[0].GetProperty("x").GetInt32());
        var data = envelope.RootElement.GetProperty("data");
        var validation = data.GetProperty("validation");
        Assert.Equal("failed", validation.GetProperty("status").GetString());
        Assert.Equal("luotsi scenario-validate --file /tmp/invalid-draft.json", validation.GetProperty("command").GetString());
        Assert.Contains("coordinates must be zero or greater", validation.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Contains(data.GetProperty("next_actions").EnumerateArray(), action =>
            action.GetProperty("kind").GetString() == "validate_scenario");
        var runHandoff = data.GetProperty("run_handoff");
        Assert.Equal("blocked", runHandoff.GetProperty("status").GetString());
        Assert.False(runHandoff.TryGetProperty("preflight_command", out _));
        Assert.False(runHandoff.TryGetProperty("dry_run_command", out _));
        Assert.DoesNotContain(data.GetProperty("next_actions").EnumerateArray(), action =>
            action.GetProperty("kind").GetString() == "dry_run_scenario");
        Assert.DoesNotContain(data.GetProperty("next_actions").EnumerateArray(), action =>
            action.GetProperty("kind").GetString() == "preflight_device");

        using var summary = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft-summary.json")));
        Assert.Equal("failed", summary.RootElement.GetProperty("validation").GetProperty("status").GetString());
        Assert.Contains("coordinates must be zero or greater", summary.RootElement.GetProperty("validation").GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Equal("blocked", summary.RootElement.GetProperty("runHandoff").GetProperty("status").GetString());

        var markdown = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft.md"));
        Assert.Contains("Status: `failed`", markdown, StringComparison.Ordinal);
        Assert.Contains("Status: `blocked`", markdown, StringComparison.Ordinal);
        Assert.Contains("coordinates must be zero or greater", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_PersistedArtifacts_RoundTrip_Through_Capsule()
    {
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedInspectReplayDraftArtifacts(fileSystem);
        var draftPath = Path.Join(replayRoot, "draft-scenario.json");
        var scenarioDraftConsole = new FakeConsole();
        var scenarioDraftApp = new App(new AppDependencies
        {
            Console = scenarioDraftConsole,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var scenarioDraftExitCode = await scenarioDraftApp.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--output", draftPath, "--write-json", "--write-markdown"]);
        using var scenarioDraftEnvelope = scenarioDraftConsole.ParseSingleOutputAsJson();

        Assert.Equal(0, scenarioDraftExitCode);
        var scenarioDraftData = scenarioDraftEnvelope.RootElement.GetProperty("data");
        Assert.True(scenarioDraftData.TryGetProperty("next_actions", out var envelopeNextActions));
        Assert.False(scenarioDraftData.TryGetProperty("nextActions", out _));
        var envelopeActions = envelopeNextActions.EnumerateArray().ToArray();
        Assert.Contains(envelopeActions, action => action.GetProperty("kind").GetString() == "review_draft");
        var envelopeAuditAction = Assert.Single(envelopeActions, static action => action.GetProperty("kind").GetString() == "audit_provenance");
        Assert.Equal($"luotsi replay graph --artifacts {replayRoot} --node-kind scenario_draft --write-markdown", envelopeAuditAction.GetProperty("command").GetString());
        Assert.DoesNotContain("--node-kind generated_step", envelopeAuditAction.GetProperty("command").GetString(), StringComparison.Ordinal);

        using var persistedSummary = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft-summary.json")));
        Assert.True(persistedSummary.RootElement.TryGetProperty("nextActions", out var persistedNextActions));
        Assert.False(persistedSummary.RootElement.TryGetProperty("next_actions", out _));
        var persistedActions = persistedNextActions.EnumerateArray().ToArray();
        Assert.Contains(persistedActions, action => action.GetProperty("kind").GetString() == "review_draft");
        var persistedAuditAction = Assert.Single(persistedActions, static action => action.GetProperty("kind").GetString() == "audit_provenance");
        Assert.Equal($"luotsi replay graph --artifacts {replayRoot} --node-kind scenario_draft --write-markdown", persistedAuditAction.GetProperty("command").GetString());
        Assert.DoesNotContain("--node-kind generated_step", persistedAuditAction.GetProperty("command").GetString(), StringComparison.Ordinal);

        var graphConsole = new FakeConsole();
        var graphApp = new App(new AppDependencies
        {
            Console = graphConsole,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var graphExitCode = await graphApp.RunAsync(["replay", "graph", "--artifacts", replayRoot]);
        using var graphEnvelope = graphConsole.ParseSingleOutputAsJson();

        Assert.Equal(0, graphExitCode);
        var graphData = graphEnvelope.RootElement.GetProperty("data");
        Assert.Equal(1, graphData.GetProperty("node_kinds").GetProperty("scenario_draft").GetInt32());
        Assert.Equal(
            scenarioDraftData.GetProperty("scenario").GetProperty("steps").GetArrayLength(),
            graphData.GetProperty("node_kinds").GetProperty("generated_step").GetInt32());
        Assert.True(graphData.GetProperty("node_kinds").GetProperty("draft_source").GetInt32() >= 1);
        Assert.Contains(graphData.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "generates_step");
        Assert.Contains(graphData.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "derived_from");
        var graphNodes = graphData.GetProperty("nodes").EnumerateArray().ToArray();
        var generatedStep = graphNodes
            .Where(static node => node.GetProperty("kind").GetString() == "generated_step")
            .Select(static node => node.GetProperty("properties"))
            .First(properties =>
                properties.TryGetProperty("source_path", out var sourcePath) &&
                sourcePath.GetString() == "session-timeline.jsonl");
        Assert.Equal("1", generatedStep.GetProperty("sequence").GetString());
        Assert.Equal(
            "luotsi replay timeline --artifacts <artifact-root> --source-path session-timeline.jsonl --sequence 1 --context 2",
            generatedStep.GetProperty("source_command").GetString());

        var capsuleConsole = new FakeConsole();
        var capsuleApp = new App(new AppDependencies
        {
            Console = capsuleConsole,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var capsuleExitCode = await capsuleApp.RunAsync(["replay", "capsule", "--artifacts", replayRoot, "--write-json", "--write-readme"]);
        using var capsuleEnvelope = capsuleConsole.ParseSingleOutputAsJson();

        Assert.Equal(0, capsuleExitCode);
        var capsuleDraftSummary = capsuleEnvelope.RootElement.GetProperty("data").GetProperty("scenario_draft_summary");
        var capsuleNextActions = capsuleDraftSummary.GetProperty("next_actions").EnumerateArray().ToArray();
        Assert.Contains(capsuleNextActions, action =>
            action.GetProperty("kind").GetString() == "review_draft" &&
            action.GetProperty("command").GetString() == $"luotsi replay open --artifacts {replayRoot}");
        var capsuleAuditAction = Assert.Single(capsuleNextActions, static action => action.GetProperty("kind").GetString() == "audit_provenance");
        Assert.Equal($"luotsi replay graph --artifacts {replayRoot} --node-kind scenario_draft --write-markdown", capsuleAuditAction.GetProperty("command").GetString());
        Assert.DoesNotContain("--node-kind generated_step", capsuleAuditAction.GetProperty("command").GetString(), StringComparison.Ordinal);

        var readme = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-capsule.md"));
        Assert.Contains("Scenario Draft Next Actions", readme, StringComparison.Ordinal);
        Assert.Contains($"luotsi replay graph --artifacts {replayRoot} --node-kind scenario_draft --write-markdown", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("--node-kind generated_step", readme, StringComparison.Ordinal);

        var markdownIndex = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, ArtifactSession.ArtifactIndexFileName));
        Assert.Contains("scenario-draft-summary.json", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("next_actions=4", markdownIndex, StringComparison.Ordinal);
        var detail = new ArtifactEvidenceDetailReader(replayRoot, fileSystem).TryBuild("scenario-draft-summary.json");
        Assert.NotNull(detail);
        Assert.Contains("next_actions=4", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_NextActions_Omit_Validation_When_Output_Is_Not_Written()
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

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var nextActions = envelope.RootElement.GetProperty("data").GetProperty("next_actions").EnumerateArray().ToArray();
        Assert.Contains(nextActions, action => action.GetProperty("kind").GetString() == "review_draft");
        Assert.Contains(nextActions, action => action.GetProperty("kind").GetString() == "reopen_source_event");
        Assert.DoesNotContain(nextActions, action => action.GetProperty("kind").GetString() == "validate_scenario");
        Assert.DoesNotContain(nextActions, action => action.GetProperty("kind").GetString() == "audit_provenance");
        Assert.DoesNotContain(envelope.RootElement.GetProperty("data").GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString()!.Contains("--node-kind scenario_draft", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_Uses_Inspect_Selector_Metadata_When_Result_Data_Has_No_Text()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/inspect-selector-replay-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"session_started","session_id":"inspect-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"tap_text","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","selector":{"text":"Files","text_match":"exact","resource_id":"com.elotouch.home:id/tvAppName","resource_id_match":"exact","class_name":"android.widget.TextView","class_name_match":"exact"},"data":{"x":814,"y":315}}
        {"type":"session_ended","session_id":"inspect-session","ended_at":"2026-05-18T10:00:09Z","reason":"client_exit"}
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--output", "/tmp/selector-draft.json", "--write-markdown"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        var steps = data.GetProperty("scenario").GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(2, steps.Length);
        Assert.Equal("waitElement", steps[0].GetProperty("action").GetString());
        Assert.Equal("tapElement", steps[1].GetProperty("action").GetString());
        Assert.False(steps[0].TryGetProperty("text", out _));
        Assert.False(steps[1].TryGetProperty("text", out _));
        var selector = steps[0].GetProperty("selector");
        Assert.Equal("Files", selector.GetProperty("text").GetString());
        Assert.Equal("exact", selector.GetProperty("text_match").GetString());
        Assert.Equal("com.elotouch.home:id/tvAppName", selector.GetProperty("resource_id").GetString());
        Assert.Equal("android.widget.TextView", selector.GetProperty("class_name").GetString());
        var origin = data.GetProperty("step_origins")[0];
        Assert.Contains("inserted waitElement before tapElement", origin.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains("resource_id:exact=com.elotouch.home:id/tvAppName", origin.GetProperty("detail").GetString(), StringComparison.Ordinal);
        var normalizations = data.GetProperty("normalizations").EnumerateArray().ToArray();
        var normalization = Assert.Single(normalizations);
        Assert.Equal("inserted_pre_tap_wait", normalization.GetProperty("kind").GetString());
        Assert.Contains("Inserted waitElement before tapElement", normalization.GetProperty("detail").GetString(), StringComparison.Ordinal);
        var reviewItems = data.GetProperty("review_items").EnumerateArray().ToArray();
        Assert.Contains(reviewItems, item =>
            item.GetProperty("category").GetString() == "selector" &&
            item.GetProperty("message").GetString()!.Contains("selector metadata", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(reviewItems, item =>
            item.GetProperty("category").GetString() == "normalization" &&
            item.GetProperty("message").GetString()!.Contains("Inserted waitElement before tapElement", StringComparison.Ordinal));
        Assert.Contains("resource_id:exact=com.elotouch.home:id/tvAppName", await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft.md")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayScenarioDraft_Uses_Selector_Default_Match_Modes_When_Metadata_Omits_Match()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/inspect-selector-default-match-replay-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"session_started","session_id":"inspect-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"tap_text","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","selector":{"text":"Files","resource_id":"com.elotouch.home:id/tvAppName","class_name":"android.widget.TextView"},"data":{"x":814,"y":315}}
        {"type":"session_ended","session_id":"inspect-session","ended_at":"2026-05-18T10:00:09Z","reason":"client_exit"}
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "scenario-draft", "--artifacts", replayRoot, "--output", "/tmp/selector-default-match-draft.json", "--write-markdown"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var originDetail = envelope.RootElement
            .GetProperty("data")
            .GetProperty("step_origins")[0]
            .GetProperty("detail")
            .GetString();
        Assert.Contains("text:contains=Files", originDetail, StringComparison.Ordinal);
        Assert.Contains("resource_id:exact=com.elotouch.home:id/tvAppName", originDetail, StringComparison.Ordinal);
        Assert.Contains("class_name:exact=android.widget.TextView", originDetail, StringComparison.Ordinal);
        var selector = envelope.RootElement
            .GetProperty("data")
            .GetProperty("scenario")
            .GetProperty("steps")[0]
            .GetProperty("selector");
        Assert.Equal("contains", selector.GetProperty("text_match").GetString());
        Assert.Equal("exact", selector.GetProperty("resource_id_match").GetString());
        Assert.Equal("exact", selector.GetProperty("class_name_match").GetString());
        Assert.Equal("inserted_pre_tap_wait", Assert.Single(envelope.RootElement
            .GetProperty("data")
            .GetProperty("normalizations")
            .EnumerateArray()).GetProperty("kind").GetString());
        var markdown = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "scenario-draft.md"));
        Assert.Contains("text:contains=Files", markdown, StringComparison.Ordinal);
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
        Assert.Equal("waitVisible", steps[0].GetProperty("action").GetString());
        Assert.Equal("Sign in", steps[0].GetProperty("text").GetString());
        Assert.Equal("tapText", steps[1].GetProperty("action").GetString());
        Assert.Equal("Sign in", steps[1].GetProperty("text").GetString());
        Assert.Equal("waitVisible", steps[2].GetProperty("action").GetString());
        Assert.Equal("Welcome", steps[2].GetProperty("text").GetString());
        Assert.Equal("waitVisible", steps[3].GetProperty("action").GetString());
        Assert.Equal("Open menu", steps[3].GetProperty("text").GetString());
        var normalizations = envelope.RootElement.GetProperty("data").GetProperty("normalizations").EnumerateArray().ToArray();
        var normalization = Assert.Single(normalizations, normalization => normalization.GetProperty("kind").GetString() == "inserted_pre_tap_wait");
        Assert.Contains("Inserted waitVisible before tapText", normalization.GetProperty("detail").GetString(), StringComparison.Ordinal);

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
        var commands = data.GetProperty("commands").EnumerateArray().ToArray();
        Assert.Contains(commands, command =>
            command.GetProperty("kind").GetString() == "open_replay_front_door" &&
            command.GetProperty("command").GetString() == $"luotsi replay open --artifacts {replayRoot}");
        Assert.Contains(commands, command => command.GetProperty("kind").GetString() == "scrub_failures");
        Assert.Contains(commands, command =>
            command.GetProperty("kind").GetString() == "graph_matching_context" &&
            command.GetProperty("command").GetString()!.Contains("--contains \"not visible\"", StringComparison.Ordinal));
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
        var nextSteps = data.GetProperty("recommended_next_steps").EnumerateArray().ToArray();
        Assert.Equal("scrub_failure", nextSteps[0].GetProperty("kind").GetString());
        Assert.Contains("replay scrub", nextSteps[0].GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.Equal("graph_failure", nextSteps[1].GetProperty("kind").GetString());
        Assert.Contains("--failed", nextSteps[1].GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.Contains(nextSteps, step => step.GetProperty("kind").GetString() == "search_failure_text");
        Assert.Contains(nextSteps, step =>
            step.GetProperty("kind").GetString() == "cluster_similar_failures" &&
            step.GetProperty("command").GetString()!.Contains("replay cluster", StringComparison.Ordinal) &&
            step.GetProperty("command").GetString()!.Contains("--min-count 2", StringComparison.Ordinal));
        Assert.Contains(nextSteps, step => step.GetProperty("kind").GetString() == "open_artifacts");
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-capsule.md")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-capsule-summary.json")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "index.md")));
        var readme = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-capsule.md"));
        Assert.Contains("## Start Here", readme, StringComparison.Ordinal);
        Assert.Contains("- Primary failure:", readme, StringComparison.Ordinal);
        Assert.Contains("waitVisible", readme, StringComparison.Ordinal);
        Assert.Contains("Best next step: Scrub the failure window (`scrub_failure`)", readme, StringComparison.Ordinal);
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
        Assert.Contains("## Recommended Next Steps", readme, StringComparison.Ordinal);
        Assert.Contains("Scrub the failure window", readme, StringComparison.Ordinal);
        Assert.Contains("Find similar failures", readme, StringComparison.Ordinal);
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
    public async Task RunAsync_ReplayCapsule_Suggests_ScenarioDraft_When_Structured_Selector_Timeline_Exists()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var inspectRoot = Path.Join(replayRoot, "inspect");
        fileSystem.CreateDirectory(inspectRoot);
        fileSystem.AddFile(Path.Join(inspectRoot, "session-timeline.jsonl"), """
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"tap_element","ok":true,"selector":{"text":"Files","text_match":"exact","resource_id":"com.elotouch.home:id/tvAppName"},"data":{"x":814,"y":315}}
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
        Assert.Contains("command_result:tap_element", data.GetProperty("scenario_draft_reason").GetString(), StringComparison.Ordinal);
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
          "nextActions": [
            {
              "kind": "review_draft",
              "title": "Review generated draft",
              "reason": "Review before editing.",
              "command": "luotsi replay open --artifacts /tmp/replay-capsule-root"
            },
            {
              "kind": "audit_provenance",
              "title": "Audit draft provenance",
              "reason": "Inspect generated steps and normalizations.",
              "command": "luotsi replay graph --artifacts /tmp/replay-capsule-root --node-kind scenario_draft --write-markdown"
            }
          ],
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
        Assert.Equal(2, draftSummary.GetProperty("next_action_count").GetInt32());
        Assert.Equal(1, draftSummary.GetProperty("normalization_count").GetInt32());
        Assert.Equal("Review selectors.", draftSummary.GetProperty("warnings")[0].GetString());
        var nextActions = draftSummary.GetProperty("next_actions").EnumerateArray().ToArray();
        Assert.Equal("review_draft", nextActions[0].GetProperty("kind").GetString());
        Assert.Equal("Review generated draft", nextActions[0].GetProperty("title").GetString());
        Assert.Contains("replay open", nextActions[0].GetProperty("command").GetString(), StringComparison.Ordinal);
        var reviewItems = draftSummary.GetProperty("review_items").EnumerateArray().ToArray();
        Assert.Equal("selector", reviewItems[0].GetProperty("category").GetString());
        Assert.Equal("Review wait selector.", reviewItems[0].GetProperty("message").GetString());
        Assert.Contains("--source-path session-timeline.jsonl", reviewItems[0].GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.Contains(data.GetProperty("suggested_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString()!.Contains("Review Checklist", StringComparison.Ordinal));
        var nextSteps = data.GetProperty("recommended_next_steps").EnumerateArray().ToArray();
        Assert.Equal("review_draft", nextSteps[0].GetProperty("kind").GetString());
        Assert.Equal("Review generated draft", nextSteps[0].GetProperty("title").GetString());
        Assert.Equal("audit_provenance", nextSteps[1].GetProperty("kind").GetString());
        Assert.Contains("scenario_draft", nextSteps[1].GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.Contains(nextSteps, step => step.GetProperty("kind").GetString() == "scrub_failure");
        var readme = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-capsule.md"));
        Assert.Contains("Scenario draft summary: `scenario-draft-summary.json`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft review: `scenario-draft.md`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft file: `draft-scenario.json`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft confidence: `medium`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft review items: `2`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft next actions: `2`", readme, StringComparison.Ordinal);
        Assert.Contains("### Scenario Draft Warning Preview", readme, StringComparison.Ordinal);
        Assert.Contains("Review selectors.", readme, StringComparison.Ordinal);
        Assert.Contains("### Scenario Draft Next Actions", readme, StringComparison.Ordinal);
        Assert.Contains("Review generated draft", readme, StringComparison.Ordinal);
        Assert.Contains("luotsi replay graph --artifacts /tmp/replay-capsule-root --node-kind scenario_draft --write-markdown", readme, StringComparison.Ordinal);
        Assert.Contains("Best next step: Review generated draft (`review_draft`)", readme, StringComparison.Ordinal);
        Assert.Contains("### Scenario Draft Review Preview", readme, StringComparison.Ordinal);
        Assert.Contains("Review wait selector.", readme, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayCapsule_Promotes_Ready_Scenario_Draft_Run_Handoff()
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
              { "action": "waitVisible" }
            ]
          },
          "packageProvenance": {
            "package": "dev.luotsi.app",
            "source": "data.package",
            "eventType": "command_result",
            "command": "wait_visible"
          },
          "deviceProvenance": {
            "serial": "emulator-5554",
            "source": "session_replay.target",
            "sessionKind": "inspect",
            "sessionId": "inspect-session"
          },
          "validation": {
            "status": "validated",
            "command": "luotsi scenario-validate --file /tmp/ready-draft.json",
            "message": "Static scenario validation passed."
          },
          "runHandoff": {
            "status": "ready",
            "reason": "Static validation passed.",
            "preflightCommand": "luotsi preflight --device emulator-5554 --package dev.luotsi.app",
            "dryRunCommand": "luotsi run --path /tmp/ready-draft.json --dry-run",
            "runCommand": "luotsi run --file /tmp/ready-draft.json --device emulator-5554 --package dev.luotsi.app",
            "claimedRunCommand": "luotsi run --file /tmp/ready-draft.json --device emulator-5554 --package dev.luotsi.app --claim-device --claim-wait-sec 60"
          },
          "nextActions": [
            {
              "kind": "review_draft",
              "title": "Review generated draft",
              "reason": "Review before editing.",
              "command": "luotsi replay open --artifacts /tmp/replay-capsule-root"
            },
            {
              "kind": "dry_run_scenario",
              "title": "Plan scenario run",
              "reason": "Confirm the generated scenario is selected as expected before starting a device run.",
              "command": "luotsi run --path /tmp/ready-draft.json --dry-run"
            },
            {
              "kind": "preflight_device",
              "title": "Preflight target device",
              "reason": "Verify adb/device/app readiness before executing the generated scenario.",
              "command": "luotsi preflight --device emulator-5554 --package dev.luotsi.app"
            },
            {
              "kind": "audit_provenance",
              "title": "Audit draft provenance",
              "reason": "Inspect generated steps and normalizations.",
              "command": "luotsi replay graph --artifacts /tmp/replay-capsule-root --node-kind scenario_draft --write-markdown"
            }
          ],
          "reviewItems": [],
          "normalizations": []
        }
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "scenario-draft.md"), "# Luotsi Scenario Draft\n");
        fileSystem.AddFile(Path.Join(replayRoot, "ready-draft.json"), "{}");
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
        var draftSummary = data.GetProperty("scenario_draft_summary");
        Assert.Equal("dev.luotsi.app", draftSummary.GetProperty("package_provenance").GetProperty("package").GetString());
        Assert.Equal("emulator-5554", draftSummary.GetProperty("device_provenance").GetProperty("serial").GetString());
        Assert.Equal("validated", draftSummary.GetProperty("validation").GetProperty("status").GetString());
        Assert.Equal("ready", draftSummary.GetProperty("run_handoff").GetProperty("status").GetString());
        Assert.Equal("luotsi run --file /tmp/ready-draft.json --device emulator-5554 --package dev.luotsi.app --claim-device --claim-wait-sec 60", draftSummary.GetProperty("run_handoff").GetProperty("claimed_run_command").GetString());

        var nextSteps = data.GetProperty("recommended_next_steps").EnumerateArray().ToArray();
        Assert.Equal("dry_run_scenario", nextSteps[0].GetProperty("kind").GetString());
        Assert.Equal("luotsi run --path /tmp/ready-draft.json --dry-run", nextSteps[0].GetProperty("command").GetString());
        Assert.Equal("preflight_device", nextSteps[1].GetProperty("kind").GetString());
        Assert.Equal("luotsi preflight --device emulator-5554 --package dev.luotsi.app", nextSteps[1].GetProperty("command").GetString());
        Assert.Equal("claimed_run_scenario", nextSteps[2].GetProperty("kind").GetString());
        Assert.Equal("luotsi run --file /tmp/ready-draft.json --device emulator-5554 --package dev.luotsi.app --claim-device --claim-wait-sec 60", nextSteps[2].GetProperty("command").GetString());
        Assert.Equal("run_scenario", nextSteps[3].GetProperty("kind").GetString());
        Assert.Equal("luotsi run --file /tmp/ready-draft.json --device emulator-5554 --package dev.luotsi.app", nextSteps[3].GetProperty("command").GetString());
        Assert.Contains(nextSteps, step => step.GetProperty("kind").GetString() == "review_draft");
        Assert.Contains(nextSteps, step => step.GetProperty("kind").GetString() == "audit_provenance");
        Assert.Single(nextSteps, step => step.GetProperty("kind").GetString() == "dry_run_scenario");
        Assert.Single(nextSteps, step => step.GetProperty("kind").GetString() == "preflight_device");
        Assert.Single(nextSteps, step => step.GetProperty("kind").GetString() == "claimed_run_scenario");

        var readme = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-capsule.md"));
        Assert.Contains("Scenario draft package: `dev.luotsi.app`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft device: `emulator-5554`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft validation: `validated`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft run handoff: `ready`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft claimed run: `luotsi run --file /tmp/ready-draft.json --device emulator-5554 --package dev.luotsi.app --claim-device --claim-wait-sec 60`", readme, StringComparison.Ordinal);
        Assert.Contains("Scenario draft run: `luotsi run --file /tmp/ready-draft.json --device emulator-5554 --package dev.luotsi.app`", readme, StringComparison.Ordinal);
        Assert.Contains("Best next step: Plan generated scenario (`dry_run_scenario`)", readme, StringComparison.Ordinal);
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
        var commands = data.GetProperty("commands").EnumerateArray().ToArray();
        Assert.Contains(commands, command =>
            command.GetProperty("kind").GetString() == "open_replay_front_door" &&
            command.GetProperty("command").GetString() == $"luotsi replay open --artifacts {replayRoot}");
        Assert.Contains(commands, command => command.GetProperty("kind").GetString() == "scrub_failures");
        Assert.Contains(commands, command => command.GetProperty("kind").GetString() == "graph_failures");
        var evt = Assert.Single(data.GetProperty("events").EnumerateArray());
        Assert.Equal("session-timeline.jsonl", evt.GetProperty("path").GetString());
        Assert.Equal(1, evt.GetProperty("sequence").GetInt32());
        Assert.Equal("scenario_step_failed", evt.GetProperty("type").GetString());
        Assert.True(evt.GetProperty("failure_relevant").GetBoolean());
        Assert.Contains("error_message=not visible", evt.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayTimeline_Exposes_Inspect_Selector_Metadata()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/inspect-selector-timeline-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"command_result","session_id":"inspect-session","id":"1","command":"tap_text","ok":true,"started_at":"2026-05-18T10:00:01Z","ended_at":"2026-05-18T10:00:02Z","selector":{"text":"Files","text_match":"exact","resource_id":"com.elotouch.home:id/tvAppName","resource_id_match":"exact","class_name":"android.widget.TextView","class_name_match":"exact","region":{"left":0,"top":0,"right":1000,"bottom":600}},"data":{"x":814,"y":315}}
        """);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost())
        });

        var exitCode = await app.RunAsync(["replay", "timeline", "--artifacts", replayRoot, "--type", "command_result", "--contains", "tvAppName"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var evt = Assert.Single(envelope.RootElement.GetProperty("data").GetProperty("events").EnumerateArray());
        Assert.Contains("resource_id:exact=com.elotouch.home:id/tvAppName", evt.GetProperty("detail").GetString(), StringComparison.Ordinal);
        var properties = evt.GetProperty("properties");
        Assert.Equal("Files", properties.GetProperty("selector.text").GetString());
        Assert.Equal("exact", properties.GetProperty("selector.text_match").GetString());
        Assert.Equal("com.elotouch.home:id/tvAppName", properties.GetProperty("selector.resource_id").GetString());
        Assert.Equal("0", properties.GetProperty("selector.region.left").GetString());
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
        Assert.Contains(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString() == $"luotsi replay capsule --artifacts {replayRoot} --write-readme --write-json");
        Assert.Contains(data.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString() == $"luotsi replay graph --artifacts {replayRoot} --failed --write-json --write-markdown");
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
        Assert.Contains(data.GetProperty("taxonomy").GetProperty("evidence_kinds").EnumerateArray(), kind => kind.GetProperty("kind").GetString() == "artifact");
        Assert.Contains(data.GetProperty("taxonomy").GetProperty("query_examples").EnumerateArray(), example => example.GetProperty("kind").GetString() == "neighborhood");
        Assert.Contains("scenario_step_failed", data.GetProperty("agent_summary").GetProperty("what_failed").GetString(), StringComparison.Ordinal);
        Assert.Contains("action-to-failure", data.GetProperty("agent_summary").GetProperty("what_changed").GetString(), StringComparison.Ordinal);
        Assert.Contains("luotsi replay", data.GetProperty("agent_summary").GetProperty("what_can_act_on").GetString(), StringComparison.Ordinal);
        Assert.Contains(data.GetProperty("agent_summary").GetProperty("evidence_node_ids").EnumerateArray(), id => id.GetString()!.StartsWith("failure:", StringComparison.Ordinal));
        Assert.Contains(data.GetProperty("agent_summary").GetProperty("commands").EnumerateArray(), command =>
            command.GetString() == $"luotsi replay open --artifacts {replayRoot} --dry-run");
        Assert.Contains(data.GetProperty("actions").EnumerateArray(), action =>
            action.GetProperty("kind").GetString() == "open_replay_front_door" &&
            action.GetProperty("command").GetString() == $"luotsi replay open --artifacts {replayRoot} --dry-run");
        Assert.Contains(data.GetProperty("actions").EnumerateArray(), action => action.GetProperty("kind").GetString() == "scrub_failures");
        Assert.Contains(data.GetProperty("actions").EnumerateArray(), action => action.GetProperty("kind").GetString() == "stream_graph");
        Assert.Contains(data.GetProperty("actions").EnumerateArray(), action => action.GetProperty("kind").GetString() == "filter_artifact_evidence");
        Assert.True(data.GetProperty("evidence_kinds").GetProperty("artifact").GetInt32() >= 1);
        Assert.True(data.GetProperty("evidence_kinds").GetProperty("failure").GetInt32() >= 1);
        Assert.Contains(data.GetProperty("evidence").EnumerateArray(), evidence =>
            evidence.GetProperty("kind").GetString() == "failure" &&
            evidence.GetProperty("edge_ids").GetArrayLength() >= 1);
        Assert.Contains(data.GetProperty("evidence").EnumerateArray(), evidence =>
            evidence.GetProperty("kind").GetString() == "artifact" &&
            evidence.GetProperty("edge_ids").GetArrayLength() >= 1);
        Assert.Contains(data.GetProperty("facts").EnumerateArray(), fact =>
            fact.GetProperty("category").GetString() == "failure" &&
            fact.GetProperty("predicate").GetString() == "has_failure_path");
        Assert.Contains(data.GetProperty("facts").EnumerateArray(), fact =>
            fact.GetProperty("category").GetString() == "transition" &&
            fact.GetProperty("predicate").GetString() == "action_to_failure");
        Assert.Contains(data.GetProperty("facts").EnumerateArray(), fact =>
            fact.GetProperty("category").GetString() == "action" &&
            fact.GetProperty("object").GetString() == "waitVisible");
        var causalChain = Assert.Single(data.GetProperty("causal_chains").EnumerateArray());
        Assert.Equal("failure:session-timeline.jsonl:1", causalChain.GetProperty("failure_node_id").GetString());
        Assert.Contains("scenario_step_failed", causalChain.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.True(causalChain.GetProperty("hops").GetArrayLength() >= 1);
        Assert.Contains(causalChain.GetProperty("hops").EnumerateArray(), hop =>
            hop.GetProperty("relation").GetString() == "transitions_to" &&
            hop.GetProperty("category").GetString() == "action_to_failure");
        Assert.Contains(data.GetProperty("hypotheses").EnumerateArray(), hypothesis =>
            hypothesis.GetProperty("kind").GetString() == "action_to_failure" &&
            hypothesis.GetProperty("evidence_node_ids").EnumerateArray().Any(id => id.GetString() == "failure:session-timeline.jsonl:1"));
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
        Assert.Contains("## Output Artifacts", markdown, StringComparison.Ordinal);
        Assert.Contains("replay-graph.jsonl", markdown, StringComparison.Ordinal);
        Assert.Contains("## Agent Summary", markdown, StringComparison.Ordinal);
        Assert.Contains("Evidence nodes", markdown, StringComparison.Ordinal);
        Assert.Contains("## What Failed", markdown, StringComparison.Ordinal);
        Assert.Contains("## What Agents Can Act On", markdown, StringComparison.Ordinal);
        Assert.Contains("## Evidence", markdown, StringComparison.Ordinal);
        Assert.Contains("Kinds:", markdown, StringComparison.Ordinal);
        Assert.Contains("| Kind | Node | Title | Detail | Artifact | Edges | Command |", markdown, StringComparison.Ordinal);
        Assert.Contains("## Facts", markdown, StringComparison.Ordinal);
        Assert.Contains("| Category | Subject | Predicate | Object | Confidence | Command |", markdown, StringComparison.Ordinal);
        Assert.Contains("## Causal Chains", markdown, StringComparison.Ordinal);
        Assert.Contains("## Hypotheses", markdown, StringComparison.Ordinal);
        Assert.Contains("| Kind | Severity | Confidence | Summary | Evidence | Command |", markdown, StringComparison.Ordinal);
        Assert.Contains("## Evidence Kinds", markdown, StringComparison.Ordinal);
        Assert.Contains("## Failure Paths", markdown, StringComparison.Ordinal);
        Assert.Contains("## Transitions", markdown, StringComparison.Ordinal);
        Assert.Contains("## Query Examples", markdown, StringComparison.Ordinal);
        var jsonl = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-graph.jsonl"));
        Assert.Contains("\"type\":\"summary\"", jsonl, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"evidence\"", jsonl, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"causal_chain\"", jsonl, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"hypothesis\"", jsonl, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"fact\"", jsonl, StringComparison.Ordinal);
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
        Assert.True(summary.RootElement.GetProperty("node_kinds").GetProperty("failure").GetInt32() >= 1);
        Assert.True(summary.RootElement.GetProperty("edge_kinds").GetProperty("has_artifact").GetInt32() >= 1);
        Assert.True(summary.RootElement.GetProperty("evidence_kinds").GetProperty("artifact").GetInt32() >= 1);
        Assert.Contains(console.OutputLines, line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.GetProperty("type").GetString() == "failure_path";
        });
        Assert.Contains(console.OutputLines, line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.GetProperty("type").GetString() == "evidence";
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
    public async Task RunAsync_ReplayGraph_Filters_Insights_By_Kind_And_Severity()
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

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot, "--insight", "transition", "--severity", "warning"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("transition", data.GetProperty("query").GetProperty("insight").GetString());
        Assert.Equal("warning", data.GetProperty("query").GetProperty("severity").GetString());
        var insight = Assert.Single(data.GetProperty("insights").EnumerateArray());
        Assert.Equal("transition", insight.GetProperty("kind").GetString());
        Assert.Equal("warning", insight.GetProperty("severity").GetString());
        Assert.True(data.GetProperty("node_count").GetInt32() > 0);
        Assert.True(data.GetProperty("edge_count").GetInt32() > 0);
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Rejects_Invalid_Insight_Severity()
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

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot, "--severity", "critical"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("--severity must be info, warning, or error", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
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
    public async Task RunAsync_ReplayGraph_Filters_By_Contains_Text()
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

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot, "--contains", "not visible", "--limit", "20"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("not visible", data.GetProperty("query").GetProperty("contains").GetString());
        Assert.Contains(data.GetProperty("nodes").EnumerateArray(), node =>
            node.GetProperty("properties").EnumerateObject().Any(property =>
                property.Value.ValueKind == JsonValueKind.String &&
                property.Value.GetString()!.Contains("not visible", StringComparison.Ordinal)));
        Assert.Contains(data.GetProperty("nodes").EnumerateArray(), node => node.GetProperty("kind").GetString() == "failure");
        Assert.Contains(data.GetProperty("edges").EnumerateArray(), edge => edge.GetProperty("kind").GetString() == "indicates");
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Filters_Evidence_By_Kind()
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

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot, "--evidence", "artifact"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("artifact", data.GetProperty("query").GetProperty("evidence").GetString());
        var evidence = data.GetProperty("evidence").EnumerateArray().ToArray();
        Assert.NotEmpty(evidence);
        Assert.All(evidence, item => Assert.Equal("artifact", item.GetProperty("kind").GetString()));
        Assert.Contains(data.GetProperty("nodes").EnumerateArray(), node => node.GetProperty("kind").GetString() == "failure");
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Filters_Facts_By_Text()
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

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot, "--fact", "action_to_failure", "--format", "jsonl"]);

        Assert.Equal(0, exitCode);
        var lines = console.OutputLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        Assert.Contains(lines, line => line.RootElement.GetProperty("type").GetString() == "summary");
        var factLines = lines
            .Where(line => line.RootElement.GetProperty("type").GetString() == "fact")
            .ToArray();
        var fact = Assert.Single(factLines);
        Assert.Equal("transition", fact.RootElement.GetProperty("fact").GetProperty("category").GetString());
        Assert.Equal("action_to_failure", fact.RootElement.GetProperty("fact").GetProperty("predicate").GetString());
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
        Assert.Equal(0, data.GetProperty("edge_count").GetInt32());
        Assert.True(data.GetProperty("matched_node_count").GetInt32() > data.GetProperty("node_count").GetInt32());
        Assert.True(data.GetProperty("matched_edge_count").GetInt32() > data.GetProperty("edge_count").GetInt32());
        Assert.True(data.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Escapes_Quoted_Artifact_Root_In_Suggested_Commands()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem, "/tmp/replay \"quoted\" root");
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
        Assert.Contains(data.GetProperty("actions").EnumerateArray(), action =>
            action.GetProperty("command").GetString()!.Contains("\"/tmp/replay \\\"quoted\\\" root\"", StringComparison.Ordinal));
        Assert.Contains(data.GetProperty("taxonomy").GetProperty("query_examples").EnumerateArray(), example =>
            example.GetProperty("command").GetString()!.Contains("\"/tmp/replay \\\"quoted\\\" root\"", StringComparison.Ordinal));
        Assert.Contains(data.GetProperty("evidence").EnumerateArray(), evidence =>
            evidence.GetProperty("command").ValueKind == JsonValueKind.String &&
            evidence.GetProperty("command").GetString()!.Contains("\"/tmp/replay \\\"quoted\\\" root\"", StringComparison.Ordinal));
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
        {"type":"screen_delta","session_id":"inspect-session","started_at":"2026-05-18T10:00:03Z","delta":{"added":[{"text":"Sign in"}]}}
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
          "eventCount": 4,
          "eventTypes": ["session_started", "command_result", "screen_delta", "session_ended"]
        }
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "scenario-draft-summary.json"), """
        {
          "schema": "luotsi-scenario-draft.v1",
          "artifactRoot": "/tmp/replay-graph-draft-root",
          "output": "/tmp/draft.json",
          "confidence": "medium",
          "scenario": {
            "name": "draft from replay",
            "steps": [
              { "name": "tap Sign in", "action": "tapText", "text": "Sign in" }
            ]
          },
          "sourceSummaries": [
            { "source": "inspect_command", "stepCount": 1, "normalizationCount": 0, "eventTypes": ["command_result"], "confidence": "medium" },
            { "source": "screen_delta", "stepCount": 0, "normalizationCount": 1, "eventTypes": ["screen_delta"], "confidence": "medium" }
          ],
          "stepOrigins": [
            { "stepIndex": 1, "source": "inspect_command", "eventType": "command_result", "command": "tap_text", "detail": "tap_text", "confidence": "medium", "sourcePath": "session-timeline.jsonl", "sequence": 1, "timestamp": "2026-05-18T10:00:01Z", "sourceCommand": "luotsi replay timeline --artifacts <artifact-root> --source-path session-timeline.jsonl --sequence 1 --context 2" }
          ],
          "normalizations": [
            { "kind": "duplicate_wait", "detail": "Dropped adjacent duplicate waitVisible for `Sign in`.", "source": "screen_delta", "eventType": "screen_delta", "confidence": "medium", "sourcePath": "session-timeline.jsonl", "sequence": 2, "timestamp": "2026-05-18T10:00:03Z", "sourceCommand": "luotsi replay timeline --artifacts <artifact-root> --source-path session-timeline.jsonl --sequence 2 --context 2" }
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
        var nodes = data.GetProperty("nodes").EnumerateArray().ToArray();
        var stepProperties = Assert.Single(nodes, node => node.GetProperty("kind").GetString() == "generated_step").GetProperty("properties");
        Assert.Equal("session-timeline.jsonl", stepProperties.GetProperty("source_path").GetString());
        Assert.Equal("1", stepProperties.GetProperty("sequence").GetString());
        Assert.Equal("luotsi replay timeline --artifacts <artifact-root> --source-path session-timeline.jsonl --sequence 1 --context 2", stepProperties.GetProperty("source_command").GetString());
        var normalizationNode = Assert.Single(nodes, node => node.GetProperty("kind").GetString() == "draft_normalization");
        var normalizationProperties = normalizationNode.GetProperty("properties");
        Assert.Equal("session-timeline.jsonl", normalizationProperties.GetProperty("source_path").GetString());
        Assert.Equal("2", normalizationProperties.GetProperty("sequence").GetString());
        Assert.Equal("luotsi replay timeline --artifacts <artifact-root> --source-path session-timeline.jsonl --sequence 2 --context 2", normalizationProperties.GetProperty("source_command").GetString());
        var screenDeltaSource = Assert.Single(nodes, node =>
            node.GetProperty("kind").GetString() == "draft_source" &&
            node.GetProperty("label").GetString() == "screen_delta");
        Assert.Equal("1", screenDeltaSource.GetProperty("properties").GetProperty("normalization_count").GetString());
        var normalizationSourceEdge = Assert.Single(data.GetProperty("edges").EnumerateArray(), edge =>
            edge.GetProperty("from").GetString() == normalizationNode.GetProperty("id").GetString() &&
            edge.GetProperty("kind").GetString() == "derived_from");
        Assert.Equal("session-timeline.jsonl", normalizationSourceEdge.GetProperty("properties").GetProperty("source_path").GetString());
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
        Assert.Equal(1, data.GetProperty("query").GetProperty("min_count").GetInt32());
        Assert.False(data.GetProperty("query").TryGetProperty("similarity", out _));
        Assert.False(data.GetProperty("query").TryGetProperty("contains", out _));
        var clusters = data.GetProperty("clusters").EnumerateArray().ToArray();
        Assert.Equal(2, clusters[0].GetProperty("count").GetInt32());
        Assert.Equal("selector_or_screen_state", clusters[0].GetProperty("category").GetString());
        Assert.Equal("waitVisible", clusters[0].GetProperty("action").GetString());
        Assert.Contains("not visible after 30 seconds", clusters[0].GetProperty("message").GetString(), StringComparison.Ordinal);
        var intelligence = clusters[0].GetProperty("intelligence");
        Assert.Equal("same_failure_shape", intelligence.GetProperty("similarity").GetString());
        Assert.True(intelligence.GetProperty("similarity_score").GetDouble() >= 0.9);
        Assert.Contains("selector", intelligence.GetProperty("likely_cause").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/tmp/replay-cluster-root\\run-b", intelligence.GetProperty("best_replay_artifact_root").GetString());
        Assert.Contains("replay graph", intelligence.GetProperty("best_graph_command").GetString(), StringComparison.Ordinal);
        Assert.Contains("replay scrub", intelligence.GetProperty("best_scrub_command").GetString(), StringComparison.Ordinal);
        Assert.Contains(intelligence.GetProperty("supporting_signals").EnumerateArray(), signal => signal.GetString() == "instances=2");
        Assert.Contains(intelligence.GetProperty("supporting_signals").EnumerateArray(), signal => signal.GetString() == "best_replay_evidence_score=10");
        var signalComparisons = intelligence.GetProperty("signal_comparisons").EnumerateArray().ToArray();
        Assert.Contains(signalComparisons, signal =>
            signal.GetProperty("name").GetString() == "action" &&
            signal.GetProperty("stability").GetString() == "stable" &&
            signal.GetProperty("values").EnumerateArray().Single().GetString() == "waitVisible");
        Assert.Contains(signalComparisons, signal =>
            signal.GetProperty("name").GetString() == "message" &&
            signal.GetProperty("stability").GetString() == "stable" &&
            signal.GetProperty("values").EnumerateArray().Count() == 2);
        var hints = clusters[0].GetProperty("hints").EnumerateArray().ToArray();
        Assert.Contains(hints, hint => hint.GetProperty("kind").GetString() == "same_failure_shape");
        Assert.Contains(hints, hint => hint.GetProperty("kind").GetString() == "likely_repeated_selector_or_screen_state_failure");
        Assert.Contains(hints, hint => hint.GetProperty("kind").GetString() == "inspect_best_failure_graph");
        Assert.Contains(hints, hint => hint.GetProperty("kind").GetString() == "scrub_best_failure");
        Assert.Contains(hints, hint =>
            hint.GetProperty("kind").GetString() == "open_best_replay" &&
            hint.GetProperty("command").GetString() == "luotsi replay open --artifacts /tmp/replay-cluster-root\\run-b");
        Assert.Contains(hints, hint =>
            hint.GetProperty("kind").GetString() == "write_best_replay_capsule" &&
            hint.GetProperty("command").GetString() == "luotsi replay capsule --artifacts /tmp/replay-cluster-root\\run-b --write-readme --write-json");
        Assert.Contains(hints, hint =>
            hint.GetProperty("kind").GetString() == "search_best_failure_text" &&
            hint.GetProperty("command").GetString()!.Contains("not visible after 30 seconds", StringComparison.Ordinal));
        Assert.Equal(Path.Join(replayRoot, "replay-clusters.json"), data.GetProperty("json_path").GetString());
        Assert.Equal(Path.Join(replayRoot, "replay-clusters.md"), data.GetProperty("markdown_path").GetString());
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-clusters.json")));
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "replay-clusters.md")));
        var markdown = await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "replay-clusters.md"));
        Assert.Contains("## Start Here", markdown, StringComparison.Ordinal);
        Assert.Contains("Top cluster:", markdown, StringComparison.Ordinal);
        Assert.Contains("Open front door: `luotsi replay open --artifacts /tmp/replay-cluster-root\\run-b`", markdown, StringComparison.Ordinal);
        Assert.Contains("Scrub failure: `luotsi replay scrub --artifacts /tmp/replay-cluster-root\\run-b --failures --context 3 --write-markdown`", markdown, StringComparison.Ordinal);
        Assert.Contains("Inspect graph: `luotsi replay graph --artifacts /tmp/replay-cluster-root\\run-b --failed --write-json --write-markdown`", markdown, StringComparison.Ordinal);
        Assert.Contains("### Intelligence", markdown, StringComparison.Ordinal);
        Assert.Contains("Likely cause", markdown, StringComparison.Ordinal);
        Assert.Contains("| Signal | Stability | Values |", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi replay graph", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayCluster_Filters_By_MinCount_Similarity_And_Text()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = "/tmp/replay-cluster-filter-root";
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

        var exitCode = await app.RunAsync(["replay", "cluster", "--artifacts", replayRoot, "--min-count", "2", "--similarity", "same_failure_shape", "--contains", "waitVisible"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("query").GetProperty("min_count").GetInt32());
        Assert.Equal("same_failure_shape", data.GetProperty("query").GetProperty("similarity").GetString());
        Assert.Equal("waitVisible", data.GetProperty("query").GetProperty("contains").GetString());
        var cluster = Assert.Single(data.GetProperty("clusters").EnumerateArray());
        Assert.Equal(2, cluster.GetProperty("count").GetInt32());
        Assert.Equal("waitVisible", cluster.GetProperty("action").GetString());
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
        Assert.Contains(summaryLine.RootElement.GetProperty("commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "open_replay_front_door");
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
        Assert.Contains("## Commands", markdownArtifact, StringComparison.Ordinal);
        Assert.Contains("luotsi replay capsule", markdownArtifact, StringComparison.Ordinal);
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
    public async Task RunAsync_ReplaySummarize_Format_Rejects_Human_Output_Flag()
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

        var exitCode = await app.RunAsync(["replay", "summarize", "--artifacts", replayRoot, "--format", "json", "--human"]);

        Assert.Equal(2, exitCode);
        Assert.Contains(console.OutputLines, static line => line.Contains("--format is a raw output mode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ReplaySummarize_Format_Rejects_Quiet_Output_Flag()
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

        var exitCode = await app.RunAsync(["replay", "summarize", "--artifacts", replayRoot, "--format", "json", "--quiet"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("--format is a raw output mode", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayTimeline_Format_Rejects_ConsoleOutput_Mode()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["replay", "timeline", "--artifacts", replayRoot, "--format", "jsonl", "--console-output", "quiet"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Contains("--format is a raw output mode", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReplayGraph_Format_Rejects_Json_Flag()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var replayRoot = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["replay", "graph", "--artifacts", replayRoot, "--format", "jsonl", "--json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Contains("--json", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
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
        Assert.Equal(4, data.GetProperty("probes").GetArrayLength());
        Assert.Equal(1, data.GetProperty("probes")[0].GetProperty("attempt_count").GetInt32());
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
    public async Task LabDoctor_Retries_Transient_Probe_Failure_And_Reports_Attempts()
    {
        var console = new FakeConsole();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        host.AdbServerStatusResults.Enqueue(CreateAdbDiagnostic("server-status", ["server-status"], exitCode: 1, stderr: "transport is not ready"));
        host.AdbServerStatusResults.Enqueue(CreateAdbDiagnostic("server-status", ["server-status"]));
        var app = new App(new AppDependencies
        {
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["lab", "doctor", "--device-query", "serial=usb-1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var firstProbe = envelope.RootElement.GetProperty("data").GetProperty("probes")[0];
        Assert.Equal("server-status", firstProbe.GetProperty("name").GetString());
        Assert.True(firstProbe.GetProperty("succeeded").GetBoolean());
        Assert.Equal(2, firstProbe.GetProperty("attempt_count").GetInt32());
        Assert.Equal(1, firstProbe.GetProperty("retry_count").GetInt32());
        Assert.Equal(["server-status", "server-status", "version", "features", "mdns check"], host.AdbDiagnostics);
    }

    [Fact]
    public async Task LabDoctor_Does_Not_Retry_NonTransient_Probe_Failure()
    {
        var console = new FakeConsole();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        host.AdbServerStatusResults.Enqueue(CreateAdbDiagnostic("server-status", ["server-status"], exitCode: 1, stderr: "unknown option --bad"));
        var app = new App(new AppDependencies
        {
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["lab", "doctor", "--device-query", "serial=usb-1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var firstProbe = envelope.RootElement.GetProperty("data").GetProperty("probes")[0];
        Assert.False(firstProbe.GetProperty("succeeded").GetBoolean());
        Assert.Equal(1, firstProbe.GetProperty("attempt_count").GetInt32());
        Assert.Equal(0, firstProbe.GetProperty("retry_count").GetInt32());
        Assert.Equal(["server-status", "version", "features", "mdns check"], host.AdbDiagnostics);
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
    public async Task LabClaim_ClaimWaitSec_Waits_For_Lease_Expiry_And_Cleans_Queue()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var delay = new FakeDelay(timeProvider);
        var store = new LabLeaseStore(fileSystem, timeProvider);
        await store.ClaimAsync("usb-1", "ci-job-1", 2);
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        var app = new App(new AppDependencies
        {
            Console = console,
            Delay = delay,
            FileSystem = fileSystem,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var claimExitCode = await app.RunAsync(["lab", "claim", "--device-query", "model=Pixel_9", "--owner", "ci-job-2", "--ttl-sec", "60", "--claim-wait-sec", "5"]);
        using var claimEnvelope = JsonDocument.Parse(console.OutputLines[0]);

        Assert.Equal(0, claimExitCode);
        var claim = claimEnvelope.RootElement.GetProperty("data");
        Assert.Equal("usb-1", claim.GetProperty("serial").GetString());
        Assert.Equal("ci-job-2", claim.GetProperty("owner").GetString());
        Assert.True(claim.TryGetProperty("last_heartbeat_at", out _));
        Assert.Equal([1000, 1000], delay.Calls);

        var queueExitCode = await app.RunAsync(["lab", "queue"]);
        using var queueEnvelope = JsonDocument.Parse(console.OutputLines[1]);

        Assert.Equal(0, queueExitCode);
        Assert.Equal(0, queueEnvelope.RootElement.GetProperty("data").GetProperty("count").GetInt32());
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
    public async Task LabInventory_Set_List_And_Clear_RoundTrip()
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

        var setExitCode = await app.RunAsync(["lab", "inventory", "set", "--device-query", "model=Pixel_9", "--pool", "smoke", "--capabilities", "camera,nfc", "--owner", "lab-admin"]);
        using var setEnvelope = JsonDocument.Parse(console.OutputLines[0]);

        Assert.Equal(0, setExitCode);
        var setData = setEnvelope.RootElement.GetProperty("data");
        Assert.Equal("usb-1", setData.GetProperty("serial").GetString());
        Assert.Equal("smoke", setData.GetProperty("pool").GetString());
        Assert.Equal("lab-admin", setData.GetProperty("owner").GetString());
        Assert.True(fileSystem.FileExists(setData.GetProperty("inventory_file").GetString()!));

        var listExitCode = await app.RunAsync(["lab", "inventory", "list"]);
        using var listEnvelope = JsonDocument.Parse(console.OutputLines[1]);

        Assert.Equal(0, listExitCode);
        var listData = listEnvelope.RootElement.GetProperty("data");
        Assert.Equal(1, listData.GetProperty("count").GetInt32());
        Assert.Equal(1, listData.GetProperty("registered_count").GetInt32());
        Assert.Equal(1, listData.GetProperty("attached_count").GetInt32());
        var listed = listData.GetProperty("devices")[0];
        Assert.True(listed.GetProperty("registered").GetBoolean());
        Assert.True(listed.GetProperty("attached").GetBoolean());
        var capabilities = listed.GetProperty("capabilities").EnumerateArray().Select(static value => value.GetString()).ToArray();
        Assert.Contains("adb", capabilities);
        Assert.Contains("camera", capabilities);
        Assert.Contains("model:Pixel_9", capabilities);
        Assert.Contains("nfc", capabilities);

        var clearExitCode = await app.RunAsync(["lab", "inventory", "clear", "--serial", "usb-1"]);
        using var clearEnvelope = JsonDocument.Parse(console.OutputLines[2]);

        Assert.Equal(0, clearExitCode);
        Assert.True(clearEnvelope.RootElement.GetProperty("data").GetProperty("cleared").GetBoolean());
        Assert.False(fileSystem.FileExists(setData.GetProperty("inventory_file").GetString()!));
    }

    [Fact]
    public async Task LabInventory_Set_Uses_Shared_Lab_State_Root_When_Configured()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            Environment = new FakeEnvironmentVariables(new Dictionary<string, string>
            {
                [LabStateStoreFactory.SharedRootEnvironmentVariable] = @"C:\lab-state"
            }),
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var setExitCode = await app.RunAsync(["lab", "inventory", "set", "--serial", "usb-1", "--pool", "smoke", "--capabilities", "camera"]);
        using var setEnvelope = JsonDocument.Parse(console.OutputLines[0]);

        Assert.Equal(0, setExitCode);
        var inventoryFile = setEnvelope.RootElement.GetProperty("data").GetProperty("inventory_file").GetString();
        Assert.Equal(Path.Join(@"C:\lab-state", "inventory", "usb-1.json"), inventoryFile);
        Assert.True(fileSystem.FileExists(inventoryFile!));
    }

    [Fact]
    public async Task LabPlan_Queued_Device_Reports_BlockedReason_And_QueueDepth()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var store = new LabLeaseStore(fileSystem, timeProvider);
        await store.EnqueueAsync("usb-1", "ci-job-1", 60);
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["lab", "plan", "--device-query", "model=Pixel_9"]);
        using var envelope = JsonDocument.Parse(console.OutputLines[0]);

        Assert.Equal(0, exitCode);
        var plan = envelope.RootElement.GetProperty("data");
        Assert.Equal("blocked", plan.GetProperty("status").GetString());
        Assert.Equal("queued", plan.GetProperty("blocked_reason").GetString());
        Assert.Equal(1, plan.GetProperty("queue_depth").GetInt32());
        Assert.Equal("luotsi lab queue", plan.GetProperty("recommended_commands")[0].GetString());
        Assert.Equal("luotsi run --path <scenarios> --claim-device --device-query model=Pixel_9 --claim-wait-sec 60", plan.GetProperty("recommended_commands")[1].GetString());
    }

    [Fact]
    public async Task LabPlan_Queued_Device_Without_Query_Uses_Device_Placeholder_In_RecommendedRunCommand()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var store = new LabLeaseStore(fileSystem, timeProvider);
        await store.EnqueueAsync("usb-1", "ci-job-1", 60);
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["lab", "plan"]);
        using var envelope = JsonDocument.Parse(console.OutputLines[0]);

        Assert.Equal(0, exitCode);
        var plan = envelope.RootElement.GetProperty("data");
        Assert.Equal("blocked", plan.GetProperty("status").GetString());
        Assert.Equal("luotsi lab queue", plan.GetProperty("recommended_commands")[0].GetString());
        Assert.Equal("luotsi run --path <scenarios> --claim-device --device <adb serial> --claim-wait-sec 60", plan.GetProperty("recommended_commands")[1].GetString());
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
    public async Task LabPlan_RequirementMismatch_Returns_Inventory_Registration_Commands()
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

        var exitCode = await app.RunAsync(["lab", "plan", "--device-query", "model=Pixel_9", "--device-pool", "smoke", "--require-capabilities", "camera,nfc"]);
        using var envelope = JsonDocument.Parse(console.OutputLines[0]);

        Assert.Equal(0, exitCode);
        var plan = envelope.RootElement.GetProperty("data");
        Assert.Equal("blocked", plan.GetProperty("status").GetString());
        Assert.Equal("requirements", plan.GetProperty("blocked_reason").GetString());
        Assert.Equal("smoke", plan.GetProperty("requirements").GetProperty("pool").GetString());
        Assert.Equal(["camera", "nfc"], plan.GetProperty("requirements").GetProperty("capabilities").EnumerateArray().Select(static value => value.GetString()!).ToArray());
        Assert.Equal("luotsi lab inventory", plan.GetProperty("recommended_commands")[0].GetString());
        Assert.Equal("luotsi lab inventory set --serial \"<adb serial>\" --pool smoke --capabilities camera,nfc", plan.GetProperty("recommended_commands")[1].GetString());
        Assert.Contains("requires pool 'smoke'", plan.GetProperty("decisions")[0].GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LabPlan_RequirementAware_Ready_Command_Preserves_Admission_Flags()
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

        var registerExitCode = await app.RunAsync(["lab", "inventory", "set", "--serial", "usb-1", "--pool", "smoke", "--capabilities", "camera,nfc"]);
        var planExitCode = await app.RunAsync(["lab", "plan", "--device-query", "model=Pixel_9", "--device-pool", "smoke", "--require-capabilities", "nfc,camera"]);
        using var planEnvelope = JsonDocument.Parse(console.OutputLines[1]);

        Assert.Equal(0, registerExitCode);
        Assert.Equal(0, planExitCode);
        var plan = planEnvelope.RootElement.GetProperty("data");
        Assert.Equal("ready", plan.GetProperty("status").GetString());
        Assert.Equal("luotsi lab claim --device-query model=Pixel_9 --device-pool smoke --require-capabilities camera,nfc", plan.GetProperty("recommended_commands")[0].GetString());
        Assert.Equal("luotsi run --path <scenarios> --claim-device --device-query model=Pixel_9 --device-pool smoke --require-capabilities camera,nfc", plan.GetProperty("recommended_commands")[1].GetString());
    }

    [Fact]
    public async Task LabPlan_Ambiguous_Recommendations_Preserve_Query_And_Admission_Flags()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        host.ConnectedDevices.Add(new DeviceInfo("usb-2", "device", "product:p model:Pixel_9_Pro device:caiman usb:1-2"));
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var registerOneExitCode = await app.RunAsync(["lab", "inventory", "set", "--serial", "usb-1", "--pool", "smoke", "--capabilities", "camera,nfc"]);
        var registerTwoExitCode = await app.RunAsync(["lab", "inventory", "set", "--serial", "usb-2", "--pool", "smoke", "--capabilities", "camera,nfc"]);
        var planExitCode = await app.RunAsync(["lab", "plan", "--device-query", "type=physical", "--device-pool", "smoke", "--require-capabilities", "nfc,camera"]);
        using var planEnvelope = JsonDocument.Parse(console.OutputLines[2]);

        Assert.Equal(0, registerOneExitCode);
        Assert.Equal(0, registerTwoExitCode);
        Assert.Equal(0, planExitCode);
        var plan = planEnvelope.RootElement.GetProperty("data");
        Assert.Equal("ambiguous", plan.GetProperty("status").GetString());
        Assert.Equal("luotsi lab status --device-query type=physical --device-pool smoke --require-capabilities camera,nfc", plan.GetProperty("recommended_commands")[0].GetString());
        Assert.Equal("luotsi lab plan --device-query type=physical,model=<model> --device-pool smoke --require-capabilities camera,nfc", plan.GetProperty("recommended_commands")[1].GetString());
    }

    [Fact]
    public async Task LabPlan_Blocked_Fallback_Status_Preserves_Query_And_Admission_Flags()
    {
        var console = new FakeConsole();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("usb-1", "device", "product:p model:Pixel_9 device:komodo usb:1-1"));
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = new FakeFileSystem(),
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["lab", "plan", "--device-query", "model=Missing", "--device-pool", "smoke", "--require-capabilities", "camera"]);
        using var envelope = JsonDocument.Parse(console.OutputLines[0]);

        Assert.Equal(0, exitCode);
        var plan = envelope.RootElement.GetProperty("data");
        Assert.Equal("blocked", plan.GetProperty("status").GetString());
        Assert.Equal("luotsi lab status --device-query model=Missing --device-pool smoke --require-capabilities camera", plan.GetProperty("recommended_commands")[0].GetString());
    }


    [Fact]
    public async Task RunAsync_Missing_Scenario_File_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            TimeProvider = timeProvider
        });
        var file = "/tmp/missing.json";

        var exitCode = await app.RunAsync(["run", "--file", file, "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("does not exist", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        var artifactRoot = envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();
        Assert.Equal(Path.Join("/tmp/test-artifacts", "20260515-120000-run"), artifactRoot);
        Assert.True(fileSystem.DirectoryExists(artifactRoot!));
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
    public async Task RunAsync_Inspect_Accepts_Rich_Element_Selectors()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        console.EnqueueInput(
            "{\"id\":\"1\",\"command\":\"wait_visible\",\"text\":\"Files\",\"text_match\":\"exact\",\"resource_id\":\"com.elotouch.home:id/tvAppName\",\"class_name\":\"android.widget.TextView\",\"region\":{\"left\":0,\"top\":0,\"right\":1000,\"bottom\":600},\"timeout_sec\":5}",
            "{\"id\":\"2\",\"command\":\"tap_text\",\"text\":\"Files\",\"text_match\":\"exact\",\"resource_id\":\"com.elotouch.home:id/tvAppName\",\"class_name\":\"android.widget.TextView\",\"timeout_sec\":5}",
            "{\"id\":\"3\",\"command\":\"exit\"}");
        var state = new ScreenState(
            timeProvider.GetUtcNow(),
            2,
            [
                new ScreenElement("Large files", null, null, "android.widget.CompoundButton", true, true, 420, 168, 594, 240),
                new ScreenElement("Files", null, "com.elotouch.home:id/tvAppName", "android.widget.TextView", true, false, 697, 296, 931, 335)
            ]);
        var host = new FakeDeviceHost(state, state, state, state, state);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["inspect", "--artifacts", "/tmp/test-artifacts"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, host.SelectorWaitRequests.Count);
        Assert.Single(host.SelectorTapRequests);
        Assert.All(host.SelectorWaitRequests, selector =>
        {
            Assert.Equal("Files", selector.Text);
            Assert.Equal(ScreenElementMatchModes.Exact, selector.TextMatch);
            Assert.Equal("com.elotouch.home:id/tvAppName", selector.ResourceId);
            Assert.Equal("android.widget.TextView", selector.ClassName);
        });
        using var waitResult = JsonDocument.Parse(console.OutputLines[2]);
        Assert.True(waitResult.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Files", waitResult.RootElement.GetProperty("data").GetProperty("text").GetString());
        var selector = waitResult.RootElement.GetProperty("selector");
        Assert.Equal("Files", selector.GetProperty("text").GetString());
        Assert.Equal("exact", selector.GetProperty("text_match").GetString());
        Assert.Equal("com.elotouch.home:id/tvAppName", selector.GetProperty("resource_id").GetString());
        Assert.False(selector.TryGetProperty("has_criteria", out _));
    }

    [Fact]
    public async Task RunAsync_Inspect_Rejects_Partial_Flat_Region_Without_Legacy_Fallback()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        console.EnqueueInput(
            "{\"id\":\"1\",\"command\":\"tap_text\",\"text\":\"Files\",\"left\":0,\"top\":0,\"timeout_sec\":5}",
            "{\"id\":\"2\",\"command\":\"exit\"}");
        var state = new ScreenState(
            timeProvider.GetUtcNow(),
            1,
            [new ScreenElement("Files", null, "com.elotouch.home:id/tvAppName", "android.widget.TextView", true, false, 697, 296, 931, 335)]);
        var host = new FakeDeviceHost(state);
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["inspect"]);

        Assert.Equal(0, exitCode);
        using var commandResult = JsonDocument.Parse(console.OutputLines[2]);
        Assert.False(commandResult.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", commandResult.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("left, top, right, and bottom", commandResult.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(host.TapTextRequests);
        Assert.Empty(host.SelectorTapRequests);
    }

    [Fact]
    public async Task RunAsync_Inspect_Rejects_Invalid_Selector_Match_Mode()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        console.EnqueueInput(
            "{\"id\":\"1\",\"command\":\"wait_visible\",\"text\":\"Files\",\"text_match\":\"regex\",\"timeout_sec\":5}",
            "{\"id\":\"2\",\"command\":\"exit\"}");
        var state = new ScreenState(
            timeProvider.GetUtcNow(),
            1,
            [new ScreenElement("Files", null, "com.elotouch.home:id/tvAppName", "android.widget.TextView", true, false, 697, 296, 931, 335)]);
        var host = new FakeDeviceHost(state);
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["inspect"]);

        Assert.Equal(0, exitCode);
        using var commandResult = JsonDocument.Parse(console.OutputLines[2]);
        Assert.False(commandResult.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", commandResult.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("text_match", commandResult.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(host.SelectorWaitRequests);
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

    [Fact]
    public async Task RunAsync_ArtifactsOpen_DryRun_Refreshes_Index_For_Artifact_Root()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), "{\"type\":\"session_started\"}");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "open", replayRoot, "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(replayRoot, data.GetProperty("artifact_root").GetString());
        Assert.Equal(Path.Join(replayRoot, "index.html"), data.GetProperty("index_path").GetString());
        Assert.True(data.GetProperty("dry_run").GetBoolean());
        Assert.True(fileSystem.FileExists(Path.Join(replayRoot, "index.html")));
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "pack_artifacts");
    }

    [Fact]
    public async Task RunAsync_ArtifactsList_Returns_Run_Ids_And_Commands()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var searchRoot = Path.Join("C:", "artifacts");
        var firstRoot = Path.Join(searchRoot, "20260526-110000-view");
        var secondRoot = Path.Join(searchRoot, "20260526-120000-run");
        fileSystem.CreateDirectory(searchRoot);
        fileSystem.AddFile(Path.Join(searchRoot, ".DS_Store"), "ignored");
        fileSystem.AddFile(Path.Join(firstRoot, "index.html"), "<!doctype html>");
        fileSystem.AddFile(Path.Join(secondRoot, "session-timeline.jsonl"), "{\"type\":\"session_started\"}");
        fileSystem.AddFile(Path.Join(secondRoot, "session-replay.json"), "{}");
        fileSystem.AddFile(Path.Join(secondRoot, "luotsi-artifact-package.json"), "{}");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "list", "--artifacts", searchRoot, "--limit", "1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("count").GetInt32());
        var entry = Assert.Single(data.GetProperty("entries").EnumerateArray());
        Assert.Equal("20260526-120000-run", entry.GetProperty("run_id").GetString());
        Assert.True(entry.GetProperty("has_package_manifest").GetBoolean());
        Assert.True(entry.GetProperty("has_timeline").GetBoolean());
        Assert.True(entry.GetProperty("has_replay_metadata").GetBoolean());
        Assert.Contains("artifacts info", entry.GetProperty("info_command").GetString(), StringComparison.Ordinal);
        Assert.Contains("artifacts open", entry.GetProperty("open_command").GetString(), StringComparison.Ordinal);
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "info_artifacts");
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "open_artifacts");
    }

    [Fact]
    public async Task RunAsync_ArtifactsList_Uses_Default_Run_Artifact_Home_When_Artifacts_Root_Is_Omitted()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var environment = OperatingSystem.IsWindows()
            ? new FakeEnvironmentVariables(new Dictionary<string, string> { ["LOCALAPPDATA"] = @"C:\Users\Test\AppData\Local" })
            : new FakeEnvironmentVariables(new Dictionary<string, string> { ["HOME"] = "/home/test" });
        var searchRoot = OperatingSystem.IsWindows()
            ? Path.Join(@"C:\Users\Test\AppData\Local", "Luotsi", "artifacts")
            : Path.Join("/home/test", ".local", "share", "luotsi", "artifacts");
        var replayRoot = Path.Join(searchRoot, "20260526-120000-run");
        fileSystem.CreateDirectory(searchRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), "{\"type\":\"session_started\"}");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            Environment = environment
        });

        var exitCode = await app.RunAsync(["artifacts", "list"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(searchRoot, data.GetProperty("search_root").GetString());
        Assert.Equal("20260526-120000-run", Assert.Single(data.GetProperty("entries").EnumerateArray()).GetProperty("run_id").GetString());
    }

    [Fact]
    public async Task RunAsync_ArtifactsInfo_Returns_Metadata_Without_Refreshing_Index()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var searchRoot = Path.Join("C:", "artifacts");
        var replayRoot = Path.Join(searchRoot, "20260526-120000-run");
        fileSystem.CreateDirectory(searchRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), "{\"type\":\"session_started\"}");
        fileSystem.AddFile(Path.Join(replayRoot, "session-replay.json"), "{}");
        fileSystem.AddFile(Path.Join(replayRoot, "luotsi-artifact-package.json"), "{}");
        fileSystem.AddFile(Path.Join(replayRoot, "screens", "failure.png"), "png");
        fileSystem.AddFile(Path.Join(replayRoot, "video.mp4"), "mp4");
        fileSystem.AddFile(Path.Join(replayRoot, "junit.xml"), "<testsuite />");
        fileSystem.AddFile(Path.Join(replayRoot, "logcat.log"), "log");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "info", "20260526-120000-run", "--artifacts", searchRoot]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("20260526-120000-run", data.GetProperty("run_id").GetString());
        Assert.Equal(replayRoot, data.GetProperty("artifact_root").GetString());
        Assert.Equal(7, data.GetProperty("file_count").GetInt32());
        Assert.False(data.GetProperty("has_html_index").GetBoolean());
        Assert.True(data.GetProperty("has_package_manifest").GetBoolean());
        Assert.False(fileSystem.FileExists(Path.Join(replayRoot, "index.html")));
        Assert.True(data.GetProperty("has_timeline").GetBoolean());
        Assert.True(data.GetProperty("has_replay_metadata").GetBoolean());
        var counts = data.GetProperty("category_counts");
        Assert.Equal(1, counts.GetProperty("screenshots").GetInt32());
        Assert.Equal(1, counts.GetProperty("videos").GetInt32());
        Assert.Equal(1, counts.GetProperty("reports").GetInt32());
        Assert.Equal(1, counts.GetProperty("logs").GetInt32());
        Assert.Equal(1, counts.GetProperty("timelines").GetInt32());
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "replay_open");
    }

    [Fact]
    public async Task RunAsync_ArtifactsInfo_Last_Resolves_Latest_Root_From_Search_Root()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var searchRoot = Path.Join("/tmp", "artifacts");
        var firstRoot = Path.Join(searchRoot, "20260526-110000-view");
        var secondRoot = Path.Join(searchRoot, "20260526-120000-run");
        fileSystem.CreateDirectory(searchRoot);
        fileSystem.AddFile(Path.Join(firstRoot, "index.html"), "<!doctype html>");
        fileSystem.AddFile(Path.Join(secondRoot, "session-timeline.jsonl"), "{\"type\":\"session_started\"}");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "info", "--last", "--artifacts", searchRoot]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal(secondRoot, envelope.RootElement.GetProperty("data").GetProperty("artifact_root").GetString());
    }

    [Fact]
    public async Task RunAsync_ArtifactsList_Rejects_NonPositive_Limit()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        fileSystem.CreateDirectory(Path.Join("/tmp", "artifacts"));
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "list", "--artifacts", Path.Join("/tmp", "artifacts"), "--limit", "0"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("--limit", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ArtifactsOpen_Resolves_Run_Id_From_Search_Root()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = Path.Join("/tmp/artifacts", "20260526-120000-view");
        fileSystem.CreateDirectory(Path.Join("/tmp", "artifacts"));
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "index.html"), "<!doctype html>");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "open", "20260526-120000-view", "--artifacts", "/tmp/artifacts", "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.EndsWith(Path.Join("artifacts", "20260526-120000-view"), envelope.RootElement.GetProperty("data").GetProperty("artifact_root").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ArtifactsOpen_Last_Resolves_Latest_Root_From_Search_Root()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var searchRoot = Path.Join("/tmp", "artifacts");
        var firstRoot = Path.Join(searchRoot, "20260526-110000-view");
        var secondRoot = Path.Join(searchRoot, "20260526-120000-run");
        fileSystem.CreateDirectory(searchRoot);
        fileSystem.AddFile(Path.Join(firstRoot, "index.html"), "<!doctype html>");
        fileSystem.AddFile(Path.Join(secondRoot, "session-timeline.jsonl"), "{\"type\":\"session_started\"}");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "open", "--last", "--artifacts", searchRoot, "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal(secondRoot, envelope.RootElement.GetProperty("data").GetProperty("artifact_root").GetString());
    }

    [Fact]
    public async Task RunAsync_ArtifactsOpen_Rejects_Target_And_Last_Together()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["artifacts", "open", "/tmp/artifacts/20260526-120000-run", "--last"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("Use either <artifact-root-or-run-id> or --last", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ArtifactsPack_Writes_Zip_With_Relative_Entries()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var output = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "index.html"), "<!doctype html>");
        fileSystem.AddFile(Path.Join(replayRoot, "failures", "failure.png"), "png");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "pack", replayRoot, "--output", output]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(replayRoot, data.GetProperty("artifact_root").GetString());
        Assert.Equal(output, data.GetProperty("output").GetString());
        Assert.Equal(3, data.GetProperty("entry_count").GetInt32());
        Assert.False(data.GetProperty("dry_run").GetBoolean());
        Assert.Equal("luotsi-artifact-package.json", data.GetProperty("manifest_path").GetString());
        var packManifest = data.GetProperty("manifest");
        Assert.Equal("luotsi-artifact-package.v1", packManifest.GetProperty("schema").GetString());
        Assert.Equal("20260526-120000-run", packManifest.GetProperty("run_id").GetString());
        Assert.Equal(2, packManifest.GetProperty("source_file_count").GetInt32());
        Assert.Equal(2, packManifest.GetProperty("files").GetArrayLength());
        Assert.False(packManifest.TryGetProperty("redaction", out _));
        Assert.Matches("^[0-9a-f]{64}$", data.GetProperty("sha256").GetString());
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "verify_artifacts" &&
            command.GetProperty("command").GetString() == $"luotsi artifacts verify {output}");
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "pack_artifacts_lab_safe" &&
            command.GetProperty("command").GetString() == $"luotsi artifacts pack {replayRoot} --output /tmp/share/replay-lab-safe.zip --redact lab-safe");
        using var archive = new ZipArchive(new MemoryStream(fileSystem.ReadBytes(output)), ZipArchiveMode.Read);
        var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "luotsi-artifact-package.json");
        using (var manifestStream = manifestEntry.Open())
        using (var manifest = JsonDocument.Parse(manifestStream))
        {
            Assert.Equal("luotsi-artifact-package.v1", manifest.RootElement.GetProperty("schema").GetString());
            Assert.Equal("20260526-120000-run", manifest.RootElement.GetProperty("run_id").GetString());
            Assert.Equal(2, manifest.RootElement.GetProperty("source_file_count").GetInt32());
            Assert.Contains(manifest.RootElement.GetProperty("recommended_commands").EnumerateArray(), command =>
                command.GetProperty("kind").GetString() == "replay_open");
        }
        Assert.Contains(archive.Entries, entry => entry.FullName == "index.html");
        Assert.Contains(archive.Entries, entry => entry.FullName == "failures/failure.png");
    }

    [Fact]
    public async Task RunAsync_ArtifactsPack_RedactOff_Writes_Exact_Copy_Without_Redaction_Metadata()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var output = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), "{\"token\":\"source-token\",\"detail\":\"visible\"}");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "pack", replayRoot, "--output", output, "--redact", "off"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.False(envelope.RootElement.GetProperty("data").GetProperty("manifest").TryGetProperty("redaction", out _));
        using var archive = new ZipArchive(new MemoryStream(fileSystem.ReadBytes(output)), ZipArchiveMode.Read);
        var timeline = ReadZipEntryText(archive, "session-timeline.jsonl");
        Assert.Contains("source-token", timeline, StringComparison.Ordinal);
        Assert.DoesNotContain("[REDACTED", timeline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ArtifactsPack_RedactLabSafe_Redacts_Text_Entries_And_Preserves_Binary()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var output = "/tmp/share/replay-redacted.zip";
        var unpackedRoot = "/tmp/unpacked-redacted";
        var screenshotBytes = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x00, 0x01, 0xff };
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "index.html"), "<!doctype html><title>safe</title>");
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """{"type":"command_result","token":"secret-session-token","detail":"visible"}""");
        fileSystem.AddFile(Path.Join(replayRoot, "logs", "logcat.txt"), "password=super-secret; authorization: Bearer abcdefghijklmnopqrstuvwxyz012345");
        await using (var screenshot = fileSystem.OpenWrite(Path.Join(replayRoot, "screens", "failure.png")))
        {
            await screenshot.WriteAsync(screenshotBytes);
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "pack", replayRoot, "--output", output, "--redact", "lab-safe"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        var redaction = data.GetProperty("manifest").GetProperty("redaction");
        Assert.Equal("lab-safe", redaction.GetProperty("mode").GetString());
        Assert.Equal(3, redaction.GetProperty("text_file_count").GetInt32());
        Assert.Equal(2, redaction.GetProperty("redacted_file_count").GetInt32());
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "verify_artifacts" &&
            command.GetProperty("command").GetString() == $"luotsi artifacts verify {output} --require-lab-safe");
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "unpack_artifacts");

        Assert.Contains("secret-session-token", await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "session-timeline.jsonl")), StringComparison.Ordinal);
        Assert.Contains("super-secret", await fileSystem.ReadAllTextAsync(Path.Join(replayRoot, "logs", "logcat.txt")), StringComparison.Ordinal);
        Assert.Equal(screenshotBytes, fileSystem.ReadBytes(Path.Join(replayRoot, "screens", "failure.png")));

        using (var archive = new ZipArchive(new MemoryStream(fileSystem.ReadBytes(output)), ZipArchiveMode.Read))
        {
            using var manifest = JsonDocument.Parse(ReadZipEntryBytes(archive, "luotsi-artifact-package.json"));
            Assert.Equal("lab-safe", manifest.RootElement.GetProperty("redaction").GetProperty("mode").GetString());

            var timeline = ReadZipEntryText(archive, "session-timeline.jsonl");
            Assert.Contains("\"token\":\"[REDACTED:token]\"", timeline, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-session-token", timeline, StringComparison.Ordinal);

            var logcat = ReadZipEntryText(archive, "logs/logcat.txt");
            Assert.Contains("password=[REDACTED:password]", logcat, StringComparison.Ordinal);
            Assert.Contains("Bearer [REDACTED:token]", logcat, StringComparison.Ordinal);
            Assert.DoesNotContain("super-secret", logcat, StringComparison.Ordinal);
            Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz012345", logcat, StringComparison.Ordinal);
            Assert.Equal(screenshotBytes, ReadZipEntryBytes(archive, "screens/failure.png"));
        }

        console.OutputLines.Clear();
        console.ErrorLines.Clear();

        Assert.Equal(0, await app.RunAsync(["artifacts", "unpack", output, "--output", unpackedRoot]));
        using var unpackEnvelope = console.ParseSingleOutputAsJson();
        var unpackData = unpackEnvelope.RootElement.GetProperty("data");
        Assert.Equal("lab-safe", unpackData.GetProperty("manifest").GetProperty("redaction").GetProperty("mode").GetString());
        Assert.Contains("[REDACTED:token]", await fileSystem.ReadAllTextAsync(Path.GetFullPath(Path.Join(unpackedRoot, "session-timeline.jsonl"))), StringComparison.Ordinal);
        Assert.Contains("[REDACTED:password]", await fileSystem.ReadAllTextAsync(Path.GetFullPath(Path.Join(unpackedRoot, "logs", "logcat.txt"))), StringComparison.Ordinal);
        Assert.Equal(screenshotBytes, fileSystem.ReadBytes(Path.GetFullPath(Path.Join(unpackedRoot, "screens", "failure.png"))));
    }

    [Fact]
    public async Task RunAsync_ArtifactsPack_Rejects_Unknown_Redaction_Mode()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "index.html"), "<!doctype html>");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "pack", replayRoot, "--redact", "full"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("--redact must be one of: off, lab-safe", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ArtifactsPack_DryRun_Does_Not_Write_Zip()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var output = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "index.html"), "<!doctype html>");
        fileSystem.AddFile(Path.Join(replayRoot, "failures", "failure.png"), "png");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "pack", replayRoot, "--output", output, "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("dry_run").GetBoolean());
        Assert.Equal(3, data.GetProperty("entry_count").GetInt32());
        Assert.Equal(output, data.GetProperty("output").GetString());
        Assert.Equal("luotsi-artifact-package.json", data.GetProperty("manifest_path").GetString());
        Assert.Equal("20260526-120000-run", data.GetProperty("manifest").GetProperty("run_id").GetString());
        Assert.False(data.TryGetProperty("sha256", out _));
        Assert.False(fileSystem.FileExists(output));
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "pack_artifacts");
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "pack_artifacts_lab_safe" &&
            command.GetProperty("command").GetString() == $"luotsi artifacts pack {replayRoot} --output /tmp/share/replay-lab-safe.zip --redact lab-safe");
    }

    [Fact]
    public async Task RunAsync_ArtifactsVerify_Validates_Package_Without_Writing_Output()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var packagePath = "/tmp/share/replay-lab-safe.zip";
        var suggestedOutput = Path.Join(Path.GetDirectoryName(Path.GetFullPath(packagePath)), Path.GetFileNameWithoutExtension(packagePath));
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "index.html"), "<!doctype html>");
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), "{\"token\":\"handoff-secret\"}");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        Assert.Equal(0, await app.RunAsync(["artifacts", "pack", replayRoot, "--output", packagePath, "--redact", "lab-safe"]));
        console.OutputLines.Clear();
        console.ErrorLines.Clear();

        var exitCode = await app.RunAsync(["artifacts", "verify", packagePath]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(packagePath, data.GetProperty("package").GetString());
        Assert.Equal("valid", data.GetProperty("status").GetString());
        Assert.Equal(3, data.GetProperty("entry_count").GetInt32());
        Assert.Equal(suggestedOutput, data.GetProperty("suggested_output_directory").GetString());
        Assert.Equal("luotsi-artifact-package.json", data.GetProperty("manifest_path").GetString());
        Assert.Equal("20260526-120000-run", data.GetProperty("manifest").GetProperty("run_id").GetString());
        Assert.Equal("lab-safe", data.GetProperty("manifest").GetProperty("redaction").GetProperty("mode").GetString());
        Assert.Equal("lab_safe", data.GetProperty("share_safety").GetString());
        Assert.False(data.GetProperty("lab_safe_required").GetBoolean());
        Assert.Empty(data.GetProperty("blockers").EnumerateArray());
        Assert.Matches("^[0-9a-f]{64}$", data.GetProperty("sha256").GetString());
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "unpack_artifacts" &&
            command.GetProperty("command").GetString() == $"luotsi artifacts unpack {packagePath} --output {suggestedOutput}");
        Assert.False(fileSystem.DirectoryExists(suggestedOutput));
        Assert.False(fileSystem.FileExists(Path.Join(suggestedOutput, "index.html")));
    }

    [Fact]
    public async Task RunAsync_ArtifactsVerify_Allows_Existing_Output_Without_Writing()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var packagePath = "/tmp/share/replay.zip";
        var suggestedOutput = Path.Join(Path.GetDirectoryName(Path.GetFullPath(packagePath)), Path.GetFileNameWithoutExtension(packagePath));
        var existingIndex = Path.Join(suggestedOutput, "index.html");
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "index.html"), "<!doctype html>");
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), "{\"type\":\"session_started\"}");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        Assert.Equal(0, await app.RunAsync(["artifacts", "pack", replayRoot, "--output", packagePath]));
        fileSystem.AddFile(existingIndex, "existing local index");
        console.OutputLines.Clear();
        console.ErrorLines.Clear();

        var exitCode = await app.RunAsync(["artifacts", "verify", packagePath]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("valid", data.GetProperty("status").GetString());
        Assert.Equal("not_redacted", data.GetProperty("share_safety").GetString());
        Assert.False(data.GetProperty("lab_safe_required").GetBoolean());
        Assert.Empty(data.GetProperty("blockers").EnumerateArray());
        Assert.Equal(suggestedOutput, data.GetProperty("suggested_output_directory").GetString());
        Assert.Equal("existing local index", await fileSystem.ReadAllTextAsync(existingIndex));
        Assert.False(fileSystem.FileExists(Path.Join(suggestedOutput, "session-timeline.jsonl")));
    }

    [Fact]
    public async Task RunAsync_ArtifactsVerify_RequireLabSafe_Blocks_Unredacted_Package()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var packagePath = "/tmp/share/replay.zip";
        var suggestedOutput = Path.Join(Path.GetDirectoryName(Path.GetFullPath(packagePath)), Path.GetFileNameWithoutExtension(packagePath));
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "index.html"), "<!doctype html>");
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), "{\"token\":\"handoff-secret\"}");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        Assert.Equal(0, await app.RunAsync(["artifacts", "pack", replayRoot, "--output", packagePath]));
        console.OutputLines.Clear();
        console.ErrorLines.Clear();

        var exitCode = await app.RunAsync(["artifacts", "verify", packagePath, "--require-lab-safe"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(1, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("blocked", data.GetProperty("status").GetString());
        Assert.Equal("not_redacted", data.GetProperty("share_safety").GetString());
        Assert.True(data.GetProperty("lab_safe_required").GetBoolean());
        Assert.Contains(data.GetProperty("blockers").EnumerateArray(), blocker =>
            blocker.GetString() == "Package was not packed with --redact lab-safe.");
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "pack_artifacts_lab_safe");
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "verify_artifacts_lab_safe" &&
            command.GetProperty("command").GetString() == $"luotsi artifacts verify {packagePath} --require-lab-safe");
        Assert.DoesNotContain(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "unpack_artifacts");
        Assert.False(fileSystem.DirectoryExists(suggestedOutput));
        Assert.False(fileSystem.FileExists(Path.Join(suggestedOutput, "index.html")));
    }

    [Fact]
    public async Task RunAsync_ArtifactsVerify_RequireLabSafe_Passes_LabSafe_Package()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var packagePath = "/tmp/share/replay-lab-safe.zip";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "index.html"), "<!doctype html>");
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), "{\"token\":\"handoff-secret\"}");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        Assert.Equal(0, await app.RunAsync(["artifacts", "pack", replayRoot, "--output", packagePath, "--redact", "lab-safe"]));
        console.OutputLines.Clear();
        console.ErrorLines.Clear();

        var exitCode = await app.RunAsync(["artifacts", "verify", packagePath, "--require-lab-safe"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("valid", data.GetProperty("status").GetString());
        Assert.Equal("lab_safe", data.GetProperty("share_safety").GetString());
        Assert.True(data.GetProperty("lab_safe_required").GetBoolean());
        Assert.Empty(data.GetProperty("blockers").EnumerateArray());
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "unpack_artifacts");
    }

    [Fact]
    public async Task RunAsync_ArtifactsVerify_Rejects_Archive_Entry_Not_Declared_In_Manifest()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var packagePath = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory("/tmp/share");
        await using (var packageStream = fileSystem.OpenWrite(packagePath))
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
            var index = archive.CreateEntry("index.html");
            await using (var entry = index.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("<!doctype html>");
            }

            var extra = archive.CreateEntry("extra.txt");
            await using (var entry = extra.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("extra");
            }

            var manifest = archive.CreateEntry("luotsi-artifact-package.json");
            await using (var entry = manifest.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("""
                {"schema":"luotsi-artifact-package.v1","run_id":"20260526-120000-run","created_at":"2026-05-26T12:00:00Z","source_file_count":1,"category_counts":{"screenshots":0,"videos":0,"reports":0,"logs":0,"timelines":0,"other":1},"recommended_commands":[{"kind":"open_artifacts","summary":"Open the unpacked artifact root locally.","command":"luotsi artifacts open <unpacked-artifact-root>"}],"files":["index.html"]}
                """);
            }
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "verify", packagePath]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("not declared in manifest", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ArtifactsPack_LabSafe_Redacts_Text_Entries_And_Preserves_Binary_Entries()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var output = "/tmp/share/redacted-replay.zip";
        var pngBytes = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x01 };
        var mp4Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 };
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "logs", "logcat.log"), "Authorization: Bearer abc.def.secret\ntoken=tok_123456\n");
        fileSystem.AddFile(Path.Join(replayRoot, "scenario-results.json"), """
        {
          "api_key": "sk_live_1234567890",
          "password": "correct horse battery staple",
          "trace": "0123456789abcdef0123456789abcdef"
        }
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), "{\"type\":\"command_result\",\"secret\":\"timeline-secret\"}");
        await using (var png = fileSystem.OpenWrite(Path.Join(replayRoot, "screens", "failure.png")))
        {
            await png.WriteAsync(pngBytes);
        }

        await using (var mp4 = fileSystem.OpenWrite(Path.Join(replayRoot, "recordings", "failure.mp4")))
        {
            await mp4.WriteAsync(mp4Bytes);
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "pack", replayRoot, "--output", output, "--redact", "lab-safe"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var manifest = envelope.RootElement.GetProperty("data").GetProperty("manifest");
        var redaction = manifest.GetProperty("redaction");
        Assert.Equal("lab-safe", redaction.GetProperty("mode").GetString());
        Assert.Equal(3, redaction.GetProperty("text_file_count").GetInt32());
        Assert.Equal(3, redaction.GetProperty("redacted_file_count").GetInt32());

        using var archive = new ZipArchive(new MemoryStream(fileSystem.ReadBytes(output)), ZipArchiveMode.Read);
        var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "luotsi-artifact-package.json");
        using (var manifestStream = manifestEntry.Open())
        using (var persistedManifest = JsonDocument.Parse(manifestStream))
        {
            var persistedRedaction = persistedManifest.RootElement.GetProperty("redaction");
            Assert.Equal("lab-safe", persistedRedaction.GetProperty("mode").GetString());
            Assert.Equal(3, persistedRedaction.GetProperty("text_file_count").GetInt32());
            Assert.Equal(3, persistedRedaction.GetProperty("redacted_file_count").GetInt32());
        }

        var logcat = ReadZipEntryText(archive, "logs/logcat.log");
        Assert.Contains("Bearer [REDACTED:token]", logcat, StringComparison.Ordinal);
        Assert.Contains("token=[REDACTED:token]", logcat, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def.secret", logcat, StringComparison.Ordinal);
        Assert.DoesNotContain("tok_123456", logcat, StringComparison.Ordinal);

        var report = ReadZipEntryText(archive, "scenario-results.json");
        Assert.Contains("\"api_key\": \"[REDACTED:apikey]\"", report, StringComparison.Ordinal);
        Assert.Contains("\"password\": \"[REDACTED:password]\"", report, StringComparison.Ordinal);
        Assert.Contains("\"trace\": \"[REDACTED:credential]\"", report, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_live_1234567890", report, StringComparison.Ordinal);
        Assert.DoesNotContain("correct horse battery staple", report, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789abcdef0123456789abcdef", report, StringComparison.Ordinal);

        var timeline = ReadZipEntryText(archive, "session-timeline.jsonl");
        Assert.Contains("\"secret\":\"[REDACTED:secret]\"", timeline, StringComparison.Ordinal);
        Assert.Equal(pngBytes, ReadZipEntryBytes(archive, "screens/failure.png"));
        Assert.Equal(mp4Bytes, ReadZipEntryBytes(archive, "recordings/failure.mp4"));
    }

    [Fact]
    public async Task RunAsync_ArtifactsPack_LabSafe_Does_Not_Modify_Source_Text()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var output = "/tmp/share/redacted-replay.zip";
        var sourcePath = Path.Join(replayRoot, "logs", "logcat.log");
        var sourceText = "Authorization: Bearer source-token\npassword=source-password\n";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(sourcePath, sourceText);
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        Assert.Equal(0, await app.RunAsync(["artifacts", "pack", replayRoot, "--output", output, "--redact", "lab-safe"]));

        Assert.Equal(sourceText, await fileSystem.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task RunAsync_ArtifactsPack_DryRun_LabSafe_Reports_Redaction_Metadata_Without_Writing_Zip()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var output = "/tmp/share/redacted-replay.zip";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), "{\"token\":\"dry-run-token\"}");
        fileSystem.AddFile(Path.Join(replayRoot, "screens", "failure.png"), "png");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "pack", replayRoot, "--output", output, "--dry-run", "--redact", "lab-safe"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("dry_run").GetBoolean());
        var redaction = data.GetProperty("manifest").GetProperty("redaction");
        Assert.Equal("lab-safe", redaction.GetProperty("mode").GetString());
        Assert.Equal(1, redaction.GetProperty("text_file_count").GetInt32());
        Assert.Equal(1, redaction.GetProperty("redacted_file_count").GetInt32());
        Assert.False(fileSystem.FileExists(output));
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString() == $"luotsi artifacts pack {replayRoot} --output {output} --redact lab-safe");
    }

    [Fact]
    public async Task RunAsync_ArtifactsPack_Rejects_Existing_Output_Without_Force()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var output = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "index.html"), "<!doctype html>");
        fileSystem.AddFile(output, "existing");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "pack", replayRoot, "--output", output]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("already exists", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ArtifactsUnpack_Extracts_Zip_To_Output_Directory()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var packagePath = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory("/tmp/share");
        await using (var packageStream = fileSystem.OpenWrite(packagePath))
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
            var index = archive.CreateEntry("index.html");
            await using (var entry = index.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("<!doctype html>");
            }

            var timeline = archive.CreateEntry("failures/session-timeline.jsonl");
            await using (var entry = timeline.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("{\"type\":\"session_started\"}");
            }

            var manifest = archive.CreateEntry("luotsi-artifact-package.json");
            await using (var entry = manifest.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("""
                {"schema":"luotsi-artifact-package.v1","run_id":"20260526-120000-run","created_at":"2026-05-26T12:00:00Z","source_file_count":2,"category_counts":{"screenshots":0,"videos":0,"reports":0,"logs":0,"timelines":1,"other":1},"recommended_commands":[{"kind":"open_artifacts","summary":"Open the unpacked artifact root locally.","command":"luotsi artifacts open <unpacked-artifact-root>"}],"files":["failures/session-timeline.jsonl","index.html"]}
                """);
            }
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "unpack", packagePath, "--output", "/tmp/unpacked"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(packagePath, data.GetProperty("package").GetString());
        var outputDirectory = data.GetProperty("output_directory").GetString();
        Assert.Equal("/tmp/unpacked", outputDirectory);
        Assert.Equal(3, data.GetProperty("entry_count").GetInt32());
        Assert.False(data.GetProperty("dry_run").GetBoolean());
        Assert.Equal(Path.Join("/tmp/unpacked", "index.html"), data.GetProperty("index_path").GetString());
        Assert.Equal("luotsi-artifact-package.json", data.GetProperty("manifest_path").GetString());
        Assert.Equal(Path.Join("/tmp/unpacked", "luotsi-artifact-package.json"), data.GetProperty("manifest_output_path").GetString());
        Assert.Equal("20260526-120000-run", data.GetProperty("manifest").GetProperty("run_id").GetString());
        Assert.Matches("^[0-9a-f]{64}$", data.GetProperty("sha256").GetString());
        Assert.True(fileSystem.FileExists(Path.GetFullPath(Path.Join(outputDirectory!, "index.html"))));
        Assert.True(fileSystem.FileExists(Path.GetFullPath(Path.Join(outputDirectory!, "failures", "session-timeline.jsonl"))));
        Assert.True(fileSystem.FileExists(Path.GetFullPath(Path.Join(outputDirectory!, "luotsi-artifact-package.json"))));
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "info_artifacts");
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "open_artifacts");
    }

    [Fact]
    public async Task RunAsync_ArtifactsUnpack_DryRun_Validates_Zip_Without_Writing()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var packagePath = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory("/tmp/share");
        await using (var packageStream = fileSystem.OpenWrite(packagePath))
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
            var index = archive.CreateEntry("index.html");
            await using (var entry = index.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("<!doctype html>");
            }

            var manifest = archive.CreateEntry("luotsi-artifact-package.json");
            await using (var entry = manifest.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("""
                {"schema":"luotsi-artifact-package.v1","run_id":"20260526-120000-run","created_at":"2026-05-26T12:00:00Z","source_file_count":1,"category_counts":{"screenshots":0,"videos":0,"reports":0,"logs":0,"timelines":0,"other":1},"recommended_commands":[{"kind":"open_artifacts","summary":"Open the unpacked artifact root locally.","command":"luotsi artifacts open <unpacked-artifact-root>"}],"files":["index.html"]}
                """);
            }
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "unpack", packagePath, "--output", "/tmp/unpacked", "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("dry_run").GetBoolean());
        Assert.Equal(2, data.GetProperty("entry_count").GetInt32());
        Assert.False(data.TryGetProperty("index_path", out _));
        Assert.Equal("luotsi-artifact-package.json", data.GetProperty("manifest_path").GetString());
        Assert.Equal(Path.Join("/tmp/unpacked", "luotsi-artifact-package.json"), data.GetProperty("manifest_output_path").GetString());
        Assert.Equal("20260526-120000-run", data.GetProperty("manifest").GetProperty("run_id").GetString());
        Assert.False(data.GetProperty("manifest").TryGetProperty("redaction", out _));
        Assert.Matches("^[0-9a-f]{64}$", data.GetProperty("sha256").GetString());
        Assert.False(fileSystem.DirectoryExists("/tmp/unpacked"));
        Assert.False(fileSystem.FileExists(Path.GetFullPath(Path.Join("/tmp/unpacked", "index.html"))));
    }

    [Fact]
    public async Task RunAsync_ArtifactsUnpack_Rejects_Archive_Entry_Not_Declared_In_Manifest()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var packagePath = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory("/tmp/share");
        await using (var packageStream = fileSystem.OpenWrite(packagePath))
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
            var index = archive.CreateEntry("index.html");
            await using (var entry = index.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("<!doctype html>");
            }

            var extra = archive.CreateEntry("extra.txt");
            await using (var entry = extra.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("extra");
            }

            var manifest = archive.CreateEntry("luotsi-artifact-package.json");
            await using (var entry = manifest.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("""
                {"schema":"luotsi-artifact-package.v1","run_id":"20260526-120000-run","created_at":"2026-05-26T12:00:00Z","source_file_count":1,"category_counts":{"screenshots":0,"videos":0,"reports":0,"logs":0,"timelines":0,"other":1},"recommended_commands":[{"kind":"open_artifacts","summary":"Open the unpacked artifact root locally.","command":"luotsi artifacts open <unpacked-artifact-root>"}],"files":["index.html"]}
                """);
            }
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "unpack", packagePath, "--output", "/tmp/unpacked"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("not declared in manifest", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ArtifactsUnpack_Rejects_Manifest_File_Missing_From_Archive()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var packagePath = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory("/tmp/share");
        await using (var packageStream = fileSystem.OpenWrite(packagePath))
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
            var index = archive.CreateEntry("index.html");
            await using (var entry = index.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("<!doctype html>");
            }

            var manifest = archive.CreateEntry("luotsi-artifact-package.json");
            await using (var entry = manifest.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("""
                {"schema":"luotsi-artifact-package.v1","run_id":"20260526-120000-run","created_at":"2026-05-26T12:00:00Z","source_file_count":2,"category_counts":{"screenshots":0,"videos":0,"reports":0,"logs":0,"timelines":0,"other":2},"recommended_commands":[{"kind":"open_artifacts","summary":"Open the unpacked artifact root locally.","command":"luotsi artifacts open <unpacked-artifact-root>"}],"files":["index.html","missing.txt"]}
                """);
            }
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "unpack", packagePath, "--output", "/tmp/unpacked"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("missing from the package", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("missing.txt", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ArtifactsUnpack_Rejects_Missing_Manifest()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var packagePath = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory("/tmp/share");
        await using (var packageStream = fileSystem.OpenWrite(packagePath))
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
            _ = archive.CreateEntry("index.html");
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "unpack", packagePath, "--output", "/tmp/unpacked"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("missing required manifest", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ArtifactsUnpack_Rejects_Invalid_Manifest_Json()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var packagePath = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory("/tmp/share");
        await using (var packageStream = fileSystem.OpenWrite(packagePath))
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
            _ = archive.CreateEntry("index.html");
            var manifest = archive.CreateEntry("luotsi-artifact-package.json");
            await using var entry = manifest.Open();
            await using var writer = new StreamWriter(entry);
            await writer.WriteAsync("{not-json");
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "unpack", packagePath, "--output", "/tmp/unpacked"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("not valid JSON", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"run_id":"20260526-120000-run","created_at":"2026-05-26T12:00:00Z","source_file_count":1,"category_counts":{"other":1},"recommended_commands":[],"files":["index.html"]}""", "missing string property 'schema'")]
    [InlineData("""{"schema":"luotsi-artifact-package.v1","created_at":"2026-05-26T12:00:00Z","source_file_count":1,"category_counts":{"other":1},"recommended_commands":[],"files":["index.html"]}""", "missing string property 'run_id'")]
    [InlineData("""{"schema":"luotsi-artifact-package.v1","run_id":"20260526-120000-run","created_at":"2026-05-26T12:00:00Z","category_counts":{"other":1},"recommended_commands":[],"files":["index.html"]}""", "missing integer property 'source_file_count'")]
    public async Task RunAsync_ArtifactsUnpack_Rejects_Manifest_Missing_Required_Field(string manifestJson, string expectedMessage)
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var packagePath = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory("/tmp/share");
        await using (var packageStream = fileSystem.OpenWrite(packagePath))
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
            _ = archive.CreateEntry("index.html");
            var manifest = archive.CreateEntry("luotsi-artifact-package.json");
            await using var entry = manifest.Open();
            await using var writer = new StreamWriter(entry);
            await writer.WriteAsync(manifestJson);
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "unpack", packagePath, "--output", "/tmp/unpacked"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains(expectedMessage, envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"schema":"luotsi-artifact-package.v1","run_id":"20260526-120000-run","created_at":"2026-05-26T12:00:00Z","source_file_count":1,"category_counts":{"other":1},"recommended_commands":[],"files":["../escape.txt"]}""", "invalid files[0] entry")]
    [InlineData("""{"schema":"luotsi-artifact-package.v1","run_id":"20260526-120000-run","created_at":"2026-05-26T12:00:00Z","source_file_count":1,"category_counts":{"other":1},"recommended_commands":[],"files":["luotsi-artifact-package.json"]}""", "invalid files[0] entry")]
    [InlineData("""{"schema":"luotsi-artifact-package.v1","run_id":"20260526-120000-run","created_at":"2026-05-26T12:00:00Z","source_file_count":2,"category_counts":{"other":2},"recommended_commands":[],"files":["index.html","index.html"]}""", "duplicate files[1] entry")]
    public async Task RunAsync_ArtifactsUnpack_Rejects_Manifest_With_Invalid_File_Entries(string manifestJson, string expectedMessage)
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var packagePath = "/tmp/share/replay.zip";
        fileSystem.CreateDirectory("/tmp/share");
        await using (var packageStream = fileSystem.OpenWrite(packagePath))
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
            _ = archive.CreateEntry("index.html");
            var manifest = archive.CreateEntry("luotsi-artifact-package.json");
            await using var entry = manifest.Open();
            await using var writer = new StreamWriter(entry);
            await writer.WriteAsync(manifestJson);
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "unpack", packagePath, "--output", "/tmp/unpacked"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains(expectedMessage, envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }
    [Fact]
    public async Task RunAsync_ArtifactsPack_Then_Unpack_RoundTrips_Manifest_And_Commands()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var replayRoot = "/tmp/luotsi/20260526-120000-run";
        var packagePath = "/tmp/share/replay.zip";
        var unpackedRoot = "/tmp/unpacked";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "index.html"), "<!doctype html>");
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), "{\"type\":\"session_started\"}");
        fileSystem.AddFile(Path.Join(replayRoot, "screens", "failure.png"), "png");
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        Assert.Equal(0, await app.RunAsync(["artifacts", "pack", replayRoot, "--output", packagePath]));
        console.OutputLines.Clear();
        console.ErrorLines.Clear();

        Assert.Equal(0, await app.RunAsync(["artifacts", "unpack", packagePath, "--output", unpackedRoot]));
        using var unpackEnvelope = console.ParseSingleOutputAsJson();
        var unpackData = unpackEnvelope.RootElement.GetProperty("data");

        Assert.True(fileSystem.FileExists(Path.GetFullPath(Path.Join(unpackedRoot, "index.html"))));
        Assert.Equal(Path.Join(unpackedRoot, "index.html"), unpackData.GetProperty("index_path").GetString());
        Assert.Equal("20260526-120000-run", unpackData.GetProperty("manifest").GetProperty("run_id").GetString());
        Assert.Contains(unpackData.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString() == $"luotsi artifacts open {unpackedRoot}");
        Assert.Contains(unpackData.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString() == $"luotsi replay open --artifacts {unpackedRoot}");

        console.OutputLines.Clear();
        console.ErrorLines.Clear();

        Assert.Equal(0, await app.RunAsync(["artifacts", "info", Path.GetFullPath(unpackedRoot)]));
        using var infoEnvelope = console.ParseSingleOutputAsJson();
        Assert.True(infoEnvelope.RootElement.GetProperty("data").GetProperty("has_package_manifest").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_ArtifactsUnpack_Rejects_ZipSlip_Entry()
    {
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var packagePath = "/tmp/share/bad.zip";
        fileSystem.CreateDirectory("/tmp/share");
        await using (var packageStream = fileSystem.OpenWrite(packagePath))
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true);
            var manifest = archive.CreateEntry("luotsi-artifact-package.json");
            await using (var entry = manifest.Open())
            await using (var writer = new StreamWriter(entry))
            {
                await writer.WriteAsync("""
                {"schema":"luotsi-artifact-package.v1","run_id":"20260526-120000-run","created_at":"2026-05-26T12:00:00Z","source_file_count":1,"category_counts":{"screenshots":0,"videos":0,"reports":0,"logs":0,"timelines":0,"other":1},"recommended_commands":[{"kind":"open_artifacts","summary":"Open the unpacked artifact root locally.","command":"luotsi artifacts open <unpacked-artifact-root>"}],"files":["index.html"]}
                """);
            }
            _ = archive.CreateEntry("../escape.txt");
        }

        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem
        });

        var exitCode = await app.RunAsync(["artifacts", "unpack", packagePath, "--output", "/tmp/unpacked"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("outside the output directory", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
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
          "helper_apk_path": "{{JsonEncodedText.Encode(helperApk)}}",
          "view_extras": "installed",
          "ffmpeg_staged": true,
          "ffmpeg_path": "{{JsonEncodedText.Encode(Path.Join(currentRoot, "ffmpeg", "bin"))}}",
          "ffmpeg_detail": "Extracted native libraries."
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

    private static string SeedReplaySummaryArtifactsWithoutFailure(FakeFileSystem fileSystem)
    {
        var replayRoot = "/tmp/replay-ok-root";
        fileSystem.CreateDirectory(replayRoot);
        fileSystem.AddFile(Path.Join(replayRoot, "session-timeline.jsonl"), """
        {"type":"view_started","session_id":"view-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"view_stats","session_id":"view-session","observed_at":"2026-05-18T10:00:05Z","stats":{"decoded_frames":120,"presented_frames":120,"dropped_frames":0}}
        {"type":"view_ended","session_id":"view-session","ended_at":"2026-05-18T10:00:06Z","reason":"closed"}
        """);
        fileSystem.AddFile(Path.Join(replayRoot, "session-replay.json"), """
        {
          "schema": "luotsi-session-replay.v1",
          "sessionKind": "view",
          "sessionId": "view-session",
          "startedAt": "2026-05-18T10:00:00Z",
          "endedAt": "2026-05-18T10:00:06Z",
          "reason": "closed",
          "exitCode": 0,
          "target": "192.168.0.134:5555",
          "timelineFileName": "session-timeline.jsonl",
          "eventCount": 3,
          "eventTypes": ["view_started", "view_stats", "view_ended"]
        }
        """);
        return replayRoot;
    }

    private static string SeedReplayCapsuleArtifacts(FakeFileSystem fileSystem, string replayRoot = "/tmp/replay-capsule-root")
    {
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

    private static string ReadZipEntryText(ZipArchive archive, string entryName)
    {
        var entry = Assert.Single(archive.Entries, candidate => candidate.FullName == entryName);
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] ReadZipEntryBytes(ZipArchive archive, string entryName)
    {
        var entry = Assert.Single(archive.Entries, candidate => candidate.FullName == entryName);
        using var stream = entry.Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static AdbDiagnosticResult CreateAdbDiagnostic(
        string name,
        IReadOnlyList<string> args,
        int exitCode = 0,
        string stdout = "",
        string stderr = "")
    {
        return new AdbDiagnosticResult(
            ResultSchemas.AdbDiagnostic,
            name,
            new AdbCommandOutput(
                $"adb {string.Join(" ", args)}",
                args,
                exitCode,
                exitCode == 0,
                stdout,
                stderr,
                1,
                null,
                []));
    }

}
