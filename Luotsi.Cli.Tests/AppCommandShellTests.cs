using Luotsi.Cli.Cli;
using Luotsi.Cli.Cli.Composition;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Provenance;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class AppCommandShellTests
{
    [Fact]
    public void WriteSuccess_Writes_Command_Envelope_With_Snake_Case_Fields()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());

        writer.WriteSuccess("devices", DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind), new DeviceListResult([]), new ArtifactData("/tmp/artifacts", "final"));

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(ResultSchemas.CommandEnvelope, envelope.RootElement.GetProperty("schema").GetString());
        Assert.Equal("/tmp/artifacts", envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString());
        Assert.Equal("luotsi", envelope.RootElement.GetProperty("provenance").GetProperty("tool").GetString());
        Assert.True(envelope.RootElement.GetProperty("provenance").TryGetProperty("framework", out _));
        Assert.True(envelope.RootElement.TryGetProperty("started_at", out _));
        Assert.True(envelope.RootElement.TryGetProperty("ended_at", out _));
    }

    [Fact]
    public void BuildProvenanceProvider_Uses_Ci_Environment()
    {
        var provider = new BuildProvenanceProvider(new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["GITHUB_ACTIONS"] = "true",
            ["GITHUB_SHA"] = "abc123",
            ["GITHUB_REF_NAME"] = "main",
            ["GITHUB_REPOSITORY"] = "digablesolutions/luotsi",
            ["GITHUB_RUN_ID"] = "456"
        }));

        var provenance = provider.Create();

        Assert.Equal("luotsi", provenance.Tool);
        Assert.Equal("abc123", provenance.CommitSha);
        Assert.Equal("main", provenance.Branch);
        Assert.Equal("digablesolutions/luotsi", provenance.Repository);
        Assert.Equal("github-actions", provenance.CiProvider);
        Assert.Equal("456", provenance.CiRunId);
    }

    [Fact]
    public async Task WriteFailureAsync_Captures_Runner_Artifacts_When_Exception_Has_No_Payload()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var responder = new AppCommandFailureResponder(new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance()));
        var runner = new FakeDeviceHost();
        var context = new AppExecutionContext(timeProvider.GetUtcNow(), CliOptions.Parse(["wait-visible", "--artifacts", "/tmp/artifacts"]))
        {
            Runner = runner
        };

        var exitCode = await responder.WriteFailureAsync("wait-visible", timeProvider.GetUtcNow(), context, new InvalidOperationException("Timed out waiting for target"));

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(1, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("selector_or_screen_state", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(ResultSchemas.FailureBundle, envelope.RootElement.GetProperty("data").GetProperty("schema").GetString());
        Assert.Equal("command", envelope.RootElement.GetProperty("data").GetProperty("scope").GetString());
        Assert.Equal("wait-visible", envelope.RootElement.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("Timed out waiting for target", envelope.RootElement.GetProperty("data").GetProperty("error_message").GetString());
    }

    [Fact]
    public void Resolve_Returns_Failure_For_Batch_Result_With_Failures()
    {
        var result = new ScenarioRunBatchResult("/tmp/scenarios", "failed", 1, 1, 1, 0, 1, 0, null, null, []);
        var resolver = new AppCommandExitCodeResolver();

        var exitCode = AppCommandExitCodeResolver.Resolve(result);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Resolve_Uses_Enforced_Ci_Policy_Exit_Code_When_Present()
    {
        var result = new ScenarioRunBatchResult(
            "/tmp/scenarios",
            "failed",
            1,
            1,
            1,
            0,
            1,
            0,
            null,
            null,
            [],
            CiPolicy: new ScenarioCiPolicyResult(
                "enforced",
                "device_quarantined",
                20,
                true,
                1,
                0,
                2,
                false,
                true,
                true,
                "Device is quarantined."));

        var exitCode = AppCommandExitCodeResolver.Resolve(result);

        Assert.Equal(20, exitCode);
    }

    [Fact]
    public void WriteUsageError_Returns_Usage_Exit_Code()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var responder = new AppCommandFailureResponder(new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance()));

        var exitCode = responder.WriteUsageError(CliOptions.Parse(["tap"]), timeProvider.GetUtcNow(), new ArtifactData("/tmp/artifacts", "final"), new UsageException("bad args"));

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task WriteFailureAsync_Uses_Enforced_Ci_Policy_Exit_Code_When_Failure_Data_Provides_It()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var responder = new AppCommandFailureResponder(new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance()));
        var context = new AppExecutionContext(timeProvider.GetUtcNow(), CliOptions.Parse(["run", "--artifacts", "/tmp/artifacts"]));
        var failure = new ScenarioStepFailureException(
            "Device is quarantined.",
            "configuration_error",
            new ScenarioRunFailureData(
                "login smoke",
                "/tmp/scenarios/login.json",
                "failed",
                new ScenarioRunTiming(1000, 100, 850, 50),
                ScenarioMetrics.Empty,
                new ScenarioFailedStepResult(1, "wait login button", "waitVisible", 500, new ScenarioStepTiming(500, 100, null, 400)),
                [],
                new FailureArtifactBundle(ResultSchemas.FailureBundle, timeProvider.GetUtcNow(), "scenario", "login smoke", "/tmp/scenarios/login.json", 1, "wait login button", "waitVisible", "System.InvalidOperationException", "device offline", [], []),
                CiPolicy: new ScenarioCiPolicyResult(
                    "enforced",
                    "device_quarantined",
                    20,
                    true,
                    1,
                    0,
                    2,
                    false,
                    true,
                    true,
                    "Device is quarantined.")),
            new InvalidOperationException("Device is quarantined."));

        var exitCode = await responder.WriteFailureAsync("run", timeProvider.GetUtcNow(), context, failure);

        Assert.Equal(20, exitCode);
    }

    [Fact]
    public void WriteSuccess_HumanOutput_Writes_Concise_Text()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());

        writer.WriteSuccess(
            "devices",
            DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            new DeviceListResult([new DeviceInfo("emulator-5554", "device", "model:Pixel_9")]),
            new ArtifactData("/tmp/artifacts", "final"),
            AppCommandConsoleOutputMode.Human);

        Assert.Equal(
            [
                "OK  devices completed in 1000 ms.",
                "  devices: 1",
                "    - serial=emulator-5554; status=device",
                "  artifacts: /tmp/artifacts"
            ],
            console.OutputLines);
        Assert.Empty(console.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_Human_Output_Flag_Writes_Text_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["version", "--human"]);

        Assert.Equal(0, exitCode);
        Assert.Contains(console.OutputLines, static line => line.StartsWith("OK  version completed", StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, static line => line.Contains("runtime_version:", StringComparison.Ordinal));
        Assert.DoesNotContain(console.OutputLines, static line => line.StartsWith("{", StringComparison.Ordinal));
        Assert.Empty(console.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_ConsoleOutput_Human_Writes_Text_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["version", "--console-output", "human"]);

        Assert.Equal(0, exitCode);
        Assert.Contains(console.OutputLines, static line => line.StartsWith("OK  version completed", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteSuccess_HumanOutput_Shows_FollowUp_Command_Hints()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());

        writer.WriteSuccess(
            "run",
            DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            new
            {
                status = "failed",
                artifact_commands = new[]
                {
                    new { kind = "open_artifacts", summary = "Open the artifact browser for this run.", command = "luotsi artifacts open /tmp/latest-run" },
                    new { kind = "replay_open", summary = "Open the replay workbench for this run.", command = "luotsi replay open --artifacts /tmp/latest-run" }
                },
                recommended_commands = new[]
                {
                    new { kind = "pack_artifacts", summary = "Pack this run for handoff.", command = "luotsi artifacts pack /tmp/latest-run" }
                }
            },
            new ArtifactData("/tmp/latest-run", "final"),
            AppCommandConsoleOutputMode.Human);

        Assert.Contains("  next: luotsi artifacts open /tmp/latest-run", console.OutputLines);
        Assert.Contains("  artifact_commands: 2", console.OutputLines);
        Assert.Contains("    - kind=open_artifacts; summary=Open the artifact browser for this run.; command=luotsi artifacts open /tmp/latest-run", console.OutputLines);
        Assert.Contains("  recommended_commands: 1", console.OutputLines);
    }

    [Fact]
    public void WriteSuccess_HumanOutput_Uses_NextActions_For_FollowUp_Command()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());

        writer.WriteSuccess(
            "replay",
            DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            new
            {
                schema = ResultSchemas.ScenarioDraft,
                next_actions = new[]
                {
                    new { kind = "review_draft", title = "Review generated draft", reason = "Review before editing.", command = "luotsi replay open --artifacts /tmp/replay-root" }
                }
            },
            new ArtifactData("/tmp/replay-root", "final"),
            AppCommandConsoleOutputMode.Human);

        Assert.Contains("  next: luotsi replay open --artifacts /tmp/replay-root", console.OutputLines);
        Assert.Contains("  next_actions: 1", console.OutputLines);
        Assert.Contains("    - kind=review_draft; title=Review generated draft; summary=Review before editing.; command=luotsi replay open --artifacts /tmp/replay-root", console.OutputLines);
    }

    [Fact]
    public void WriteSuccess_HumanOutput_Shows_Artifact_Intake_Audit()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());

        writer.WriteSuccess(
            "artifacts",
            DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            new
            {
                run_id = "20260526-120000-run",
                artifact_root = "/tmp/replay-root",
                has_artifact_intake_summary = true,
                artifact_intake_summary = new
                {
                    status = "restored",
                    share_safety = "lab_safe",
                    sha_verified = true,
                    package = "/tmp/share/replay-lab-safe.zip"
                },
                recommended_commands = new[]
                {
                    new { kind = "replay_open", summary = "Open replay.", command = "luotsi replay open --artifacts /tmp/replay-root" }
                }
            },
            new ArtifactData("/tmp/replay-root", "final"),
            AppCommandConsoleOutputMode.Human);

        Assert.Contains("  intake: status=restored; share_safety=lab_safe; sha_verified=true; package=/tmp/share/replay-lab-safe.zip", console.OutputLines);
        Assert.Contains("  next: luotsi replay open --artifacts /tmp/replay-root", console.OutputLines);
    }

    [Fact]
    public void WriteSuccess_HumanOutput_Shows_Run_Failure_Capsule()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());
        var failureArtifacts = new FailureArtifactBundle(
            ResultSchemas.FailureBundle,
            timeProvider.GetUtcNow(),
            "scenario",
            "login smoke",
            "/tmp/scenarios/login.json",
            1,
            "wait login button",
            "waitVisible",
            "System.InvalidOperationException",
            "button not visible",
            [
                new FailureArtifact("screenshot", "failures/wait-login-button.png"),
                new FailureArtifact("logcat", "logs/failure-logcat.txt"),
                new FailureArtifact("hierarchy", "hierarchy.xml"),
                new FailureArtifact("screen_state", "screen-state.json")
            ],
            []);
        var failureData = new ScenarioRunFailureData(
            "login smoke",
            "/tmp/scenarios/login.json",
            "failed",
            new ScenarioRunTiming(1500, 100, 1300, 100),
            new Dictionary<string, double>(),
            new ScenarioFailedStepResult(
                1,
                "wait login button",
                "waitVisible",
                750,
                new ScenarioStepTiming(750, 250, null, 500)),
            [],
            failureArtifacts);
        var result = new ScenarioRunBatchResult(
            "/tmp/scenarios",
            "failed",
            1,
            1,
            1,
            0,
            1,
            0,
            null,
            null,
            [
                ScenarioBatchItemResult.FromFailure(
                    "login smoke",
                    "/tmp/scenarios/login.json",
                    failureData,
                    new ErrorInfo("System.InvalidOperationException", "button not visible", "selector_or_screen_state"))
            ],
            ArtifactCommands:
            [
                new ScenarioArtifactCommandHint("open_artifacts", "Open the artifact browser for this run.", "luotsi artifacts open /tmp/latest-run"),
                new ScenarioArtifactCommandHint("replay_open", "Open the replay workbench for this run.", "luotsi replay open --artifacts /tmp/latest-run")
            ]);

        writer.WriteSuccess(
            "run",
            DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            result,
            new ArtifactData("/tmp/latest-run", "final"),
            AppCommandConsoleOutputMode.Human);

        Assert.Equal("FAIL run finished in 1000 ms.", console.OutputLines[0]);
        Assert.Contains("  status: failed", console.OutputLines);
        Assert.Contains("  summary: 1 selected; 0 passed; 1 failed; 0 sharded out", console.OutputLines);
        Assert.Contains("  primary_failure: login smoke / wait login button (waitVisible) / button not visible", console.OutputLines);
        Assert.Contains("  evidence: screenshots=1; hierarchies=1; screen_states=1; logcat=1", console.OutputLines);
        Assert.Contains("  next: luotsi artifacts open /tmp/latest-run", console.OutputLines);
        Assert.Contains("  artifact_commands: 2", console.OutputLines);
    }

    [Fact]
    public void WriteSuccess_HumanOutput_Shows_Replay_Open_Triage_Capsule()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());
        var result = new ReplayOpenResult(
            ResultSchemas.ReplayOpen,
            "/tmp/replay-root",
            "/tmp/replay-root/index.html",
            "/tmp/replay-root/index.md",
            null,
            null,
            null,
            null,
            1,
            1,
            new ReplayOpenPrimaryFailureResult(
                "scenario",
                "login-smoke-session",
                DateTimeOffset.Parse("2026-05-18T09:59:58Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                "failed",
                1,
                "emulator-5554",
                "login smoke",
                "wait login button",
                "waitVisible",
                "button not visible",
                "session-timeline.jsonl",
                "failure-capsule.json",
                "luotsi replay scrub --source-path session-timeline.jsonl --sequence 1 --context 3"),
            new ReplayOpenNextActionResult(
                "scrub_failure",
                "Scrub the failure window",
                "Start with the focused previous/current/next timeline view.",
                "luotsi replay scrub --artifacts /tmp/replay-root --failures --context 3 --write-markdown"),
            [
                new ReplayOpenCommandHintResult(
                    "graph",
                    "Write the semantic debug graph for the primary failure.",
                    "luotsi replay graph --artifacts /tmp/replay-root --failed --write-json --write-markdown")
            ],
            false,
            null,
            []);

        writer.WriteSuccess(
            "replay",
            DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            result,
            new ArtifactData("/tmp/replay-root", "final"),
            AppCommandConsoleOutputMode.Human);

        Assert.Equal("OK  replay completed in 1000 ms.", console.OutputLines[0]);
        Assert.Contains("  triage: 1 failure signal across 1 session", console.OutputLines);
        Assert.Contains("  primary_failure: login smoke / wait login button (waitVisible) / button not visible", console.OutputLines);
        Assert.Contains("  evidence: luotsi replay scrub --source-path session-timeline.jsonl --sequence 1 --context 3", console.OutputLines);
        Assert.Contains("  next_step: Scrub the failure window", console.OutputLines);
        Assert.Contains("  next: luotsi replay scrub --artifacts /tmp/replay-root --failures --context 3 --write-markdown", console.OutputLines);
        Assert.Contains("  commands: 1", console.OutputLines);
        Assert.Contains("    - kind=graph; summary=Write the semantic debug graph for the primary failure.; command=luotsi replay graph --artifacts /tmp/replay-root --failed --write-json --write-markdown", console.OutputLines);
    }

    [Fact]
    public void WriteSuccess_HumanOutput_Shows_Run_Summary_Packet_Triage_Path()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());
        var result = new RunSummaryResult(
            ResultSchemas.RunSummary,
            DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            "/tmp/replay-root",
            "needs_triage",
            "Failure signals found.",
            1,
            1,
            [
                new RunSummaryChecklistItemResult(
                    1,
                    "Run the recommended packet command",
                    "luotsi replay scrub --artifacts /tmp/replay-root --failures --context 3 --write-markdown",
                    "Highest-signal command."),
                new RunSummaryChecklistItemResult(
                    2,
                    "Read the primary failure fields before opening broad artifacts",
                    "luotsi replay scrub --source-path session-timeline.jsonl --sequence 1 --context 3",
                    "Focused evidence command.")
            ],
            new ReplayOpenPrimaryFailureResult(
                "scenario",
                "login-smoke-session",
                DateTimeOffset.Parse("2026-05-18T09:59:58Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                "failed",
                1,
                "emulator-5554",
                "login smoke",
                "wait login button",
                "waitVisible",
                "button not visible",
                "session-timeline.jsonl",
                "failure-capsule.json",
                "luotsi replay scrub --source-path session-timeline.jsonl --sequence 1 --context 3"),
            new ReplayOpenNextActionResult(
                "scrub_failure",
                "Scrub the failure window",
                "Start with the focused previous/current/next timeline view.",
                "luotsi replay scrub --artifacts /tmp/replay-root --failures --context 3 --write-markdown"),
            new RunSummaryEntryPoints(
                "/tmp/replay-root/index.html",
                "/tmp/replay-root/index.md",
                null,
                null,
                "/tmp/replay-root/run-summary.json",
                "/tmp/replay-root/run-summary.md"),
            []);

        writer.WriteSuccess(
            "replay",
            DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            result,
            new ArtifactData("/tmp/replay-root", "final"),
            AppCommandConsoleOutputMode.Human);

        Assert.Equal("OK  replay completed in 1000 ms.", console.OutputLines[0]);
        Assert.Contains("  status: needs_triage", console.OutputLines);
        Assert.Contains("  triage: 1 failure signal across 1 session", console.OutputLines);
        Assert.Contains("  primary_failure: login smoke / wait login button (waitVisible) / button not visible", console.OutputLines);
        Assert.Contains("  evidence: luotsi replay scrub --source-path session-timeline.jsonl --sequence 1 --context 3", console.OutputLines);
        Assert.Contains("  next_step: Scrub the failure window", console.OutputLines);
        Assert.Contains("  next: luotsi replay scrub --artifacts /tmp/replay-root --failures --context 3 --write-markdown", console.OutputLines);
        Assert.Contains("  triage_checklist: 2", console.OutputLines);
        Assert.Contains("    - step=1; action=Run the recommended packet command; command=luotsi replay scrub --artifacts /tmp/replay-root --failures --context 3 --write-markdown", console.OutputLines);
        Assert.Contains("  packet: /tmp/replay-root/run-summary.json", console.OutputLines);
        Assert.Contains("  markdown: /tmp/replay-root/run-summary.md", console.OutputLines);
    }

    [Fact]
    public void WriteSuccess_HumanOutput_Shows_Run_Summary_Check_As_Validated_Triage_Path()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());
        var result = new RunSummaryCheckResult(
            ResultSchemas.RunSummaryCheck,
            DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            "/tmp/replay-root",
            "/tmp/replay-root/run-summary.json",
            "valid",
            "needs_triage",
            1,
            1,
            "luotsi replay scrub --artifacts /tmp/replay-root --failures --context 3 --write-markdown",
            new ReplayOpenNextActionResult(
                "scrub_failure",
                "Scrub the failure window",
                "Start with the focused previous/current/next timeline view.",
                "luotsi replay scrub --artifacts /tmp/replay-root --failures --context 3 --write-markdown"),
            [
                new RunSummaryChecklistItemResult(
                    1,
                    "Run the recommended packet command",
                    "luotsi replay scrub --artifacts /tmp/replay-root --failures --context 3 --write-markdown",
                    "Highest-signal command.")
            ],
            null,
            "/tmp/replay-root/run-summary.md");

        writer.WriteSuccess(
            "replay",
            DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            result,
            new ArtifactData("/tmp/replay-root", "final"),
            AppCommandConsoleOutputMode.Human);

        Assert.Equal("OK  replay completed in 1000 ms.", console.OutputLines[0]);
        Assert.Contains("  status: valid", console.OutputLines);
        Assert.Contains("  packet_status: needs_triage", console.OutputLines);
        Assert.Contains("  triage: 1 failure signal across 1 session", console.OutputLines);
        Assert.Contains("  next_step: Scrub the failure window", console.OutputLines);
        Assert.Contains("  next: luotsi replay scrub --artifacts /tmp/replay-root --failures --context 3 --write-markdown", console.OutputLines);
        Assert.Contains("  triage_checklist: 1", console.OutputLines);
        Assert.Contains("  packet: /tmp/replay-root/run-summary.json", console.OutputLines);
        Assert.Contains("  markdown: /tmp/replay-root/run-summary.md", console.OutputLines);
    }

    [Fact]
    public void WriteSuccess_HumanOutput_Shows_Replay_Capsule_Summary()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());
        var result = new ReplayCapsuleResult(
            ResultSchemas.ReplayCapsule,
            "/tmp/replay-root",
            1,
            1,
            true,
            true,
            "Found replay action history for scenario drafting.",
            new ReplayCapsuleScenarioDraftArtifacts("scenario-draft-summary.json", "scenario-draft.md", "draft-scenario.json"),
            null,
            new ReplayCapsuleArtifactIntakeArtifacts("artifact-intake-summary.json", "artifact-intake.md"),
            new ReplayCapsuleArtifactIntakeSummary(
                "restored",
                "/tmp/share/replay-lab-safe.zip",
                "/tmp/replay-root",
                7,
                "lab_safe",
                true,
                "abc123",
                true,
                2),
            null,
            null,
            new ReplayCapsulePrimaryFailureResult(
                "login smoke",
                "wait login button",
                "waitVisible",
                "button not visible",
                "failure-capsule.json",
                "session-timeline.jsonl",
                "luotsi replay timeline --artifacts /tmp/replay-root --source-path session-timeline.jsonl --sequence 8"),
            new ReplayCapsuleArtifactCounts(1, 0, 1, 1, 1, 1, 1),
            [],
            [],
            [
                new ReplayCapsuleNextStep(
                    "scrub_failure",
                    "Scrub the failure window",
                    "Start with the previous/current/next event view.",
                    "luotsi replay scrub --artifacts /tmp/replay-root --failures --context 3 --write-markdown")
            ],
            [
                new ReplayCapsuleCommandHint(
                    "luotsi replay graph --artifacts /tmp/replay-root --failed --write-json --write-markdown",
                    "Write the semantic debug graph for the failure bundle.")
            ]);

        writer.WriteSuccess(
            "replay",
            DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            result,
            new ArtifactData("/tmp/replay-root", "final"),
            AppCommandConsoleOutputMode.Human);

        Assert.Equal("OK  replay completed in 1000 ms.", console.OutputLines[0]);
        Assert.Contains("  triage: 1 failure signal across 1 session", console.OutputLines);
        Assert.Contains("  primary_failure: login smoke / wait login button (waitVisible) / button not visible", console.OutputLines);
        Assert.Contains("  evidence: luotsi replay timeline --artifacts /tmp/replay-root --source-path session-timeline.jsonl --sequence 8", console.OutputLines);
        Assert.Contains("  evidence: screenshots=1; logs=1; hierarchies=1; screen_states=1; reports=1; timelines=1", console.OutputLines);
        Assert.Contains("  intake: status=restored; share_safety=lab_safe; sha_verified=true; package=/tmp/share/replay-lab-safe.zip", console.OutputLines);
        Assert.Contains("  next_step: Scrub the failure window", console.OutputLines);
        Assert.Contains("  next: luotsi replay scrub --artifacts /tmp/replay-root --failures --context 3 --write-markdown", console.OutputLines);
        Assert.Contains("  recommended_next_steps: 1", console.OutputLines);
        Assert.Contains("  suggested_commands: 1", console.OutputLines);
        Assert.Contains("    - summary=Write the semantic debug graph for the failure bundle.; command=luotsi replay graph --artifacts /tmp/replay-root --failed --write-json --write-markdown", console.OutputLines);
    }

    [Fact]
    public void WriteFailure_HumanOutput_Shows_Failure_Bundle_Evidence_And_Next_Command()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());
        var failureBundle = new FailureArtifactBundle(
            ResultSchemas.FailureBundle,
            timeProvider.GetUtcNow(),
            "command",
            "wait-visible",
            null,
            1,
            "wait login button",
            "waitVisible",
            "System.InvalidOperationException",
            "button not visible",
            [
                new FailureArtifact("screenshot", "failures/wait-login-button.png"),
                new FailureArtifact("logcat", "logs/failure-logcat.txt"),
                new FailureArtifact("hierarchy", "hierarchy.xml")
            ],
            []);

        writer.WriteFailure(
            "wait-visible",
            DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            failureBundle,
            new ArtifactData("/tmp/failure-root", "final"),
            new InvalidOperationException("button not visible"),
            "selector_or_screen_state",
            AppCommandConsoleOutputMode.Human);

        Assert.Equal("FAIL wait-visible failed in 1000 ms.", console.OutputLines[0]);
        Assert.Contains("  selector_or_screen_state: button not visible", console.OutputLines);
        Assert.Contains("  scope: command wait-visible", console.OutputLines);
        Assert.Contains("  failed_step: wait login button (waitVisible)", console.OutputLines);
        Assert.Contains("  evidence: screenshots=1; hierarchies=1; logcat=1", console.OutputLines);
        Assert.Contains("  next: luotsi artifacts open /tmp/failure-root", console.OutputLines);
    }

    [Fact]
    public void WriteSuccess_HumanOutput_Uses_Description_When_Command_Hint_Has_No_Summary()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());

        writer.WriteSuccess(
            "run",
            DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            new
            {
                artifact_commands = new object[]
                {
                    new { kind = "open_artifacts", description = "Open the artifact browser for this run.", command = "luotsi artifacts open /tmp/latest-run" }
                }
            },
            new ArtifactData("/tmp/latest-run", "final"),
            AppCommandConsoleOutputMode.Human);

        Assert.Contains("    - kind=open_artifacts; summary=Open the artifact browser for this run.; command=luotsi artifacts open /tmp/latest-run", console.OutputLines);
    }

    [Fact]
    public async Task RunAsync_ConsoleOutput_Quiet_Suppresses_Success_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["version", "--console-output", "quiet"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(console.OutputLines);
        Assert.Empty(console.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_Quiet_Flag_Suppresses_Success_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["version", "--quiet"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(console.OutputLines);
        Assert.Empty(console.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_ConsoleOutput_Quiet_Keeps_Failure_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["tap", "--x", "nope", "--y", "1", "--console-output", "quiet"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task RunAsync_Quiet_Flag_Conflicts_With_Human_Output()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["version", "--quiet", "--human"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Contains("--quiet", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Invalid_ConsoleOutput_Returns_Usage_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["version", "--console-output", "yaml"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("--console-output", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private static BuildProvenance CreateProvenance() =>
        new BuildProvenanceProvider(new FakeEnvironmentVariables(new Dictionary<string, string>())).Create();
}
