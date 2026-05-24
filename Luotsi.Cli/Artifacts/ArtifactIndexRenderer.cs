using System.Net;
using System.Text;
using System.Text.Json;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Artifacts;

internal sealed class ArtifactIndexRenderer(string root, IFileSystem fileSystem)
{
    private const int MaxJsonlSummaryBytes = 256 * 1024;
    private const int MaxJsonlSummaryLines = 500;
    private readonly string _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<string> BuildMarkdownIndexAsync(IReadOnlyList<string> files)
    {
        var replaySummaries = new SessionReplaySummaryReader(_root, _fileSystem).ReadSummaries(files);
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Artifacts");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{_root}`");
        builder.AppendLine();
        if (files.Count == 0)
        {
            builder.AppendLine("No artifacts have been written yet.");
            return builder.ToString();
        }

        AppendReplaySessionsMarkdown(builder, replaySummaries);
        AppendReplayWorkflowMarkdown(builder, replaySummaries);

        foreach (var group in files.GroupBy(GetArtifactCategory))
        {
            builder.AppendLine($"## {group.Key}");
            builder.AppendLine();
            foreach (var file in group)
            {
                builder.AppendLine($"- [{file}]({EscapeMarkdownLink(file)})");
                var summary = await TryBuildArtifactSummaryAsync(file).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    builder.AppendLine($"  - {summary}");
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    public async Task<string> BuildHtmlIndexAsync(IReadOnlyList<string> files)
    {
        var replaySummaries = new SessionReplaySummaryReader(_root, _fileSystem).ReadSummaries(files);
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\">");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("  <title>Luotsi Artifacts</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    :root { color-scheme: light dark; --bg: #0f172a; --panel: #111827; --text: #e5e7eb; --muted: #9ca3af; --line: #334155; --accent: #38bdf8; }");
        builder.AppendLine("    @media (prefers-color-scheme: light) { :root { --bg: #f8fafc; --panel: #ffffff; --text: #0f172a; --muted: #475569; --line: #cbd5e1; --accent: #0369a1; } }");
        builder.AppendLine("    body { margin: 0; font: 14px/1.45 system-ui, -apple-system, Segoe UI, sans-serif; background: var(--bg); color: var(--text); }");
        builder.AppendLine("    main { max-width: 1040px; margin: 0 auto; padding: 32px 20px 48px; }");
        builder.AppendLine("    header { margin-bottom: 24px; }");
        builder.AppendLine("    h1 { margin: 0 0 8px; font-size: 28px; letter-spacing: 0; }");
        builder.AppendLine("    .root { color: var(--muted); word-break: break-all; }");
        builder.AppendLine("    section { margin-top: 22px; border: 1px solid var(--line); background: var(--panel); border-radius: 8px; overflow: hidden; }");
        builder.AppendLine("    h2 { margin: 0; padding: 14px 16px; font-size: 16px; border-bottom: 1px solid var(--line); }");
        builder.AppendLine("    ul { list-style: none; margin: 0; padding: 0; }");
        builder.AppendLine("    li { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 16px; padding: 12px 16px; border-top: 1px solid var(--line); }");
        builder.AppendLine("    li:first-child { border-top: 0; }");
        builder.AppendLine("    a { color: var(--accent); text-decoration: none; overflow-wrap: anywhere; }");
        builder.AppendLine("    a:hover { text-decoration: underline; }");
        builder.AppendLine("    .timeline-label { margin-top: 8px; color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: .06em; }");
        builder.AppendLine("    .timeline { list-style: none; margin: 6px 0 0; padding: 0; }");
        builder.AppendLine("    .timeline li { display: block; padding: 4px 0 0; border-top: 0; }");
        builder.AppendLine("    .kind { color: var(--muted); font-size: 12px; text-transform: uppercase; letter-spacing: .06em; }");
        builder.AppendLine("    .empty { padding: 18px 16px; color: var(--muted); }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <main>");
        builder.AppendLine("    <header>");
        builder.AppendLine("      <h1>Luotsi Artifacts</h1>");
        builder.AppendLine($"      <div class=\"root\">{HtmlEncode(_root)}</div>");
        builder.AppendLine("    </header>");

        if (files.Count == 0)
        {
            builder.AppendLine("    <section><h2>Artifacts</h2><div class=\"empty\">No artifacts have been written yet.</div></section>");
        }
        else
        {
            AppendReplaySessionsHtml(builder, replaySummaries);
            AppendReplayWorkflowHtml(builder, replaySummaries);

            foreach (var group in files.GroupBy(GetArtifactCategory))
            {
                builder.AppendLine("    <section>");
                builder.AppendLine($"      <h2>{HtmlEncode(group.Key)}</h2>");
                builder.AppendLine("      <ul>");
                foreach (var file in group)
                {
                    builder.AppendLine("        <li>");
                    builder.AppendLine("          <div>");
                    builder.AppendLine($"            <a href=\"{HtmlAttributeEncode(EscapeHtmlLink(file))}\">{HtmlEncode(file)}</a>");
                    var summary = await TryBuildArtifactSummaryAsync(file).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        builder.AppendLine($"            <div class=\"root\">{HtmlEncode(summary)}</div>");
                    }

                    builder.AppendLine("          </div>");
                    builder.AppendLine($"          <span class=\"kind\">{HtmlEncode(GetArtifactKind(file))}</span>");
                    builder.AppendLine("        </li>");
                }

                builder.AppendLine("      </ul>");
                builder.AppendLine("    </section>");
            }
        }

        builder.AppendLine("  </main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    public static int GetArtifactSortGroup(string path) =>
        GetArtifactCategory(path) switch
        {
            "Screenshots" => 0,
            "Recordings" => 1,
            "Replay" => 2,
            "Reports" => 3,
            "Logs" => 4,
            "Screen State" => 5,
            "Hierarchy" => 6,
            _ => 7
        };

    private static string GetArtifactCategory(string path)
    {
        var extension = Path.GetExtension(path);
        var fileName = Path.GetFileName(path);
        if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return "Screenshots";
        }

        if (string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".webm", StringComparison.OrdinalIgnoreCase))
        {
            return "Recordings";
        }

        if (fileName.Contains("replay", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("scenario-draft", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, SessionReplayArtifacts.TimelineFileName, StringComparison.OrdinalIgnoreCase))
        {
            return "Replay";
        }

        if (fileName.Contains("session-replay", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("session-timeline", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, FailureCapsuleArtifactNames.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return "Reports";
        }

        if (fileName.Contains("report", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("junit", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".trx", StringComparison.OrdinalIgnoreCase))
        {
            return "Reports";
        }

        if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("logcat", StringComparison.OrdinalIgnoreCase))
        {
            return "Logs";
        }

        if (fileName.Contains("screen-state", StringComparison.OrdinalIgnoreCase))
        {
            return "Screen State";
        }

        if (fileName.Contains("hierarchy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return "Hierarchy";
        }

        return "Other";
    }

    private async Task<string?> TryBuildArtifactSummaryAsync(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            return await TryBuildJsonlSummaryAsync(path).ConfigureAwait(false);
        }

        return string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
            ? await TryBuildJsonReportSummaryAsync(path).ConfigureAwait(false)
            : null;
    }

    private async Task<string?> TryBuildJsonReportSummaryAsync(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(await _fileSystem.ReadAllTextAsync(Path.Join(_root, path)).ConfigureAwait(false));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var schemaName = schema.GetString();
            if (string.Equals(schemaName, ResultSchemas.ReplayCapsule, StringComparison.Ordinal))
            {
                return BuildReplayCapsuleSummary(root);
            }

            if (string.Equals(schemaName, ResultSchemas.ScenarioDraft, StringComparison.Ordinal))
            {
                return BuildScenarioDraftSummary(root);
            }

            if (string.Equals(schemaName, ResultSchemas.ReplayScrub, StringComparison.Ordinal))
            {
                return BuildReplayScrubSummary(root);
            }

            if (string.Equals(schemaName, "luotsi-scenario-run-report.v1", StringComparison.Ordinal))
            {
                return BuildScenarioRunReportSummary(root);
            }

            return string.Equals(schemaName, ResultSchemas.FailureCapsule, StringComparison.Ordinal)
                ? BuildFailureCapsuleSummary(root)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? BuildScenarioRunReportSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "status");
        AddJsonProperty(parts, root, "total");
        AddJsonProperty(parts, root, "passed");
        AddJsonProperty(parts, root, "failed");
        AddJsonProperty(parts, root, "skipped");
        AddJsonProperty(parts, root, "durationMs", "duration_ms");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildReplayCapsuleSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "sessionCount", "session_count");
        AddJsonProperty(parts, root, "failureCount", "failure_count");
        AddJsonProperty(parts, root, "scenarioDraftAvailable", "scenario_draft_available");
        AddJsonProperty(parts, root, "scenarioDraftReason", "scenario_draft_reason");
        AddArrayCount(parts, root, "artifactManifest", "artifact_manifest");
        AddArrayCount(parts, root, "failureTimeline", "failure_timeline");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildReplayScrubSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "eventCount", "event_count");
        AddJsonProperty(parts, root, "focusIndex", "focus_index");
        AddJsonProperty(parts, root, "markdownPath", "markdown_path");
        if (root.TryGetProperty("focusEvent", out var focusEvent) ||
            root.TryGetProperty("focus_event", out focusEvent))
        {
            AddJsonProperty(parts, focusEvent, "type", "focus_type");
            AddJsonProperty(parts, focusEvent, "detail", "focus_detail");
        }

        AddArrayCount(parts, root, "commands");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildScenarioDraftSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "confidence");
        AddArrayCount(parts, root, "sourceSummaries", "source_summaries");
        if (root.TryGetProperty("scenario", out var scenario) &&
            scenario.ValueKind == JsonValueKind.Object &&
            scenario.TryGetProperty("steps", out var steps) &&
            steps.ValueKind == JsonValueKind.Array)
        {
            parts.Add($"steps={steps.GetArrayLength()}");
        }

        AddArrayCount(parts, root, "warnings");
        AddArrayCount(parts, root, "reviewItems", "review_items");
        AddArrayCount(parts, root, "normalizations");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildFailureCapsuleSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "status");

        if (root.TryGetProperty("scenarios", out var scenarios) && scenarios.ValueKind == JsonValueKind.Array)
        {
            var scenarioItems = scenarios.EnumerateArray().ToArray();
            parts.Add($"scenarios={scenarioItems.Length}");

            var failedScenarioNames = scenarioItems
                .Select(static scenario => TryGetString(scenario, "scenario"))
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Take(2)
                .Cast<string>()
                .ToArray();
            if (failedScenarioNames.Length > 0)
            {
                parts.Add($"failed_scenarios={string.Join(", ", failedScenarioNames)}");
            }

            var failedSteps = scenarioItems
                .Select(static scenario => TryGetObjectString(scenario, "failedStep", "name"))
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Take(2)
                .Cast<string>()
                .ToArray();
            if (failedSteps.Length > 0)
            {
                parts.Add($"failed_steps={string.Join(", ", failedSteps)}");
            }
        }

        AddArrayCount(parts, root, "screenshots");
        AddArrayCount(parts, root, "logcat");
        AddArrayCount(parts, root, "hierarchies");
        AddArrayCount(parts, root, "screenStates", "screen_states");
        AddArrayCount(parts, root, "failureBundles", "failure_bundles");

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private async Task<string?> TryBuildJsonlSummaryAsync(string path)
    {
        try
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var terminalStatuses = new List<string>();
            var (sampledLines, truncated) = await ReadJsonlTailLinesAsync(path).ConfigureAwait(false);
            foreach (var (type, status) in sampledLines.Select(ParseJsonlEvent))
            {
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                counts[type] = counts.GetValueOrDefault(type) + 1;
                if (status is not null)
                {
                    terminalStatuses.Add(status);
                }
            }

            if (counts.Count == 0)
            {
                return null;
            }

            var parts = truncated
                ? new List<string> { $"events_sampled={counts.Values.Sum()}" }
                : new List<string> { $"events={counts.Values.Sum()}" };
            if (terminalStatuses.Count > 0)
            {
                parts.Add($"terminal={string.Join(",", terminalStatuses)}");
            }

            foreach (var (type, count) in counts.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase).Take(5))
            {
                parts.Add($"{type}={count}");
            }

            return string.Join(" | ", parts);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<(string[] Lines, bool Truncated)> ReadJsonlTailLinesAsync(string path)
    {
        using var stream = _fileSystem.OpenRead(Path.Join(_root, path));
        var truncatedByBytes = false;
        if (stream is {CanSeek: true, Length: > MaxJsonlSummaryBytes})
        {
            stream.Seek(-MaxJsonlSummaryBytes, SeekOrigin.End);
            truncatedByBytes = true;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (truncatedByBytes && lines.Length > 0)
        {
            lines = lines[1..];
        }

        if (lines.Length <= MaxJsonlSummaryLines)
        {
            return (lines, truncatedByBytes);
        }

        return (lines[^MaxJsonlSummaryLines..], true);
    }

    private static (string? Type, string? Status) ParseJsonlEvent(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        if (!root.TryGetProperty("type", out var typeProperty))
        {
            return (null, null);
        }

        var type = typeProperty.GetString();
        var status = string.Equals(type, "scenario_run_ended", StringComparison.OrdinalIgnoreCase) &&
            root.TryGetProperty("status", out var statusProperty)
                ? statusProperty.GetString() ?? "unknown"
                : null;
        return (type, status);
    }

    private static void AddJsonProperty(List<string> parts, JsonElement root, string name, string? label = null)
    {
        if (!root.TryGetProperty(name, out var property))
        {
            return;
        }

        var value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label ?? ToSnakeCase(name)}={value}");
        }
    }

    private static void AddArrayCount(List<string> parts, JsonElement root, string name, string? label = null)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        parts.Add($"{label ?? ToSnakeCase(name)}={property.GetArrayLength()}");
    }

    private static string? TryGetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static string? TryGetObjectString(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetString(property, propertyName);
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        foreach (var ch in value)
        {
            if (char.IsUpper(ch) && builder.Length > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static string GetArtifactKind(string path)
    {
        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension) ? "file" : extension.TrimStart('.').ToUpperInvariant();
    }

    private void AppendReplaySessionsMarkdown(StringBuilder builder, IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        if (replaySummaries.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Replay Sessions");
        builder.AppendLine();
        foreach (var summary in replaySummaries)
        {
            builder.AppendLine($"### {BuildReplayTitle(summary)}");
            builder.AppendLine();
            builder.Append($"- {BuildReplayOutcome(summary)}");
            builder.Append($" | [metadata]({EscapeMarkdownLink(summary.MetadataPath)})");
            if (summary.HasTimeline)
            {
                builder.Append($" | [timeline]({EscapeMarkdownLink(summary.TimelinePath)})");
            }

            if (!string.IsNullOrWhiteSpace(summary.FailureCapsulePath))
            {
                builder.Append($" | [failure capsule]({EscapeMarkdownLink(summary.FailureCapsulePath)})");
            }

            builder.AppendLine();
            if (summary.TimelineHighlights.Count > 0)
            {
                builder.AppendLine(summary.HasFailureSignals ? "- Failure timeline:" : "- Session timeline:");
                foreach (var entry in summary.TimelineHighlights)
                {
                    builder.AppendLine($"  - {FormatTimelineEntry(entry)}");
                }
            }

            builder.AppendLine();
        }
    }

    private void AppendReplayWorkflowMarkdown(StringBuilder builder, IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        if (replaySummaries.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Replay Workflow");
        builder.AppendLine();
        foreach (var command in BuildReplayWorkflowCommands(replaySummaries))
        {
            builder.AppendLine($"- `{command.Command}`");
            builder.AppendLine($"  - {command.Purpose}");
        }

        builder.AppendLine();
    }

    private void AppendReplaySessionsHtml(StringBuilder builder, IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        if (replaySummaries.Count == 0)
        {
            return;
        }

        builder.AppendLine("    <section>");
        builder.AppendLine("      <h2>Replay Sessions</h2>");
        builder.AppendLine("      <ul>");
        foreach (var summary in replaySummaries)
        {
            builder.AppendLine("        <li>");
            builder.AppendLine("          <div>");
            builder.AppendLine($"            <div><strong>{HtmlEncode(BuildReplayTitle(summary))}</strong></div>");
            builder.AppendLine($"            <div class=\"root\">{HtmlEncode(BuildReplayOutcome(summary))}</div>");
            builder.Append($"            <div class=\"root\"><a href=\"{HtmlAttributeEncode(EscapeHtmlLink(summary.MetadataPath))}\">metadata</a>");
            if (summary.HasTimeline)
            {
                builder.Append($" | <a href=\"{HtmlAttributeEncode(EscapeHtmlLink(summary.TimelinePath))}\">timeline</a>");
            }

            if (!string.IsNullOrWhiteSpace(summary.FailureCapsulePath))
            {
                builder.Append($" | <a href=\"{HtmlAttributeEncode(EscapeHtmlLink(summary.FailureCapsulePath))}\">failure capsule</a>");
            }

            builder.AppendLine("</div>");
            if (summary.TimelineHighlights.Count > 0)
            {
                builder.AppendLine($"            <div class=\"timeline-label\">{HtmlEncode(summary.HasFailureSignals ? "Failure timeline" : "Session timeline")}</div>");
                builder.AppendLine("            <ul class=\"timeline\">");
                foreach (var entry in summary.TimelineHighlights)
                {
                    builder.AppendLine($"              <li>{HtmlEncode(FormatTimelineEntry(entry))}</li>");
                }

                builder.AppendLine("            </ul>");
            }

            builder.AppendLine("          </div>");
            builder.AppendLine("          <span class=\"kind\">REPLAY</span>");
            builder.AppendLine("        </li>");
        }

        builder.AppendLine("      </ul>");
        builder.AppendLine("    </section>");
    }

    private void AppendReplayWorkflowHtml(StringBuilder builder, IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        if (replaySummaries.Count == 0)
        {
            return;
        }

        builder.AppendLine("    <section>");
        builder.AppendLine("      <h2>Replay Workflow</h2>");
        builder.AppendLine("      <ul>");
        foreach (var command in BuildReplayWorkflowCommands(replaySummaries))
        {
            builder.AppendLine("        <li>");
            builder.AppendLine("          <div>");
            builder.AppendLine($"            <code>{HtmlEncode(command.Command)}</code>");
            builder.AppendLine($"            <div class=\"root\">{HtmlEncode(command.Purpose)}</div>");
            builder.AppendLine("          </div>");
            builder.AppendLine($"          <span class=\"kind\">{HtmlEncode(command.Kind)}</span>");
            builder.AppendLine("        </li>");
        }

        builder.AppendLine("      </ul>");
        builder.AppendLine("    </section>");
    }

    private IEnumerable<ReplayWorkflowCommand> BuildReplayWorkflowCommands(IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        yield return new ReplayWorkflowCommand(
            "CAPSULE",
            $"luotsi replay capsule --artifacts {Quote(_root)} --write-readme --write-json",
            "Start here: summarize failures, artifacts, and recommended replay next steps.");

        if (replaySummaries.Any(static summary => summary.HasFailureSignals))
        {
            yield return new ReplayWorkflowCommand(
                "SCRUB",
                $"luotsi replay scrub --artifacts {Quote(_root)} --failures --context 3 --write-markdown",
                "Review the focused failure window with previous/current/next timeline events.");
            yield return new ReplayWorkflowCommand(
                "GRAPH",
                $"luotsi replay graph --artifacts {Quote(_root)} --failed --write-json --write-markdown",
                "Open semantic failure context with evidence, facts, causal chains, and hypotheses.");
            yield return new ReplayWorkflowCommand(
                "CLUSTER",
                $"luotsi replay cluster --artifacts {Quote(ResolveClusterRoot(_root))} --min-count 2 --write-markdown",
                "Look for matching failure shapes across sibling replay bundles.");
        }
    }

    private static string BuildReplayTitle(SessionReplaySummary summary) =>
        string.IsNullOrWhiteSpace(summary.Target)
            ? summary.SessionKind
            : $"{summary.SessionKind} {summary.Target}";

    private static string BuildReplayOutcome(SessionReplaySummary summary)
    {
        var parts = new List<string>
        {
            $"reason={summary.Reason}",
            $"exit_code={summary.ExitCode}",
            $"events={summary.EventCount}"
        };

        if (!string.IsNullOrWhiteSpace(summary.Target))
        {
            parts.Add($"target={summary.Target}");
        }

        return string.Join(" | ", parts);
    }

    private static string FormatTimelineEntry(SessionReplayTimelineEntry entry)
    {
        var builder = new StringBuilder();
        if (entry.Timestamp is not null)
        {
            builder.Append(entry.Timestamp.Value.ToString("O"));
            builder.Append(" | ");
        }

        builder.Append(entry.Type);
        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            builder.Append(" | ");
            builder.Append(entry.Detail);
        }

        return builder.ToString();
    }

    private static string EscapeMarkdownLink(string path) =>
        path.Replace("\\", "/", StringComparison.Ordinal).Replace(" ", "%20", StringComparison.Ordinal);

    private static string EscapeHtmlLink(string path) =>
        Uri.EscapeDataString(path.Replace("\\", "/", StringComparison.Ordinal)).Replace("%2F", "/", StringComparison.Ordinal);

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;

    private static string ResolveClusterRoot(string artifactRoot)
    {
        var parent = Path.GetDirectoryName(artifactRoot);
        return string.IsNullOrWhiteSpace(parent) ? artifactRoot : parent;
    }

    private static string HtmlEncode(string value) => WebUtility.HtmlEncode(value);

    private static string HtmlAttributeEncode(string value) => WebUtility.HtmlEncode(value);

    private sealed record ReplayWorkflowCommand(string Kind, string Command, string Purpose);
}
