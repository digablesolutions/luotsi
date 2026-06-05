using System.Linq;
using System.Text;
using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal sealed class ReplayCapsuleService(IFileSystem fileSystem)
{
    private const string CapsuleReadmeFileName = "replay-capsule.md";
    private const string CapsuleSummaryFileName = "replay-capsule-summary.json";
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<ReplayCapsuleResult> DescribeAsync(CliOptions options, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var files = _fileSystem.GetFiles(artifacts.Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(artifacts.Root, path).Replace('\\', '/'))
            .ToArray();
        var summaries = new SessionReplaySummaryReader(artifacts.Root, _fileSystem).ReadSummaries(files);
        if (summaries.Count == 0)
        {
            throw new UsageException($"No session replay metadata was found under artifact root '{artifacts.Root}'.");
        }

        var failureSessions = summaries
            .Where(static summary => summary.HasFailureSignals)
            .ToArray();
        var primaryFailureSession = failureSessions.FirstOrDefault();
        var primaryFailure = primaryFailureSession is null
            ? null
            : CreatePrimaryFailure(artifacts.Root, primaryFailureSession);
        var artifactCounts = CountArtifacts(files);
        var artifactManifest = ReplayCapsuleArtifactManifestBuilder.Build(files).ToArray();
        var failureTimeline = BuildFailureTimeline(artifacts.Root, failureSessions).ToArray();
        var scenarioDraft = InspectScenarioDraftReadiness(artifacts.Root, files);
        var scenarioDraftArtifacts = FindScenarioDraftArtifacts(files);
        var scenarioDraftSummary = ReadScenarioDraftSummary(artifacts.Root, scenarioDraftArtifacts.SummaryPath);
        var commandHints = BuildCommandHints(artifacts.Root, primaryFailure, scenarioDraft.Available, scenarioDraftArtifacts).ToArray();
        var nextSteps = BuildRecommendedNextSteps(artifacts.Root, primaryFailure, scenarioDraft.Available, scenarioDraftSummary).ToArray();
        var readmePath = options.HasFlag("write-readme")
            ? Path.Join(artifacts.Root, CapsuleReadmeFileName)
            : null;
        var jsonPath = options.HasFlag("write-json")
            ? Path.Join(artifacts.Root, CapsuleSummaryFileName)
            : null;
        var result = new ReplayCapsuleResult(
            ResultSchemas.ReplayCapsule,
            artifacts.Root,
            summaries.Count,
            failureSessions.Length,
            summaries.Any(static summary => summary.FailureCapsule is not null),
            scenarioDraft.Available,
            scenarioDraft.Reason,
            scenarioDraftArtifacts,
            scenarioDraftSummary,
            readmePath,
            jsonPath,
            primaryFailure,
            artifactCounts,
            artifactManifest,
            failureTimeline,
            nextSteps,
            commandHints);

        if (jsonPath is not null)
        {
            await artifacts.WriteJsonAsync(CapsuleSummaryFileName, result).ConfigureAwait(false);
        }

        if (readmePath is not null)
        {
            await artifacts.WriteTextAsync(CapsuleReadmeFileName, BuildReadme(artifacts.Root, summaries.Count, failureSessions.Length, scenarioDraft, scenarioDraftArtifacts, scenarioDraftSummary, primaryFailure, artifactCounts, artifactManifest, failureTimeline, nextSteps, commandHints)).ConfigureAwait(false);
        }

        return result;
    }

    private static ReplayCapsulePrimaryFailureResult CreatePrimaryFailure(string artifactRoot, SessionReplaySummary summary)
    {
        var failedScenario = summary.FailureCapsule?.Scenarios.FirstOrDefault();
        var failedStep = failedScenario?.FailedStep;
        var failureHighlight = summary.TimelineHighlights.FirstOrDefault(static entry => entry.IsFailureRelevant);
        var message = failedScenario?.Error?.Message ??
            failureHighlight?.Detail;
        return new ReplayCapsulePrimaryFailureResult(
            failedScenario?.Scenario,
            failedStep?.Name,
            failedStep?.Action,
            message,
            summary.FailureCapsulePath,
            summary.TimelinePath,
            failureHighlight is null ? null : BuildTimelineSourceCommand(artifactRoot, summary.TimelinePath, failureHighlight.Sequence));
    }

    private static ReplayCapsuleArtifactCounts CountArtifacts(IReadOnlyList<string> files) =>
        new(
            Count(files, IsScreenshot),
            Count(files, IsVideo),
            Count(files, IsLog),
            Count(files, IsHierarchy),
            Count(files, IsScreenState),
            Count(files, IsReport),
            Count(files, static path => Path.GetFileName(path).Equals(SessionReplayArtifacts.TimelineFileName, StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<ReplayCapsuleTimelineHighlightResult> BuildFailureTimeline(
        string artifactRoot,
        IEnumerable<SessionReplaySummary> failureSessions)
    {
        foreach (var summary in failureSessions)
        {
            foreach (var highlight in summary.TimelineHighlights.Where(static highlight => highlight.IsFailureRelevant))
            {
                yield return new ReplayCapsuleTimelineHighlightResult(
                    summary.MetadataPath,
                    summary.TimelinePath,
                    highlight.Sequence,
                    highlight.Timestamp,
                    highlight.Type,
                    highlight.Detail,
                    highlight.IsFailureRelevant,
                    highlight.ScenarioId,
                    highlight.Scenario,
                    highlight.StepIndex,
                    BuildTimelineSourceCommand(artifactRoot, summary.TimelinePath, highlight.Sequence));
            }
        }
    }

    private static IEnumerable<ReplayCapsuleCommandHint> BuildCommandHints(
        string artifactRoot,
        ReplayCapsulePrimaryFailureResult? primaryFailure,
        bool scenarioDraftAvailable,
        ReplayCapsuleScenarioDraftArtifacts scenarioDraftArtifacts)
    {
        yield return new ReplayCapsuleCommandHint($"luotsi replay open --artifacts {Quote(artifactRoot)}", "Open the replay front door and local artifact browser.");
        yield return new ReplayCapsuleCommandHint($"luotsi replay summarize --artifacts {Quote(artifactRoot)}", "Read session summaries and failure capsule links.");
        yield return new ReplayCapsuleCommandHint(
            $"luotsi replay timeline --artifacts {Quote(artifactRoot)} --failures --context 3 --write-json --write-markdown",
            "Write the failure-focused timeline with nearby context for offline triage.");
        yield return new ReplayCapsuleCommandHint(
            $"luotsi replay scrub --artifacts {Quote(artifactRoot)} --failures --context 3 --write-json --write-markdown",
            "Write a local previous/focused/next event scrub view for the primary failure window.");
        yield return new ReplayCapsuleCommandHint(
            $"luotsi replay graph --artifacts {Quote(artifactRoot)} --write-json --write-markdown",
            "Write the semantic debug graph for agents and local inspection.");

        if (!string.IsNullOrWhiteSpace(primaryFailure?.Message))
        {
            yield return new ReplayCapsuleCommandHint(
                $"luotsi replay search --artifacts {Quote(artifactRoot)} --contains {Quote(primaryFailure.Message)}",
                "Find the primary failure message across timeline, reports, and logs.");
        }

        if (scenarioDraftAvailable)
        {
            yield return new ReplayCapsuleCommandHint(
                $"luotsi replay scenario-draft --artifacts {Quote(artifactRoot)} --output draft-scenario.json --write-json --write-markdown",
                "Create a starter scenario from captured inspect, screen-delta, view, or telemetry history.");
        }

        if (!string.IsNullOrWhiteSpace(scenarioDraftArtifacts.MarkdownPath))
        {
            yield return new ReplayCapsuleCommandHint(
                $"luotsi replay search --artifacts {Quote(artifactRoot)} --contains \"Review Checklist\"",
                "Find the generated scenario draft review checklist.");
        }
    }

    private static IEnumerable<ReplayCapsuleNextStep> BuildRecommendedNextSteps(
        string artifactRoot,
        ReplayCapsulePrimaryFailureResult? primaryFailure,
        bool scenarioDraftAvailable,
        ReplayCapsuleScenarioDraftSummary? scenarioDraftSummary)
    {
        var emittedKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emittedCommands = new HashSet<string>(StringComparer.Ordinal);

        var runHandoffNextSteps = BuildRunHandoffNextSteps(scenarioDraftSummary?.RunHandoff)
            .Where(step => TryMarkRecommendedStep(step, emittedKinds, emittedCommands));
        foreach (var step in runHandoffNextSteps)
        {
            yield return step;
        }

        var scenarioDraftNextSteps = (scenarioDraftSummary?.NextActions ?? [])
            .Select(action => new ReplayCapsuleNextStep(
                action.Kind,
                action.Title,
                action.Reason,
                action.Command))
            .Where(step => TryMarkRecommendedStep(step, emittedKinds, emittedCommands));
        foreach (var step in scenarioDraftNextSteps)
        {
            yield return step;
        }

        if (primaryFailure is not null)
        {
            var scrub = new ReplayCapsuleNextStep(
                "scrub_failure",
                "Scrub the failure window",
                "Start with the focused previous/current/next timeline view before opening broad artifacts.",
                $"luotsi replay scrub --artifacts {Quote(artifactRoot)} --failures --context 3 --write-markdown");
            if (TryMarkRecommendedStep(scrub, emittedKinds, emittedCommands))
            {
                yield return scrub;
            }

            var graph = new ReplayCapsuleNextStep(
                "graph_failure",
                "Open semantic failure context",
                "Use the graph when an agent or reviewer needs evidence, facts, causal chains, and hypotheses.",
                $"luotsi replay graph --artifacts {Quote(artifactRoot)} --failed --write-json --write-markdown");
            if (TryMarkRecommendedStep(graph, emittedKinds, emittedCommands))
            {
                yield return graph;
            }
        }

        if (!string.IsNullOrWhiteSpace(primaryFailure?.Message))
        {
            var search = new ReplayCapsuleNextStep(
                "search_failure_text",
                "Search the bundle for the failure text",
                "Find matching logcat, timeline, hierarchy, report, or markdown references.",
                $"luotsi replay search --artifacts {Quote(artifactRoot)} --contains {Quote(primaryFailure.Message)}");
            if (TryMarkRecommendedStep(search, emittedKinds, emittedCommands))
            {
                yield return search;
            }

            var cluster = new ReplayCapsuleNextStep(
                "cluster_similar_failures",
                "Find similar failures across replay bundles",
                "Use this when the current artifact root sits under a larger CI or local artifacts directory.",
                $"luotsi replay cluster --artifacts {Quote(ResolveClusterRoot(artifactRoot))} --min-count 2 --contains {Quote(primaryFailure.Message)} --write-markdown");
            if (TryMarkRecommendedStep(cluster, emittedKinds, emittedCommands))
            {
                yield return cluster;
            }
        }

        var open = new ReplayCapsuleNextStep(
            "open_artifacts",
            "Open the artifact browser",
            "Use this when screenshots, videos, logs, and generated replay artifacts need human inspection.",
            $"luotsi replay open --artifacts {Quote(artifactRoot)}");
        if (TryMarkRecommendedStep(open, emittedKinds, emittedCommands))
        {
            yield return open;
        }

        if (scenarioDraftAvailable)
        {
            var draft = new ReplayCapsuleNextStep(
                "draft_scenario",
                "Draft a scenario from replay",
                "Use captured inspect/view/telemetry events to create a reviewable starter scenario.",
                $"luotsi replay scenario-draft --artifacts {Quote(artifactRoot)} --output draft-scenario.json --write-json --write-markdown");
            if (TryMarkRecommendedStep(draft, emittedKinds, emittedCommands))
            {
                yield return draft;
            }
        }
    }

    private static IEnumerable<ReplayCapsuleNextStep> BuildRunHandoffNextSteps(ReplayScenarioDraftRunHandoff? runHandoff)
    {
        if (runHandoff is null ||
            !string.Equals(runHandoff.Status, "ready", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(runHandoff.DryRunCommand))
        {
            yield return new ReplayCapsuleNextStep(
                "dry_run_scenario",
                "Plan generated scenario",
                "Validated draft is ready; confirm the scenario selection and execution plan before device work.",
                runHandoff.DryRunCommand);
        }

        if (!string.IsNullOrWhiteSpace(runHandoff.PreflightCommand))
        {
            yield return new ReplayCapsuleNextStep(
                "preflight_device",
                "Preflight target device",
                "Verify adb, device, and target app readiness before executing the generated scenario.",
                runHandoff.PreflightCommand);
        }

        if (!string.IsNullOrWhiteSpace(runHandoff.ClaimedRunCommand))
        {
            yield return new ReplayCapsuleNextStep(
                "claimed_run_scenario",
                "Claim and run generated scenario",
                "Claim the selected device for this run before executing the validated draft in a shared lab.",
                runHandoff.ClaimedRunCommand);
        }

        if (!string.IsNullOrWhiteSpace(runHandoff.RunCommand))
        {
            yield return new ReplayCapsuleNextStep(
                "run_scenario",
                "Run generated scenario",
                "Execute the validated draft on a selected device after review and preflight.",
                runHandoff.RunCommand);
        }
    }

    private static bool TryMarkRecommendedStep(
        ReplayCapsuleNextStep step,
        ISet<string> emittedKinds,
        ISet<string> emittedCommands)
    {
        if (emittedKinds.Contains(step.Kind) || emittedCommands.Contains(step.Command))
        {
            return false;
        }

        emittedKinds.Add(step.Kind);
        emittedCommands.Add(step.Command);
        return true;
    }

    private static ReplayCapsuleScenarioDraftArtifacts FindScenarioDraftArtifacts(IReadOnlyList<string> files)
    {
        var summary = files.FirstOrDefault(static file => Path.GetFileName(file).Equals("scenario-draft-summary.json", StringComparison.OrdinalIgnoreCase));
        var markdown = files.FirstOrDefault(static file => Path.GetFileName(file).Equals("scenario-draft.md", StringComparison.OrdinalIgnoreCase));
        var scenario = files.FirstOrDefault(IsDraftScenarioFile);
        return new ReplayCapsuleScenarioDraftArtifacts(summary, markdown, scenario);
    }

    private static bool IsDraftScenarioFile(string file)
    {
        var fileName = Path.GetFileName(file);
        return fileName.Contains("draft", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            !fileName.Equals("scenario-draft-summary.json", StringComparison.OrdinalIgnoreCase);
    }

    private ReplayCapsuleScenarioDraftSummary? ReadScenarioDraftSummary(string artifactRoot, string? summaryPath)
    {
        if (string.IsNullOrWhiteSpace(summaryPath))
        {
            return null;
        }

        var fullPath = Path.Join(artifactRoot, summaryPath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            using var stream = _fileSystem.OpenRead(fullPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            return new ReplayCapsuleScenarioDraftSummary(
                TryGetString(root, "confidence", out var confidence) ? confidence : null,
                CountScenarioSteps(root),
                CountArray(root, "warnings"),
                CountArray(root, "reviewItems"),
                CountArray(root, "nextActions"),
                CountArray(root, "normalizations"),
                ReadPackageProvenance(root),
                ReadDeviceProvenance(root),
                ReadValidation(root),
                ReadRunHandoff(root),
                ReadStringArray(root, "warnings", 5),
                ReadNextActions(root, 5),
                ReadReviewItems(root, 5));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ReplayCapsuleScenarioDraftReadiness InspectScenarioDraftReadiness(string artifactRoot, IReadOnlyList<string> files)
    {
        var timelineCount = 0;
        foreach (var file in files.Where(static file => Path.GetFileName(file).Equals(SessionReplayArtifacts.TimelineFileName, StringComparison.OrdinalIgnoreCase)))
        {
            timelineCount++;
            var path = Path.Join(artifactRoot, file.Replace('/', Path.DirectorySeparatorChar));
            using var stream = _fileSystem.OpenRead(path);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096);
            while (reader.ReadLine() is { } line)
            {
                var source = TryReadScenarioDraftSource(line);
                if (source is not null)
                {
                    return new ReplayCapsuleScenarioDraftReadiness(true, $"Found {source} source in {file}.");
                }
            }
        }

        return timelineCount == 0
            ? new ReplayCapsuleScenarioDraftReadiness(false, "No session-timeline.jsonl files were found for scenario draft generation.")
            : new ReplayCapsuleScenarioDraftReadiness(false, "No inspect/view action, screen-delta, or telemetry events were found for scenario draft generation.");
    }

    private static string? TryReadScenarioDraftSource(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryGetString(root, "type", out var type))
            {
                return null;
            }

            if (type is "screen_delta" or "view_screenshot_captured" or "view_key_command_sent")
            {
                return type;
            }

            if (string.Equals(type, "command_result", StringComparison.Ordinal) &&
                TryGetString(root, "command", out var command) &&
                command is "tap_text" or "tap_element" or "tap_selector" or
                    "wait_visible" or "wait_element" or "wait_selector" or
                    "type_text" or "keyevent" or "screenshot" or "take_screenshot" or
                    "telemetry_tail" or "telemetry_watch")
            {
                return "command_result:" + command;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int Count(IEnumerable<string> files, Func<string, bool> predicate) =>
        files.Count(predicate);

    private static string BuildReadme(
        string artifactRoot,
        int sessionCount,
        int failureCount,
        ReplayCapsuleScenarioDraftReadiness scenarioDraft,
        ReplayCapsuleScenarioDraftArtifacts scenarioDraftArtifacts,
        ReplayCapsuleScenarioDraftSummary? scenarioDraftSummary,
        ReplayCapsulePrimaryFailureResult? primaryFailure,
        ReplayCapsuleArtifactCounts artifactCounts,
        IReadOnlyList<ReplayCapsuleArtifactManifestEntry> artifactManifest,
        IReadOnlyList<ReplayCapsuleTimelineHighlightResult> failureTimeline,
        IReadOnlyList<ReplayCapsuleNextStep> nextSteps,
        IReadOnlyList<ReplayCapsuleCommandHint> commandHints)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Replay Capsule");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{artifactRoot}`");
        builder.AppendLine($"Sessions: `{sessionCount}`");
        builder.AppendLine($"Failures: `{failureCount}`");
        builder.AppendLine($"Scenario draft available: `{scenarioDraft.Available}`");
        builder.AppendLine($"Scenario draft reason: `{scenarioDraft.Reason}`");
        AppendField(builder, "Scenario draft summary", scenarioDraftArtifacts.SummaryPath);
        AppendField(builder, "Scenario draft review", scenarioDraftArtifacts.MarkdownPath);
        AppendField(builder, "Scenario draft file", scenarioDraftArtifacts.ScenarioPath);
        if (scenarioDraftSummary is not null)
        {
            AppendField(builder, "Scenario draft confidence", scenarioDraftSummary.Confidence);
            AppendField(builder, "Scenario draft package", scenarioDraftSummary.PackageProvenance?.Package);
            AppendField(builder, "Scenario draft device", scenarioDraftSummary.DeviceProvenance?.Serial);
            AppendField(builder, "Scenario draft validation", scenarioDraftSummary.Validation?.Status);
            AppendField(builder, "Scenario draft run handoff", scenarioDraftSummary.RunHandoff?.Status);
            AppendField(builder, "Scenario draft dry run", scenarioDraftSummary.RunHandoff?.DryRunCommand);
            AppendField(builder, "Scenario draft preflight", scenarioDraftSummary.RunHandoff?.PreflightCommand);
            AppendField(builder, "Scenario draft claimed run", scenarioDraftSummary.RunHandoff?.ClaimedRunCommand);
            AppendField(builder, "Scenario draft run", scenarioDraftSummary.RunHandoff?.RunCommand);
            builder.AppendLine($"- Scenario draft steps: `{scenarioDraftSummary.StepCount}`");
            builder.AppendLine($"- Scenario draft warnings: `{scenarioDraftSummary.WarningCount}`");
            builder.AppendLine($"- Scenario draft review items: `{scenarioDraftSummary.ReviewItemCount}`");
            builder.AppendLine($"- Scenario draft next actions: `{scenarioDraftSummary.NextActionCount}`");
            builder.AppendLine($"- Scenario draft normalizations: `{scenarioDraftSummary.NormalizationCount}`");
            if (scenarioDraftSummary.Warnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Scenario Draft Warning Preview");
                builder.AppendLine();
                foreach (var warning in scenarioDraftSummary.Warnings)
                {
                    builder.AppendLine("- " + EscapeMarkdown(warning));
                }
            }

            if (scenarioDraftSummary.NextActions.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Scenario Draft Next Actions");
                builder.AppendLine();
                foreach (var action in scenarioDraftSummary.NextActions)
                {
                    builder.AppendLine($"- **{EscapeMarkdown(action.Title)}** (`{EscapeMarkdown(action.Kind)}`)");
                    builder.AppendLine($"  {EscapeMarkdown(action.Reason)}");
                    builder.AppendLine($"  `{EscapeMarkdown(action.Command)}`");
                }
            }

            if (scenarioDraftSummary.ReviewItems.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Scenario Draft Review Preview");
                builder.AppendLine();
                builder.AppendLine("| Severity | Category | Step | Message | Command |");
                builder.AppendLine("|---|---|---|---|---|");
                foreach (var item in scenarioDraftSummary.ReviewItems)
                {
                    builder.Append("| ");
                    builder.Append(EscapeMarkdown(item.Severity));
                    builder.Append(" | ");
                    builder.Append(EscapeMarkdown(item.Category));
                    builder.Append(" | ");
                    builder.Append(EscapeMarkdown(item.StepIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
                    builder.Append(" | ");
                    builder.Append(EscapeMarkdown(item.Message));
                    builder.Append(" | ");
                    builder.Append(EscapeMarkdown(item.Command ?? string.Empty));
                    builder.AppendLine(" |");
                }
            }
        }

        AppendStartHere(builder, primaryFailure, nextSteps);

        builder.AppendLine();
        builder.AppendLine("## Primary Failure");
        builder.AppendLine();
        if (primaryFailure is null)
        {
            builder.AppendLine("No failure signal was found in the replay summaries.");
        }
        else
        {
            AppendField(builder, "Scenario", primaryFailure.Scenario);
            AppendField(builder, "Step", primaryFailure.Step);
            AppendField(builder, "Action", primaryFailure.Action);
            AppendField(builder, "Message", primaryFailure.Message);
            AppendField(builder, "Failure capsule", primaryFailure.FailureCapsulePath);
            AppendField(builder, "Timeline", primaryFailure.TimelinePath);
            AppendField(builder, "Reopen", primaryFailure.SourceCommand);
        }

        builder.AppendLine();
        builder.AppendLine("## Recommended Next Steps");
        builder.AppendLine();
        foreach (var step in nextSteps)
        {
            builder.AppendLine($"- **{EscapeMarkdown(step.Title)}** (`{EscapeMarkdown(step.Kind)}`)");
            builder.AppendLine($"  {EscapeMarkdown(step.Reason)}");
            builder.AppendLine($"  `{EscapeMarkdown(step.Command)}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Failure Timeline");
        builder.AppendLine();
        if (failureTimeline.Count == 0)
        {
            builder.AppendLine("No failure-relevant timeline events were found.");
        }
        else
        {
            builder.AppendLine("| Time | Type | Scenario | Step | Detail | Reopen |");
            builder.AppendLine("|---|---|---|---|---|---|");
            foreach (var entry in failureTimeline)
            {
                builder.Append("| ");
                builder.Append(EscapeMarkdown(entry.Timestamp?.ToString("O") ?? string.Empty));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(entry.Type));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(entry.Scenario ?? string.Empty));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(entry.StepIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(entry.Detail));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(entry.SourceCommand));
                builder.AppendLine(" |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Artifact Counts");
        builder.AppendLine();
        builder.AppendLine($"- Screenshots: `{artifactCounts.Screenshots}`");
        builder.AppendLine($"- Videos: `{artifactCounts.Videos}`");
        builder.AppendLine($"- Logs: `{artifactCounts.Logs}`");
        builder.AppendLine($"- Hierarchies: `{artifactCounts.Hierarchies}`");
        builder.AppendLine($"- Screen states: `{artifactCounts.ScreenStates}`");
        builder.AppendLine($"- Reports: `{artifactCounts.Reports}`");
        builder.AppendLine($"- Timelines: `{artifactCounts.Timelines}`");
        builder.AppendLine();
        builder.AppendLine("## Artifact Manifest");
        builder.AppendLine();
        builder.AppendLine("| Kind | Role | Session | Path |");
        builder.AppendLine("|---|---|---|---|");
        foreach (var artifact in artifactManifest)
        {
            builder.Append("| ");
            builder.Append(EscapeMarkdown(artifact.Kind));
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(artifact.Role));
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(artifact.Session ?? string.Empty));
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(artifact.Path));
            builder.AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Next Commands");
        builder.AppendLine();
        foreach (var hint in commandHints)
        {
            builder.AppendLine($"- `{hint.Command}`");
            builder.AppendLine($"  {hint.Purpose}");
        }

        return builder.ToString();
    }

    private static void AppendStartHere(
        StringBuilder builder,
        ReplayCapsulePrimaryFailureResult? primaryFailure,
        IReadOnlyList<ReplayCapsuleNextStep> nextSteps)
    {
        builder.AppendLine();
        builder.AppendLine("## Start Here");
        builder.AppendLine();
        if (primaryFailure is not null)
        {
            var summary = new[]
                {
                    primaryFailure.Scenario,
                    primaryFailure.Step,
                    primaryFailure.Action,
                    primaryFailure.Message
                }
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Take(4)
                .ToArray();
            if (summary.Length > 0)
            {
                builder.AppendLine("- Primary failure: " + EscapeMarkdown(string.Join(" / ", summary)));
            }
            else
            {
                builder.AppendLine("- Primary failure: details unavailable");
            }
        }
        else
        {
            builder.AppendLine("- Primary failure: none detected");
        }

        var firstStep = nextSteps.FirstOrDefault();
        if (firstStep is not null)
        {
            builder.AppendLine($"- Best next step: {EscapeMarkdown(firstStep.Title)} (`{EscapeMarkdown(firstStep.Kind)}`)");
            builder.AppendLine($"- Run: `{EscapeMarkdown(firstStep.Command)}`");
        }
    }

    private static void AppendField(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"- {label}: `{value}`");
        }
    }

    private static bool IsScreenshot(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        return fileName.Contains("screenshot", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideo(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".h264", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLog(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        return fileName.Contains("logcat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".log", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHierarchy(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Contains("hierarchy", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScreenState(string path) =>
        Path.GetFileName(path).Contains("screen-state", StringComparison.OrdinalIgnoreCase);

    private static bool IsReport(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Contains("report", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("junit.xml", StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeMarkdown(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static int CountScenarioSteps(JsonElement root)
    {
        if (!root.TryGetProperty("scenario", out var scenario) ||
            scenario.ValueKind != JsonValueKind.Object ||
            !scenario.TryGetProperty("steps", out var steps) ||
            steps.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return steps.GetArrayLength();
    }

    private static int CountArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.GetArrayLength()
            : 0;

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string name, int limit)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString() ?? string.Empty)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Take(limit)
            .ToArray();
    }

    private static ReplayScenarioDraftValidation? ReadValidation(JsonElement root)
    {
        if (!root.TryGetProperty("validation", out var property) ||
            property.ValueKind != JsonValueKind.Object ||
            !TryGetString(property, "status", out var status) ||
            !TryGetString(property, "command", out var command))
        {
            return null;
        }

        return new ReplayScenarioDraftValidation(
            status,
            command,
            TryGetString(property, "message", out var message) ? message : null,
            TryGetString(property, "error", out var error) ? error : null);
    }

    private static ReplayScenarioDraftPackageProvenance? ReadPackageProvenance(JsonElement root)
    {
        if (!root.TryGetProperty("packageProvenance", out var property) ||
            property.ValueKind != JsonValueKind.Object ||
            !TryGetString(property, "package", out var package) ||
            !TryGetString(property, "source", out var source) ||
            !TryGetString(property, "eventType", out var eventType))
        {
            return null;
        }

        return new ReplayScenarioDraftPackageProvenance(
            package,
            source,
            eventType,
            TryGetString(property, "command", out var command) ? command : null,
            TryGetString(property, "sourcePath", out var sourcePath) ? sourcePath : null,
            TryGetInt(property, "sequence", out var sequence) ? sequence : null,
            TryGetDateTimeOffset(property, "timestamp", out var timestamp) ? timestamp : null,
            TryGetString(property, "sourceCommand", out var sourceCommand) ? sourceCommand : null);
    }

    private static ReplayScenarioDraftDeviceProvenance? ReadDeviceProvenance(JsonElement root)
    {
        if (!root.TryGetProperty("deviceProvenance", out var property) ||
            property.ValueKind != JsonValueKind.Object ||
            !TryGetString(property, "serial", out var serial) ||
            !TryGetString(property, "source", out var source) ||
            !TryGetString(property, "sessionKind", out var sessionKind))
        {
            return null;
        }

        return new ReplayScenarioDraftDeviceProvenance(
            serial,
            source,
            sessionKind,
            TryGetString(property, "sessionId", out var sessionId) ? sessionId : null,
            TryGetString(property, "sourcePath", out var sourcePath) ? sourcePath : null,
            TryGetDateTimeOffset(property, "startedAt", out var startedAt) ? startedAt : null);
    }

    private static ReplayScenarioDraftRunHandoff? ReadRunHandoff(JsonElement root)
    {
        if (!root.TryGetProperty("runHandoff", out var property) ||
            property.ValueKind != JsonValueKind.Object ||
            !TryGetString(property, "status", out var status) ||
            !TryGetString(property, "reason", out var reason))
        {
            return null;
        }

        return new ReplayScenarioDraftRunHandoff(
            status,
            reason,
            TryGetString(property, "preflightCommand", out var preflightCommand) ? preflightCommand : null,
            TryGetString(property, "dryRunCommand", out var dryRunCommand) ? dryRunCommand : null,
            TryGetString(property, "runCommand", out var runCommand) ? runCommand : null,
            TryGetString(property, "claimedRunCommand", out var claimedRunCommand) ? claimedRunCommand : null);
    }

    private static IReadOnlyList<ReplayScenarioDraftNextAction> ReadNextActions(JsonElement root, int limit)
    {
        if (!root.TryGetProperty("nextActions", out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<ReplayScenarioDraftNextAction>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryGetString(item, "command", out var command))
            {
                continue;
            }

            items.Add(new ReplayScenarioDraftNextAction(
                TryGetString(item, "kind", out var kind) ? kind : "next_action",
                TryGetString(item, "title", out var title) ? title : "Next action",
                TryGetString(item, "reason", out var reason) ? reason : string.Empty,
                command));
            if (items.Count == limit)
            {
                break;
            }
        }

        return items;
    }

    private static IReadOnlyList<ReplayScenarioDraftReviewItem> ReadReviewItems(JsonElement root, int limit)
    {
        if (!root.TryGetProperty("reviewItems", out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<ReplayScenarioDraftReviewItem>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new ReplayScenarioDraftReviewItem(
                TryGetString(item, "severity", out var severity) ? severity : "info",
                TryGetString(item, "category", out var category) ? category : "general",
                TryGetInt(item, "stepIndex", out var stepIndex) ? stepIndex : null,
                TryGetString(item, "message", out var message) ? message : string.Empty,
                TryGetString(item, "command", out var command) ? command : null));
            if (items.Count == limit)
            {
                break;
            }
        }

        return items;
    }

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value);
    }

    private static bool TryGetDateTimeOffset(JsonElement root, string name, out DateTimeOffset value)
    {
        value = default;
        return root.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                property.GetString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out value);
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;

    private static string ResolveClusterRoot(string artifactRoot)
    {
        var parent = Path.GetDirectoryName(artifactRoot);
        return string.IsNullOrWhiteSpace(parent) ? artifactRoot : parent;
    }

    private static string BuildTimelineSourceCommand(string artifactRoot, string timelinePath, int sequence) =>
        $"luotsi replay timeline --artifacts {Quote(artifactRoot)} --source-path {Quote(timelinePath)} --sequence {sequence} --context 3";

    private sealed record ReplayCapsuleScenarioDraftReadiness(bool Available, string Reason);
}
