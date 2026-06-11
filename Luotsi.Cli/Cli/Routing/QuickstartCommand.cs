using System.Text;
using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal static class QuickstartCommand
{
    private const string JsonFileName = "quickstart-plan.json";
    private const string MarkdownFileName = "quickstart-plan.md";
    private const string ProofPackJsonFileName = "evaluation-proof-pack.json";
    private const string ProofPackMarkdownFileName = "evaluation-proof-pack.md";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<QuickstartResult> RunAsync(CliOptions options, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var jsonPath = options.HasFlag("write-json") ? Path.Join(artifacts.Root, JsonFileName) : null;
        var markdownPath = options.HasFlag("write-markdown") ? Path.Join(artifacts.Root, MarkdownFileName) : null;
        var proofPackJsonPath = options.HasFlag("write-json") ? Path.Join(artifacts.Root, ProofPackJsonFileName) : null;
        var proofPackMarkdownPath = options.HasFlag("write-markdown") ? Path.Join(artifacts.Root, ProofPackMarkdownFileName) : null;
        var writesArtifacts = jsonPath is not null || markdownPath is not null;
        var result = Build(options, writesArtifacts ? artifacts.Root : null);

        if (!writesArtifacts)
        {
            return result;
        }

        var resultWithHandoff = result with
        {
            Handoff = new QuickstartHandoffResult(
                artifacts.Root,
                jsonPath,
                markdownPath,
                proofPackJsonPath,
                proofPackMarkdownPath,
                jsonPath is null || markdownPath is null
                    ? $"luotsi quickstart {BuildCurrentOptionFlags(result.Inputs)} --write-json --write-markdown".Replace("  ", " ", StringComparison.Ordinal).Trim()
                    : null)
        };

        if (jsonPath is not null)
        {
            await WriteTextArtifactAsync(artifacts, JsonFileName, JsonSerializer.Serialize(resultWithHandoff, AppCommandJson.Options) + Environment.NewLine).ConfigureAwait(false);
        }

        if (markdownPath is not null)
        {
            await WriteTextArtifactAsync(artifacts, MarkdownFileName, BuildMarkdown(resultWithHandoff)).ConfigureAwait(false);
        }

        if (proofPackJsonPath is not null)
        {
            await WriteTextArtifactAsync(artifacts, ProofPackJsonFileName, JsonSerializer.Serialize(resultWithHandoff.ProofPack, AppCommandJson.Options) + Environment.NewLine).ConfigureAwait(false);
        }

        if (proofPackMarkdownPath is not null)
        {
            await WriteTextArtifactAsync(artifacts, ProofPackMarkdownFileName, BuildProofPackMarkdown(resultWithHandoff.ProofPack)).ConfigureAwait(false);
        }

        await artifacts.RefreshIndexAsync().ConfigureAwait(false);
        return resultWithHandoff;
    }

    public static QuickstartResult Build(CliOptions options, string? proofPackArtifactRoot = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var suppliedDevice = Normalize(options.Get("device"));
        var device = suppliedDevice ?? "<adb serial>";
        var package = Normalize(options.Get("package"));
        var packageValue = package ?? "<app.id>";
        var artifacts = Normalize(options.Get("artifacts")) ?? "artifacts/first-run";
        var scenarioPath = Normalize(options.Get("path")) ?? "scenarios";
        var scenarioFile = Normalize(options.Get("file")) ?? CombineCommandPath(scenarioPath, "first-run-smoke.json");

        var deviceFlag = $"--device {Quote(device)}";
        var packageFlag = $"--package {Quote(packageValue)}";
        var artifactsFlag = $"--artifacts {Quote(artifacts)}";
        var outputDirFlag = $"--output-dir {Quote(artifacts)}";
        var scenarioFileValue = Quote(scenarioFile);
        var scenarioPathValue = Quote(scenarioPath);
        var doctorCommand = suppliedDevice is null
            ? "luotsi doctor"
            : $"luotsi doctor {deviceFlag} {packageFlag}";
        var repairCommand = $"luotsi doctor {deviceFlag} {packageFlag} --fix";
        var firstCommand = suppliedDevice is null ? doctorCommand : repairCommand;
        var quickstartHandoffCommand = $"luotsi quickstart {BuildCurrentOptionFlags(new QuickstartInputResult(suppliedDevice, package, artifacts, scenarioPath))} --write-json --write-markdown"
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
        var deviceProofCommand = doctorCommand;
        var deviceTruthProofCommand = $"luotsi screen-state {deviceFlag} {artifactsFlag}";

        var steps = new[]
        {
            new QuickstartStepResult(
                0,
                "verify_install",
                "Confirm the installed binary and bundled assets are discoverable.",
                "luotsi version",
                "The envelope reports runtime_version, installed_tag, and install_root.",
                RequiresDevice: false,
                RequiresPackage: false),
            new QuickstartStepResult(
                1,
                "select_device",
                suppliedDevice is null
                    ? "Ask Luotsi to select or explain the target Android device."
                    : "Confirm ADB can see the selected Android device.",
                suppliedDevice is null ? "luotsi doctor" : "luotsi devices",
                suppliedDevice is null
                    ? "The envelope reports next_command for the selected-device doctor report."
                    : "The device appears with a serial you can pass to --device.",
                RequiresDevice: false,
                RequiresPackage: false),
            new QuickstartStepResult(
                2,
                "repair_readiness",
                "Run the onboarding doctor and apply Luotsi-owned setup fixes.",
                repairCommand,
                "readiness_plan.status is ready or the next_command explains the blocker.",
                RequiresDevice: true,
                RequiresPackage: package is null),
            new QuickstartStepResult(
                3,
                "capture_device_truth",
                "Take a structured screen-state snapshot from the real device.",
                $"luotsi screen-state {deviceFlag} {artifactsFlag}",
                "The result returns visible UI state and the artifact root is preserved.",
                RequiresDevice: true,
                RequiresPackage: false),
            new QuickstartStepResult(
                4,
                "start_agent_loop",
                "Open the JSONL inspect loop an AI agent can read and drive.",
                $"luotsi inspect {deviceFlag} {artifactsFlag}",
                "The session emits screen snapshots, deltas, command results, and replay artifacts.",
                RequiresDevice: true,
                RequiresPackage: false),
            new QuickstartStepResult(
                5,
                "map_or_draft",
                "Map the app into reviewable automation candidates, then preserve replay evidence.",
                $"luotsi discover {deviceFlag} {packageFlag} --budget 5m {outputDirFlag}",
                "discovery-map.json, session-replay.json, and scenario-candidates are written.",
                RequiresDevice: true,
                RequiresPackage: package is null)
        };

        var recommendedCommands = new[]
        {
            new QuickstartRecommendedCommandResult(
                "doctor",
                suppliedDevice is null
                    ? "List adb-visible devices and get the exact selected-device doctor command."
                    : "Diagnose the local machine, ADB transport, device, target package, and live-view prerequisites.",
                doctorCommand),
            new QuickstartRecommendedCommandResult(
                "repair",
                "Apply Luotsi-owned setup fixes and rerun readiness checks.",
                repairCommand),
            new QuickstartRecommendedCommandResult(
                "agent_loop",
                "Start the structured JSONL loop for an AI operator.",
                $"luotsi inspect {deviceFlag} {artifactsFlag}"),
            new QuickstartRecommendedCommandResult(
                "discover",
                "Explore the target app on a bounded budget and produce review-required scenario candidates.",
                $"luotsi discover {deviceFlag} {packageFlag} --budget 5m {outputDirFlag}"),
            new QuickstartRecommendedCommandResult(
                "scenario_seed",
                "Create a small scenario file when you want to hand-author the first repeatable playbook.",
                $"luotsi scenario-init --file {scenarioFileValue} --name \"first-run smoke\""),
            new QuickstartRecommendedCommandResult(
                "scenario_validate",
                "Validate scenarios without touching a device.",
                $"luotsi scenario-validate --path {scenarioPathValue}"),
            new QuickstartRecommendedCommandResult(
                "ci_dry_run",
                "Check the CI command shape before using a lab device.",
                $"luotsi run --path {scenarioPathValue} {deviceFlag} {packageFlag} --dry-run"),
            new QuickstartRecommendedCommandResult(
                "replay",
                "Reopen the latest local artifact bundle instead of rerunning the device session.",
                $"luotsi replay open --last --artifacts {Quote(artifacts)} --dry-run")
        };

        var proofChecks = new[]
        {
            new QuickstartProofCheckResult(
                "install",
                "Luotsi binary and bundled assets are discoverable.",
                "luotsi version",
                "Envelope includes runtime_version, command_path, install_root, and helper/bundled asset status.",
                "ready_to_run",
                null),
            new QuickstartProofCheckResult(
                "device",
                suppliedDevice is null
                    ? "At least one adb device can be selected for the readiness report."
                    : "The selected adb device is visible and stable.",
                deviceProofCommand,
                "readiness_plan.status is ready, or blockers include exact remediation commands.",
                ResolveProofCheckStatus(deviceProofCommand),
                ResolveProofCheckBlockedReason(deviceProofCommand)),
            new QuickstartProofCheckResult(
                "artifact_handoff",
                "First-run handoff artifacts can be written for a human or AI operator.",
                quickstartHandoffCommand,
                "quickstart-plan.json, quickstart-plan.md, evaluation-proof-pack.json, evaluation-proof-pack.md, and index.md exist in the artifact root.",
                "ready_to_run",
                null),
            new QuickstartProofCheckResult(
                "device_truth",
                "The selected device can produce structured UI evidence.",
                deviceTruthProofCommand,
                "Screen-state output includes visible UI data and preserves the artifact root.",
                ResolveProofCheckStatus(deviceTruthProofCommand),
                ResolveProofCheckBlockedReason(deviceTruthProofCommand)),
            new QuickstartProofCheckResult(
                "replay",
                "Captured evidence can be reopened without touching the device.",
                $"luotsi replay open --last --artifacts {Quote(artifacts)} --dry-run",
                "Replay output returns next actions and references the latest preserved artifact bundle.",
                "ready_after_artifact",
                "Run the artifact handoff or a device/session command before expecting --last to resolve.")
        };

        var proofPack = BuildProofPack(proofPackArtifactRoot ?? artifacts, device, packageValue, scenarioPath, scenarioFile, recommendedCommands);

        return new QuickstartResult(
            ResultSchemas.Quickstart,
            "ready_to_start",
            "Get from fresh install to real-device evidence in under five minutes.",
            "5m",
            new QuickstartInputResult(
                suppliedDevice,
                package,
                artifacts,
                scenarioPath),
            steps,
            recommendedCommands,
            proofChecks,
            firstCommand,
            "Run Luotsi commands as the Android actuation surface. Read JSON envelopes and JSONL events, preserve artifacts, reopen replay evidence before proposing scenario changes, and pause for human review before destructive actions.",
            [
                "Local-first Android device truth over ADB, without requiring a hosted device farm.",
                "Structured JSON envelopes and JSONL sessions built for AI agents and CI.",
                "Replayable artifacts, timelines, screenshots, logs, and scenario drafts for post-run triage.",
                "A host-side control layer that can sit beside Appium, Maestro, Firebase Test Lab, or cloud AI testing services."
            ],
            [
                "automation_frameworks: Appium, Maestro, Detox",
                "cloud_device_infrastructure: Firebase Test Lab, BrowserStack App Automate",
                "ai_test_authoring: BrowserStack Test Companion, LambdaTest KaneAI"
            ],
            proofPack,
            Handoff: null);
    }

    private static QuickstartProofPackResult BuildProofPack(
        string artifactRoot,
        string device,
        string packageValue,
        string scenarioPath,
        string scenarioFile,
        IReadOnlyList<QuickstartRecommendedCommandResult> recommendedCommands)
    {
        var deviceFlag = $"--device {Quote(device)}";
        var packageFlag = $"--package {Quote(packageValue)}";
        var artifactFlag = $"--artifacts {Quote(artifactRoot)}";
        var scenarioPathValue = Quote(scenarioPath);
        var scenarioFileValue = Quote(scenarioFile);

        return new QuickstartProofPackResult(
            "luotsi-evaluation-proof-pack.v1",
            "collecting",
            artifactRoot,
            "Use this checklist to decide whether the first Luotsi evaluation left enough evidence for production discussion.",
            [
                new(
                    "install_verified",
                    "Luotsi binary and bundled assets are visible.",
                    "luotsi version",
                    "The command succeeds and reports runtime_version, installed_tag, and install_root."),
                new(
                    "device_ready",
                    "The selected real device is adb-visible and Luotsi can explain or fix readiness blockers.",
                    $"luotsi doctor {deviceFlag} {packageFlag} --fix",
                    "readiness_plan.status is ready, or blockers and next_command are explicit enough for handoff."),
                new(
                    "screen_state_evidence",
                    "A one-shot real-device state snapshot exists.",
                    $"luotsi screen-state {deviceFlag} {artifactFlag}",
                    "The envelope includes ok=true, visible UI data, and artifacts.artifact_root."),
                new(
                    "agent_loop_evidence",
                    "An agent-readable JSONL session can be opened and later replayed.",
                    $"luotsi inspect {deviceFlag} {artifactFlag}",
                    "The session writes screen_snapshot, command_result, screen_delta, and session_ended events."),
                new(
                    "scenario_seeded",
                    "The first repeatable CI candidate exists or has been intentionally deferred.",
                    $"luotsi scenario-init --file {scenarioFileValue} --name \"first-run smoke\"",
                    "The scenario file is ready for review before device execution."),
                new(
                    "scenario_validated",
                    "Scenario syntax is validated without touching a device.",
                    $"luotsi scenario-validate --path {scenarioPathValue}",
                    "Validation succeeds or reports reviewable authoring errors."),
                new(
                    "replayable_handoff",
                    "The artifact root can be reopened after device access is gone.",
                    $"luotsi replay open --artifacts {Quote(artifactRoot)} --dry-run",
                    "Replay returns the primary failure or an explicit no-failure summary plus follow-up commands."),
                new(
                    "shareable_package",
                    "The evidence can be packaged and verified before team handoff.",
                    $"luotsi artifacts pack {Quote(artifactRoot)} --output {Quote(CombineCommandPath(artifactRoot, "first-run.zip"))} --redact lab-safe",
                    "The package reports SHA-256 and can pass artifacts verify --require-lab-safe.")
            ],
            [
                "At least one live-device command produced an artifact root.",
                "A reviewer can reopen evidence with replay open without reconnecting the device.",
                "A CI candidate is either validated or explicitly deferred with a blocker.",
                "Any shared bundle is packed with lab-safe redaction and verified before intake."
            ],
            recommendedCommands);
    }

    public static async Task<QuickstartVerifyResult> VerifyAsync(CliOptions options, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var plan = Build(options, artifacts.Root);
        var readyChecks = plan.ProofChecks
            .Where(static check => string.Equals(check.Status, "ready_to_run", StringComparison.OrdinalIgnoreCase))
            .Select(QuickstartVerifyCheckResult.FromProofCheck)
            .ToArray();
        var blockedChecks = plan.ProofChecks
            .Where(static check => string.Equals(check.Status, "needs_input", StringComparison.OrdinalIgnoreCase))
            .Select(QuickstartVerifyCheckResult.FromProofCheck)
            .ToArray();
        var laterChecks = plan.ProofChecks
            .Where(static check =>
                !string.Equals(check.Status, "ready_to_run", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(check.Status, "needs_input", StringComparison.OrdinalIgnoreCase))
            .Select(QuickstartVerifyCheckResult.FromProofCheck)
            .ToArray();
        var status = blockedChecks.Length == 0 ? "ready_to_verify" : "blocked";
        var nextCommand = readyChecks.FirstOrDefault()?.Command ??
            blockedChecks.FirstOrDefault()?.Command ??
            plan.FirstCommand;
        var localProofs = await RunLocalProofsAsync(plan, artifacts).ConfigureAwait(false);
        var passedLocalProofCount = localProofs.Count(static proof => string.Equals(proof.Status, "passed", StringComparison.OrdinalIgnoreCase));

        return new QuickstartVerifyResult(
            ResultSchemas.QuickstartVerify,
            status,
            status == "ready_to_verify"
                ? "All immediate quickstart proof checks have concrete commands; run them in order, then complete later artifact-gated checks."
                : "Some quickstart proof checks still need concrete input before the first-run proof path is executable.",
            plan.Inputs,
            plan.ProofChecks.Count,
            readyChecks.Length,
            blockedChecks.Length,
            laterChecks.Length,
            localProofs.Count,
            passedLocalProofCount,
            nextCommand,
            localProofs,
            readyChecks,
            blockedChecks,
            laterChecks,
            [
                new QuickstartRecommendedCommandResult(
                    "plan",
                    "Review the full five-minute path and proof-check evidence expectations.",
                    $"luotsi quickstart {BuildCurrentOptionFlags(plan.Inputs)}".Replace("  ", " ", StringComparison.Ordinal).Trim()),
                new QuickstartRecommendedCommandResult(
                    "handoff",
                    "Persist the first-run plan and proof checklist for a human or AI operator.",
                    $"luotsi quickstart {BuildCurrentOptionFlags(plan.Inputs)} --write-json --write-markdown".Replace("  ", " ", StringComparison.Ordinal).Trim())
            ]);
    }

    private static async Task<IReadOnlyList<QuickstartLocalProofResult>> RunLocalProofsAsync(QuickstartResult plan, ArtifactSession artifacts)
    {
        var results = new List<QuickstartLocalProofResult>
        {
            new(
                "install",
                "passed",
                "luotsi command envelope is executing and can evaluate the quickstart contract.",
                plan.ProofChecks.First(static check => string.Equals(check.Kind, "install", StringComparison.Ordinal)).Command,
                null)
        };

        var resultWithHandoff = plan with
        {
            Handoff = new QuickstartHandoffResult(
                artifacts.Root,
                Path.Join(artifacts.Root, JsonFileName),
                Path.Join(artifacts.Root, MarkdownFileName),
                Path.Join(artifacts.Root, ProofPackJsonFileName),
                Path.Join(artifacts.Root, ProofPackMarkdownFileName),
                null)
        };

        await WriteTextArtifactAsync(artifacts, JsonFileName, JsonSerializer.Serialize(resultWithHandoff, AppCommandJson.Options) + Environment.NewLine).ConfigureAwait(false);
        await WriteTextArtifactAsync(artifacts, MarkdownFileName, BuildMarkdown(resultWithHandoff)).ConfigureAwait(false);
        await WriteTextArtifactAsync(artifacts, ProofPackJsonFileName, JsonSerializer.Serialize(resultWithHandoff.ProofPack, AppCommandJson.Options) + Environment.NewLine).ConfigureAwait(false);
        await WriteTextArtifactAsync(artifacts, ProofPackMarkdownFileName, BuildProofPackMarkdown(resultWithHandoff.ProofPack)).ConfigureAwait(false);
        await artifacts.RefreshIndexAsync().ConfigureAwait(false);

        results.Add(new(
            "artifact_handoff",
            "passed",
            "quickstart-plan.json, quickstart-plan.md, evaluation-proof-pack.json, evaluation-proof-pack.md, and index.md were written in this command artifact root.",
            plan.ProofChecks.First(static check => string.Equals(check.Kind, "artifact_handoff", StringComparison.Ordinal)).Command,
            artifacts.Root));

        return results;
    }

    private static string BuildMarkdown(QuickstartResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi quickstart handoff");
        builder.AppendLine();
        builder.AppendLine(result.Goal);
        builder.AppendLine();
        builder.AppendLine("## First command");
        builder.AppendLine();
        builder.AppendLine("```bash");
        builder.AppendLine(result.FirstCommand);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Inputs");
        builder.AppendLine();
        builder.AppendLine($"- Device: {result.Inputs.Device ?? "<select with luotsi doctor>"}");
        builder.AppendLine($"- Package: {result.Inputs.Package ?? "<app.id>"}");
        builder.AppendLine($"- Artifacts: {result.Inputs.Artifacts}");
        builder.AppendLine($"- Scenario path: {result.Inputs.ScenarioPath}");
        builder.AppendLine();
        builder.AppendLine("## Five-minute path");
        builder.AppendLine();
        foreach (var step in result.Steps)
        {
            builder.AppendLine($"### Minute {step.Minute}: {step.Title}");
            builder.AppendLine();
            builder.AppendLine("```bash");
            builder.AppendLine(step.Command);
            builder.AppendLine("```");
            builder.AppendLine();
            builder.AppendLine($"Success signal: {step.SuccessSignal}");
            builder.AppendLine();
        }

        builder.AppendLine("## Recommended commands");
        builder.AppendLine();
        foreach (var command in result.RecommendedCommands)
        {
            builder.AppendLine($"- {command.Kind}: `{command.Command}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Proof checks");
        builder.AppendLine();
        foreach (var check in result.ProofChecks)
        {
            builder.AppendLine($"- {check.Kind} ({check.Status}): `{check.Command}`");
            builder.AppendLine($"  - Summary: {check.Summary}");
            builder.AppendLine($"  - Evidence: {check.Evidence}");
            if (!string.IsNullOrWhiteSpace(check.BlockedReason))
            {
                builder.AppendLine($"  - Note: {check.BlockedReason}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Evaluation proof pack");
        builder.AppendLine();
        builder.AppendLine("Use the proof pack when a teammate or AI operator needs to decide whether this first run produced production-grade evidence.");
        if (result.Handoff?.ProofPackJsonPath is not null)
        {
            builder.AppendLine($"- JSON: `{result.Handoff.ProofPackJsonPath}`");
        }

        if (result.Handoff?.ProofPackMarkdownPath is not null)
        {
            builder.AppendLine($"- Markdown: `{result.Handoff.ProofPackMarkdownPath}`");
        }

        builder.AppendLine();
        foreach (var gate in result.ProofPack.Gates)
        {
            builder.AppendLine($"- {gate.Id}: `{gate.Command}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Agent prompt");
        builder.AppendLine();
        builder.AppendLine(result.AgentPrompt);
        builder.AppendLine();
        return builder.ToString();
    }

    private static string BuildProofPackMarkdown(QuickstartProofPackResult proofPack)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi evaluation proof pack");
        builder.AppendLine();
        builder.AppendLine(proofPack.Goal);
        builder.AppendLine();
        builder.AppendLine($"- Status: {proofPack.Status}");
        builder.AppendLine($"- Artifact root: {proofPack.ArtifactRoot}");
        builder.AppendLine();
        builder.AppendLine("## Evidence gates");
        builder.AppendLine();
        foreach (var gate in proofPack.Gates)
        {
            builder.AppendLine($"### {gate.Id}");
            builder.AppendLine();
            builder.AppendLine(gate.Description);
            builder.AppendLine();
            builder.AppendLine("```bash");
            builder.AppendLine(gate.Command);
            builder.AppendLine("```");
            builder.AppendLine();
            builder.AppendLine($"Success signal: {gate.SuccessSignal}");
            builder.AppendLine();
        }

        builder.AppendLine("## Production-ready when");
        builder.AppendLine();
        foreach (var criterion in proofPack.ProductionReadyWhen)
        {
            builder.AppendLine($"- {criterion}");
        }

        builder.AppendLine();
        builder.AppendLine("## Recommended commands");
        builder.AppendLine();
        foreach (var command in proofPack.RecommendedCommands)
        {
            builder.AppendLine($"- {command.Kind}: `{command.Command}`");
        }

        builder.AppendLine();
        return builder.ToString();
    }

    private static async Task WriteTextArtifactAsync(ArtifactSession artifacts, string name, string text)
    {
        await using var stream = artifacts.OpenArtifactWrite(name);
        var bytes = Utf8NoBom.GetBytes(text);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
    }

    private static string BuildCurrentOptionFlags(QuickstartInputResult inputs)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(inputs.Device))
        {
            builder.Append(" --device ").Append(Quote(inputs.Device));
        }

        if (!string.IsNullOrWhiteSpace(inputs.Package))
        {
            builder.Append(" --package ").Append(Quote(inputs.Package));
        }

        if (!string.IsNullOrWhiteSpace(inputs.Artifacts))
        {
            builder.Append(" --artifacts ").Append(Quote(inputs.Artifacts));
        }

        if (!string.IsNullOrWhiteSpace(inputs.ScenarioPath))
        {
            builder.Append(" --path ").Append(Quote(inputs.ScenarioPath));
        }

        return builder.ToString();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ResolveProofCheckStatus(string command) =>
        command.Contains('<', StringComparison.Ordinal) ? "needs_input" : "ready_to_run";

    private static string? ResolveProofCheckBlockedReason(string command)
    {
        if (!command.Contains('<', StringComparison.Ordinal))
        {
            return null;
        }

        var missing = new List<string>();
        if (command.Contains("<adb serial>", StringComparison.Ordinal))
        {
            missing.Add("--device");
        }

        if (command.Contains("<app.id>", StringComparison.Ordinal))
        {
            missing.Add("--package");
        }

        if (missing.Count == 0)
        {
            return "Replace placeholder values before running this proof check.";
        }

        var missingText = string.Join(" and ", missing);
        return missing.Contains("--device")
            ? $"Provide {missingText} or run the earlier selection proof first."
            : $"Provide {missingText} before running this proof check.";
    }

    private static string CombineCommandPath(string path, string fileName) =>
        path.TrimEnd('/', '\\') + "/" + fileName;

    private static string Quote(string value)
    {
        if (value.StartsWith('<') && value.EndsWith('>'))
        {
            return value;
        }

        return value.Any(static character => char.IsWhiteSpace(character) || character == '"')
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }
}

internal sealed record QuickstartResult(
    string Schema,
    string Status,
    string Goal,
    string TimeBudget,
    QuickstartInputResult Inputs,
    IReadOnlyList<QuickstartStepResult> Steps,
    IReadOnlyList<QuickstartRecommendedCommandResult> RecommendedCommands,
    IReadOnlyList<QuickstartProofCheckResult> ProofChecks,
    string FirstCommand,
    string AgentPrompt,
    IReadOnlyList<string> Differentiators,
    IReadOnlyList<string> SimilarToolCategories,
    QuickstartProofPackResult ProofPack,
    QuickstartHandoffResult? Handoff);

internal sealed record QuickstartHandoffResult(
    string ArtifactRoot,
    string? JsonPath,
    string? MarkdownPath,
    string? ProofPackJsonPath,
    string? ProofPackMarkdownPath,
    string? RecommendedCommand);

internal sealed record QuickstartProofPackResult(
    string Schema,
    string Status,
    string ArtifactRoot,
    string Goal,
    IReadOnlyList<QuickstartProofGateResult> Gates,
    IReadOnlyList<string> ProductionReadyWhen,
    IReadOnlyList<QuickstartRecommendedCommandResult> RecommendedCommands);

internal sealed record QuickstartProofGateResult(
    string Id,
    string Description,
    string Command,
    string SuccessSignal);

internal sealed record QuickstartInputResult(
    string? Device,
    string? Package,
    string Artifacts,
    string ScenarioPath);

internal sealed record QuickstartStepResult(
    int Minute,
    string Id,
    string Title,
    string Command,
    string SuccessSignal,
    bool RequiresDevice,
    bool RequiresPackage);

internal sealed record QuickstartRecommendedCommandResult(
    string Kind,
    string Summary,
    string Command);

internal sealed record QuickstartProofCheckResult(
    string Kind,
    string Summary,
    string Command,
    string Evidence,
    string Status,
    string? BlockedReason);

internal sealed record QuickstartVerifyResult(
    string Schema,
    string Status,
    string Summary,
    QuickstartInputResult Inputs,
    int Total,
    int ReadyCount,
    int BlockedCount,
    int LaterCount,
    int LocalProofCount,
    int PassedLocalProofCount,
    string NextCommand,
    IReadOnlyList<QuickstartLocalProofResult> LocalProofs,
    IReadOnlyList<QuickstartVerifyCheckResult> ReadyChecks,
    IReadOnlyList<QuickstartVerifyCheckResult> BlockedChecks,
    IReadOnlyList<QuickstartVerifyCheckResult> LaterChecks,
    IReadOnlyList<QuickstartRecommendedCommandResult> RecommendedCommands);

internal sealed record QuickstartVerifyCheckResult(
    string Kind,
    string Status,
    string Command,
    string Evidence,
    string? BlockedReason)
{
    public static QuickstartVerifyCheckResult FromProofCheck(QuickstartProofCheckResult check) =>
        new(check.Kind, check.Status, check.Command, check.Evidence, check.BlockedReason);
}

internal sealed record QuickstartLocalProofResult(
    string Kind,
    string Status,
    string Evidence,
    string Command,
    string? ArtifactRoot);
