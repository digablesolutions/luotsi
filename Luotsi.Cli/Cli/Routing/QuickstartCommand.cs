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
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<QuickstartResult> RunAsync(CliOptions options, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var result = Build(options);
        var jsonPath = options.HasFlag("write-json") ? Path.Join(artifacts.Root, JsonFileName) : null;
        var markdownPath = options.HasFlag("write-markdown") ? Path.Join(artifacts.Root, MarkdownFileName) : null;

        if (jsonPath is null && markdownPath is null)
        {
            return result;
        }

        var resultWithHandoff = result with
        {
            Handoff = new QuickstartHandoffResult(
                artifacts.Root,
                jsonPath,
                markdownPath,
                jsonPath is null || markdownPath is null
                    ? $"luotsi quickstart {BuildCurrentOptionFlags(result)} --write-json --write-markdown".Replace("  ", " ", StringComparison.Ordinal).Trim()
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

        await artifacts.RefreshIndexAsync().ConfigureAwait(false);
        return resultWithHandoff;
    }

    public static QuickstartResult Build(CliOptions options)
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
            Handoff: null);
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
        builder.AppendLine("## Agent prompt");
        builder.AppendLine();
        builder.AppendLine(result.AgentPrompt);
        builder.AppendLine();
        return builder.ToString();
    }

    private static async Task WriteTextArtifactAsync(ArtifactSession artifacts, string name, string text)
    {
        await using var stream = artifacts.OpenArtifactWrite(name);
        var bytes = Utf8NoBom.GetBytes(text);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
    }

    private static string BuildCurrentOptionFlags(QuickstartResult result)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.Inputs.Device))
        {
            builder.Append(" --device ").Append(Quote(result.Inputs.Device));
        }

        if (!string.IsNullOrWhiteSpace(result.Inputs.Package))
        {
            builder.Append(" --package ").Append(Quote(result.Inputs.Package));
        }

        if (!string.IsNullOrWhiteSpace(result.Inputs.Artifacts))
        {
            builder.Append(" --artifacts ").Append(Quote(result.Inputs.Artifacts));
        }

        if (!string.IsNullOrWhiteSpace(result.Inputs.ScenarioPath))
        {
            builder.Append(" --path ").Append(Quote(result.Inputs.ScenarioPath));
        }

        return builder.ToString();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
    string FirstCommand,
    string AgentPrompt,
    IReadOnlyList<string> Differentiators,
    IReadOnlyList<string> SimilarToolCategories,
    QuickstartHandoffResult? Handoff);

internal sealed record QuickstartHandoffResult(
    string ArtifactRoot,
    string? JsonPath,
    string? MarkdownPath,
    string? RecommendedCommand);

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
