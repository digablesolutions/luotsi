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
            : CreatePrimaryFailure(primaryFailureSession);
        var artifactCounts = CountArtifacts(files);
        var artifactManifest = ReplayCapsuleArtifactManifestBuilder.Build(files).ToArray();
        var failureTimeline = BuildFailureTimeline(failureSessions).ToArray();
        var scenarioDraft = InspectScenarioDraftReadiness(artifacts.Root, files);
        var commandHints = BuildCommandHints(artifacts.Root, primaryFailure, scenarioDraft.Available).ToArray();
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
            readmePath,
            jsonPath,
            primaryFailure,
            artifactCounts,
            artifactManifest,
            failureTimeline,
            commandHints);

        if (jsonPath is not null)
        {
            await artifacts.WriteJsonAsync(CapsuleSummaryFileName, result).ConfigureAwait(false);
        }

        if (readmePath is not null)
        {
            await artifacts.WriteTextAsync(CapsuleReadmeFileName, BuildReadme(artifacts.Root, summaries.Count, failureSessions.Length, scenarioDraft, primaryFailure, artifactCounts, artifactManifest, failureTimeline, commandHints)).ConfigureAwait(false);
        }

        return result;
    }

    private static ReplayCapsulePrimaryFailureResult CreatePrimaryFailure(SessionReplaySummary summary)
    {
        var failedScenario = summary.FailureCapsule?.Scenarios.FirstOrDefault();
        var failedStep = failedScenario?.FailedStep;
        var message = failedScenario?.Error?.Message ??
            summary.TimelineHighlights.FirstOrDefault(static entry => entry.IsFailureRelevant)?.Detail;
        return new ReplayCapsulePrimaryFailureResult(
            failedScenario?.Scenario,
            failedStep?.Name,
            failedStep?.Action,
            message,
            summary.FailureCapsulePath,
            summary.TimelinePath);
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

    private static IEnumerable<ReplayCapsuleTimelineHighlightResult> BuildFailureTimeline(IEnumerable<SessionReplaySummary> failureSessions)
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
                    highlight.StepIndex);
            }
        }
    }

    private IEnumerable<ReplayCapsuleCommandHint> BuildCommandHints(
        string artifactRoot,
        ReplayCapsulePrimaryFailureResult? primaryFailure,
        bool scenarioDraftAvailable)
    {
        yield return new ReplayCapsuleCommandHint($"luotsi replay open --artifacts {Quote(artifactRoot)}", "Open the local artifact browser.");
        yield return new ReplayCapsuleCommandHint($"luotsi replay summarize --artifacts {Quote(artifactRoot)}", "Read session summaries and failure capsule links.");
        yield return new ReplayCapsuleCommandHint(
            $"luotsi replay timeline --artifacts {Quote(artifactRoot)} --failures --context 3 --write-json --write-markdown",
            "Write the failure-focused timeline with nearby context for offline triage.");
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
                command is "tap_text" or "wait_visible" or "type_text" or "keyevent" or "screenshot" or "take_screenshot" or "telemetry_tail" or "telemetry_watch")
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
        ReplayCapsulePrimaryFailureResult? primaryFailure,
        ReplayCapsuleArtifactCounts artifactCounts,
        IReadOnlyList<ReplayCapsuleArtifactManifestEntry> artifactManifest,
        IReadOnlyList<ReplayCapsuleTimelineHighlightResult> failureTimeline,
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
            builder.AppendLine("| Time | Type | Scenario | Step | Detail |");
            builder.AppendLine("|---|---|---|---|---|");
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

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;

    private sealed record ReplayCapsuleScenarioDraftReadiness(bool Available, string Reason);
}
