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
        return await BuildMarkdownIndexAsync(files, replaySummaries).ConfigureAwait(false);
    }

    public async Task<string> BuildMarkdownIndexAsync(
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
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
        return await BuildHtmlIndexAsync(files, replaySummaries).ConfigureAwait(false);
    }

    public async Task<string> BuildHtmlIndexAsync(
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        var hasFailureWorkbench = replaySummaries.Any(static summary => summary.HasFailureSignals);
        var pageTitle = hasFailureWorkbench ? "Luotsi Failure Workbench" : "Luotsi Artifacts";
        var pageEyebrow = hasFailureWorkbench ? "Replay triage" : "Replay artifacts";
        var pageHeading = hasFailureWorkbench ? "Failure Workbench" : "Luotsi Artifacts";
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\">");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine($"  <title>{HtmlEncode(pageTitle)}</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    :root { color-scheme: light dark; --bg: #0d1117; --surface: #111722; --panel: #151b24; --panel-strong: #1b2330; --panel-subtle: #10151d; --text: #e6edf3; --muted: #8b949e; --line: #30363d; --line-soft: #21262d; --accent: #58a6ff; --accent-strong: #79c0ff; --warning: #d29922; --danger: #f85149; --danger-soft: rgba(248,81,73,.12); --success: #3fb950; --shadow: 0 18px 54px rgba(1,4,9,.32); --code-bg: #0b1018; }");
        builder.AppendLine("    @media (prefers-color-scheme: light) { :root { --bg: #f6f8fa; --surface: #ffffff; --panel: #ffffff; --panel-strong: #f6f8fa; --panel-subtle: #f6f8fa; --text: #24292f; --muted: #57606a; --line: #d0d7de; --line-soft: #eaeef2; --accent: #0969da; --accent-strong: #0550ae; --warning: #9a6700; --danger: #cf222e; --danger-soft: rgba(207,34,46,.08); --success: #1a7f37; --shadow: 0 16px 38px rgba(27,31,36,.08); --code-bg: #f6f8fa; } }");
        builder.AppendLine("    * { box-sizing: border-box; }");
        builder.AppendLine("    body { margin: 0; font: 14px/1.5 Inter, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif; background: var(--bg); color: var(--text); }");
        builder.AppendLine("    main { position: relative; max-width: 1220px; margin: 0 auto; padding: 28px 22px 56px; }");
        builder.AppendLine("    header { margin-bottom: 18px; padding: 22px; border: 1px solid var(--line); background: var(--surface); border-radius: 8px; box-shadow: var(--shadow); }");
        builder.AppendLine("    .eyebrow { margin: 0 0 8px; color: var(--accent); font-size: 11px; font-weight: 760; letter-spacing: .08em; text-transform: uppercase; }");
        builder.AppendLine("    h1 { margin: 0 0 10px; font-size: clamp(26px, 3vw, 38px); line-height: 1.08; letter-spacing: 0; }");
        builder.AppendLine("    .root { color: var(--muted); word-break: break-word; overflow-wrap: anywhere; }");
        builder.AppendLine("    .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 8px; margin-top: 18px; }");
        builder.AppendLine("    .stat { padding: 12px 14px; border: 1px solid var(--line-soft); border-radius: 8px; background: var(--panel-subtle); }");
        builder.AppendLine("    .stat-value { display: block; font-size: 24px; font-weight: 760; line-height: 1.1; }");
        builder.AppendLine("    .stat-label { display: block; margin-top: 4px; color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: .06em; }");
        builder.AppendLine("    section { margin-top: 16px; border: 1px solid var(--line); background: var(--panel); border-radius: 8px; overflow: hidden; box-shadow: 0 8px 24px rgba(1,4,9,.16); }");
        builder.AppendLine("    h2 { margin: 0; padding: 14px 16px; font-size: 14px; line-height: 1.2; border-bottom: 1px solid var(--line); background: var(--panel-strong); letter-spacing: 0; }");
        builder.AppendLine("    ul { list-style: none; margin: 0; padding: 0; }");
        builder.AppendLine("    li { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 16px; align-items: start; padding: 13px 16px; border-top: 1px solid var(--line-soft); }");
        builder.AppendLine("    li:first-child { border-top: 0; }");
        builder.AppendLine("    a { color: var(--accent-strong); text-decoration: none; overflow-wrap: anywhere; }");
        builder.AppendLine("    a:hover { text-decoration: underline; }");
        builder.AppendLine("    code { display: inline-block; max-width: 100%; padding: 4px 7px; border: 1px solid var(--line); border-radius: 6px; background: var(--code-bg); color: var(--text); font: 12px/1.45 ui-monospace, SFMono-Regular, Consolas, Liberation Mono, monospace; overflow-wrap: anywhere; }");
        builder.AppendLine("    .timeline-label { margin-top: 12px; color: var(--muted); font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; }");
        builder.AppendLine("    .timeline { list-style: none; margin: 8px 0 0; padding: 0; border-left: 1px solid var(--line); }");
        builder.AppendLine("    .timeline li { display: block; position: relative; padding: 7px 0 0 14px; border-top: 0; color: var(--muted); }");
        builder.AppendLine("    .timeline li::before { content: \"\"; position: absolute; left: -4px; top: 15px; width: 7px; height: 7px; border-radius: 999px; background: var(--accent); box-shadow: 0 0 0 3px var(--panel); }");
        builder.AppendLine("    .kind { color: var(--muted); font-size: 10px; font-weight: 760; text-transform: uppercase; letter-spacing: .08em; }");
        builder.AppendLine("    .badge { min-width: 74px; justify-self: end; padding: 4px 8px; border: 1px solid var(--line); border-radius: 999px; background: color-mix(in srgb, var(--panel-strong) 78%, transparent); text-align: center; }");
        builder.AppendLine("    .workflow ul { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 12px; padding: 14px; }");
        builder.AppendLine("    .workflow li { display: block; min-height: 128px; padding: 15px; border: 1px solid var(--line-soft); border-radius: 8px; background: var(--surface); }");
        builder.AppendLine("    .workflow li:first-child { border-top: 1px solid var(--line); }");
        builder.AppendLine("    .workflow .kind { display: inline-block; margin-bottom: 10px; color: var(--accent); }");
        builder.AppendLine("    .workflow code { margin-bottom: 9px; }");
        builder.AppendLine("    .toolbar { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 12px; align-items: center; margin: 18px 0 0; }");
        builder.AppendLine("    .search { width: 100%; min-height: 40px; padding: 9px 12px; border: 1px solid var(--line); border-radius: 8px; background: var(--panel-subtle); color: var(--text); outline: none; }");
        builder.AppendLine("    .search:focus { border-color: var(--accent); box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 20%, transparent); }");
        builder.AppendLine("    .jump-links { display: flex; gap: 8px; flex-wrap: wrap; justify-content: flex-end; }");
        builder.AppendLine("    .jump-links a, .copy-command { display: inline-flex; align-items: center; min-height: 34px; padding: 7px 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--panel-strong); color: var(--text); font: inherit; cursor: pointer; text-decoration: none; }");
        builder.AppendLine("    .jump-links a:hover, .copy-command:hover { border-color: var(--accent); background: color-mix(in srgb, var(--accent) 10%, var(--panel-strong)); text-decoration: none; }");
        builder.AppendLine("    .workbench { border-color: color-mix(in srgb, var(--danger) 38%, var(--line)); }");
        builder.AppendLine("    .workbench h2 { color: var(--text); }");
        builder.AppendLine("    .workbench-grid { display: grid; grid-template-columns: minmax(0, 1.18fr) minmax(300px, .82fr); gap: 16px; padding: 16px; }");
        builder.AppendLine("    .panel { padding: 15px; border: 1px solid var(--line-soft); border-radius: 8px; background: var(--surface); }");
        builder.AppendLine("    .hero-panel { display: grid; align-content: start; min-height: 100%; border-color: color-mix(in srgb, var(--danger) 42%, var(--line)); background: linear-gradient(180deg, var(--surface), color-mix(in srgb, var(--danger) 5%, var(--surface))); }");
        builder.AppendLine("    .panel h3 { margin: 0 0 10px; font-size: 12px; line-height: 1.25; letter-spacing: .06em; text-transform: uppercase; color: var(--muted); }");
        builder.AppendLine("    .failure-title { margin: 0 0 8px; font-size: 22px; font-weight: 740; line-height: 1.2; letter-spacing: 0; }");
        builder.AppendLine("    .failure-message { margin: 10px 0 0; padding: 10px 12px; border: 1px solid color-mix(in srgb, var(--danger) 42%, var(--line)); border-left-width: 3px; border-radius: 6px; background: var(--danger-soft); overflow-wrap: anywhere; }");
        builder.AppendLine("    .chip-row { display: flex; flex-wrap: wrap; gap: 8px; margin: 0 0 12px; }");
        builder.AppendLine("    .chip { display: inline-flex; align-items: center; gap: 6px; min-height: 26px; padding: 3px 8px; border: 1px solid var(--line); border-radius: 999px; background: var(--panel-subtle); color: var(--muted); font-size: 12px; font-weight: 650; }");
        builder.AppendLine("    .chip-danger { border-color: color-mix(in srgb, var(--danger) 58%, var(--line)); color: var(--danger); }");
        builder.AppendLine("    .chip-success { border-color: color-mix(in srgb, var(--success) 48%, var(--line)); color: var(--success); }");
        builder.AppendLine("    .chip-warning { border-color: color-mix(in srgb, var(--warning) 50%, var(--line)); color: var(--warning); }");
        builder.AppendLine("    .meta-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(130px, 1fr)); gap: 8px; margin-top: 12px; }");
        builder.AppendLine("    .meta { padding: 9px 10px; border: 1px solid var(--line-soft); border-radius: 7px; background: var(--panel-subtle); }");
        builder.AppendLine("    .meta span { display: block; color: var(--muted); font-size: 11px; text-transform: uppercase; letter-spacing: .06em; }");
        builder.AppendLine("    .meta strong { display: block; margin-top: 3px; font-weight: 680; overflow-wrap: anywhere; }");
        builder.AppendLine("    .evidence-list { display: grid; gap: 8px; margin: 0; padding: 0; }");
        builder.AppendLine("    .evidence-list li { display: block; padding: 9px 10px; border: 1px solid var(--line-soft); border-radius: 7px; background: var(--panel-subtle); }");
        builder.AppendLine("    .evidence-list li.primary-evidence { border-color: color-mix(in srgb, var(--accent) 46%, var(--line)); background: color-mix(in srgb, var(--accent) 7%, var(--panel-subtle)); }");
        builder.AppendLine("    .triage-path { display: grid; gap: 10px; }");
        builder.AppendLine("    .triage-step { display: grid; grid-template-columns: 32px minmax(0, 1fr); gap: 10px; align-items: start; padding: 10px; border: 1px solid var(--line-soft); border-radius: 8px; background: var(--panel-subtle); }");
        builder.AppendLine("    .step-number { display: grid; place-items: center; width: 28px; height: 28px; border: 1px solid color-mix(in srgb, var(--accent) 42%, var(--line)); border-radius: 999px; background: color-mix(in srgb, var(--accent) 9%, var(--panel-subtle)); color: var(--accent); font-weight: 760; }");
        builder.AppendLine("    .step-title { font-weight: 720; }");
        builder.AppendLine("    .command-row { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 8px; align-items: start; margin-top: 8px; }");
        builder.AppendLine("    .command-row code { display: block; width: 100%; white-space: nowrap; overflow-x: auto; overflow-y: hidden; }");
        builder.AppendLine("    .command-row .copy-command { flex: 0 0 auto; }");
        builder.AppendLine("    .next-action { margin-top: 12px; }");
        builder.AppendLine("    .next-action code { margin-top: 7px; }");
        builder.AppendLine("    .empty { padding: 18px 16px; color: var(--muted); }");
        builder.AppendLine("    @media (max-width: 900px) { .workbench-grid { grid-template-columns: 1fr; } .toolbar { grid-template-columns: 1fr; } .jump-links { justify-content: flex-start; } }");
        builder.AppendLine("    @media (max-width: 680px) { main { padding: 18px 12px 34px; } header { padding: 18px; } li { grid-template-columns: 1fr; } .badge { justify-self: start; } .workflow ul { grid-template-columns: 1fr; } .command-row { grid-template-columns: 1fr; } }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <main>");
        builder.AppendLine("    <header>");
        builder.AppendLine($"      <div class=\"eyebrow\">{HtmlEncode(pageEyebrow)}</div>");
        builder.AppendLine($"      <h1>{HtmlEncode(pageHeading)}</h1>");
        builder.AppendLine($"      <div class=\"root\">{HtmlEncode(_root)}</div>");
        AppendHeaderStatsHtml(builder, files, replaySummaries);
        AppendToolbarHtml(builder, replaySummaries);
        builder.AppendLine("    </header>");

        if (files.Count == 0)
        {
            builder.AppendLine("    <section><h2>Artifacts</h2><div class=\"empty\">No artifacts have been written yet.</div></section>");
        }
        else
        {
            AppendFailureWorkbenchHtml(builder, replaySummaries);
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
        AppendIndexScriptHtml(builder);
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

            if (string.Equals(schemaName, ResultSchemas.ReplayOpen, StringComparison.Ordinal))
            {
                return BuildReplayOpenSummary(root);
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

    private static string? BuildReplayOpenSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "sessionCount", "session_count");
        AddJsonProperty(parts, root, "failureCount", "failure_count");
        AddJsonProperty(parts, root, "opened");
        AddReplayOpenNextActionSummary(parts, root);
        AddReplayOpenPrimaryFailureSummary(parts, root);
        AddArrayCount(parts, root, "commands");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static void AddReplayOpenNextActionSummary(List<string> parts, JsonElement root)
    {
        if (!TryGetProperty(root, "recommendedNextAction", "recommended_next_action", out var nextAction) ||
            nextAction.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        AddJsonProperty(parts, nextAction, "kind", "recommended_action");
        AddJsonProperty(parts, nextAction, "title", "recommended_title");
        AddJsonProperty(parts, nextAction, "command", "recommended_command");
    }

    private static void AddReplayOpenPrimaryFailureSummary(List<string> parts, JsonElement root)
    {
        if (!TryGetProperty(root, "primaryFailure", "primary_failure", out var primaryFailure) ||
            primaryFailure.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var summary = new[]
            {
                TryGetString(primaryFailure, "scenario"),
                TryGetString(primaryFailure, "step"),
                TryGetString(primaryFailure, "action"),
                TryGetString(primaryFailure, "message")
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Take(3)
            .ToArray();
        if (summary.Length > 0)
        {
            parts.Add("primary_failure=" + string.Join(" / ", summary));
        }
    }

    private static string? BuildReplayCapsuleSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "sessionCount", "session_count");
        AddJsonProperty(parts, root, "failureCount", "failure_count");
        AddJsonProperty(parts, root, "scenarioDraftAvailable", "scenario_draft_available");
        AddJsonProperty(parts, root, "scenarioDraftReason", "scenario_draft_reason");
        AddReplayCapsulePrimaryFailureSummary(parts, root);
        AddReplayCapsuleNextStepSummary(parts, root);
        AddArrayCount(parts, root, "artifactManifest", "artifact_manifest");
        AddArrayCount(parts, root, "failureTimeline", "failure_timeline");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static void AddReplayCapsulePrimaryFailureSummary(List<string> parts, JsonElement root)
    {
        if (!TryGetProperty(root, "primaryFailure", "primary_failure", out var primaryFailure) ||
            primaryFailure.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var summary = new[]
            {
                TryGetString(primaryFailure, "scenario"),
                TryGetString(primaryFailure, "step"),
                TryGetString(primaryFailure, "action"),
                TryGetString(primaryFailure, "message")
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Take(3)
            .ToArray();
        if (summary.Length > 0)
        {
            parts.Add("primary_failure=" + string.Join(" / ", summary));
        }
    }

    private static void AddReplayCapsuleNextStepSummary(List<string> parts, JsonElement root)
    {
        if (!TryGetProperty(root, "recommendedNextSteps", "recommended_next_steps", out var nextSteps) ||
            nextSteps.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var firstStep = nextSteps.EnumerateArray().FirstOrDefault();
        if (firstStep.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var title = TryGetString(firstStep, "title");
        var command = TryGetString(firstStep, "command");
        if (!string.IsNullOrWhiteSpace(title))
        {
            parts.Add("next_step=" + title);
        }

        if (!string.IsNullOrWhiteSpace(command))
        {
            parts.Add("next_command=" + command);
        }
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

    private static bool TryGetProperty(JsonElement root, string name, string alternateName, out JsonElement property) =>
        root.TryGetProperty(name, out property) || root.TryGetProperty(alternateName, out property);

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

        builder.AppendLine("## Replay Front Door");
        builder.AppendLine();
        foreach (var command in BuildReplayWorkflowCommands(replaySummaries))
        {
            builder.AppendLine($"- `{command.Command}`");
            builder.AppendLine($"  - {command.Purpose}");
        }

        builder.AppendLine();
    }

    private static void AppendHeaderStatsHtml(
        StringBuilder builder,
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        builder.AppendLine("      <div class=\"stats\">");
        AppendHeaderStatHtml(builder, replaySummaries.Count, "Replay sessions");
        AppendHeaderStatHtml(builder, replaySummaries.Count(static summary => summary.HasFailureSignals), "Failure signals");
        AppendHeaderStatHtml(builder, files.Count, "Artifacts");
        AppendHeaderStatHtml(builder, files.Count(IsReportArtifact), "Reports");
        builder.AppendLine("      </div>");
    }

    private static void AppendHeaderStatHtml(StringBuilder builder, int value, string label)
    {
        builder.AppendLine("        <div class=\"stat\">");
        builder.AppendLine($"          <span class=\"stat-value\">{value}</span>");
        builder.AppendLine($"          <span class=\"stat-label\">{HtmlEncode(label)}</span>");
        builder.AppendLine("        </div>");
    }

    private static void AppendToolbarHtml(StringBuilder builder, IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        if (replaySummaries.Count == 0)
        {
            return;
        }

        builder.AppendLine("      <div class=\"toolbar\">");
        builder.AppendLine("        <input class=\"search\" type=\"search\" placeholder=\"Filter artifacts, timeline, commands, and evidence\" aria-label=\"Filter artifact index\" data-filter-input>");
        builder.AppendLine("        <nav class=\"jump-links\" aria-label=\"Artifact sections\">");
        builder.AppendLine("          <a href=\"#failure-workbench\">Workbench</a>");
        builder.AppendLine("          <a href=\"#replay-sessions\">Sessions</a>");
        builder.AppendLine("          <a href=\"#replay-front-door\">Commands</a>");
        builder.AppendLine("        </nav>");
        builder.AppendLine("      </div>");
    }

    private void AppendFailureWorkbenchHtml(StringBuilder builder, IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        var primary = SelectPrimaryFailure(replaySummaries);
        if (primary is null)
        {
            return;
        }

        var summary = primary.Value.Summary;
        var scenario = primary.Value.Scenario;
        var step = scenario?.FailedStep;
        var error = scenario?.Error;
        var title = scenario is not null
            ? scenario.Scenario
            : BuildReplayTitle(summary);
        var actionCommand = $"luotsi replay scrub --artifacts {Quote(_root)} --failures --context 3 --write-markdown";

        builder.AppendLine("    <section class=\"workbench\" id=\"failure-workbench\">");
        builder.AppendLine("      <h2>Failure Workbench</h2>");
        builder.AppendLine("      <div class=\"workbench-grid\">");
        builder.AppendLine("        <div class=\"panel hero-panel\" data-filter-item>");
        builder.AppendLine("          <h3>Primary failure</h3>");
        builder.AppendLine("          <div class=\"chip-row\">");
        builder.AppendLine("            <span class=\"chip chip-danger\">needs triage</span>");
        builder.AppendLine($"            <span class=\"chip\">{HtmlEncode(summary.SessionKind)}</span>");
        builder.AppendLine($"            <span class=\"chip\">{HtmlEncode(summary.EventCount.ToString(System.Globalization.CultureInfo.InvariantCulture))} events</span>");
        if (!string.IsNullOrWhiteSpace(summary.Target))
        {
            builder.AppendLine($"            <span class=\"chip\">{HtmlEncode(summary.Target)}</span>");
        }

        builder.AppendLine("          </div>");
        builder.AppendLine($"          <p class=\"failure-title\">{HtmlEncode(title)}</p>");
        builder.AppendLine("          <div class=\"meta-grid\">");
        AppendMetaHtml(builder, "Session", BuildReplayTitle(summary));
        AppendMetaHtml(builder, "Reason", summary.Reason);
        AppendMetaHtml(builder, "Step", step?.Name ?? "unknown");
        AppendMetaHtml(builder, "Action", step?.Action ?? "unknown");
        builder.AppendLine("          </div>");
        if (!string.IsNullOrWhiteSpace(error?.Message))
        {
            var category = string.IsNullOrWhiteSpace(error.Category) ? "failure" : error.Category;
            builder.AppendLine($"          <div class=\"failure-message\"><strong>{HtmlEncode(category)}</strong>: {HtmlEncode(error.Message)}</div>");
        }

        builder.AppendLine("          <div class=\"next-action\">");
        builder.AppendLine("            <h3>Recommended next action</h3>");
        builder.AppendLine("            <div class=\"root\">Scrub the smallest timeline window before opening broader evidence.</div>");
        AppendCommandRowHtml(builder, actionCommand);
        builder.AppendLine("          </div>");
        builder.AppendLine("        </div>");
        builder.AppendLine("        <div class=\"panel\" data-filter-item>");
        builder.AppendLine("          <h3>Triage path</h3>");
        AppendTriagePathHtml(builder, replaySummaries, actionCommand);
        builder.AppendLine("        </div>");
        builder.AppendLine("        <div class=\"panel\" data-filter-item>");
        builder.AppendLine("          <h3>Evidence</h3>");
        AppendEvidenceHtml(builder, summary, scenario);
        builder.AppendLine("        </div>");
        builder.AppendLine("        <div class=\"panel\" data-filter-item>");
        builder.AppendLine("          <h3>Timeline preview</h3>");
        AppendTimelineHtml(builder, summary);
        builder.AppendLine("        </div>");
        AppendSemanticSignalsHtml(builder);
        builder.AppendLine("        <div class=\"panel\" data-filter-item>");
        builder.AppendLine("          <h3>Replay actions</h3>");
        builder.AppendLine("          <ul class=\"evidence-list\">");
        foreach (var command in BuildReplayWorkflowCommands(replaySummaries).Take(4))
        {
            builder.AppendLine("            <li>");
            builder.AppendLine($"              <div class=\"kind\">{HtmlEncode(command.Kind)}</div>");
            AppendCommandRowHtml(builder, command.Command, "              ");
            builder.AppendLine("            </li>");
        }

        builder.AppendLine("          </ul>");
        builder.AppendLine("        </div>");
        builder.AppendLine("      </div>");
        builder.AppendLine("    </section>");
    }

    private void AppendTriagePathHtml(
        StringBuilder builder,
        IReadOnlyList<SessionReplaySummary> replaySummaries,
        string scrubCommand)
    {
        var graphCommand = BuildReplayWorkflowCommands(replaySummaries)
            .FirstOrDefault(static command => string.Equals(command.Kind, "GRAPH", StringComparison.OrdinalIgnoreCase))
            ?.Command;
        var clusterCommand = BuildReplayWorkflowCommands(replaySummaries)
            .FirstOrDefault(static command => string.Equals(command.Kind, "CLUSTER", StringComparison.OrdinalIgnoreCase))
            ?.Command;

        builder.AppendLine("          <div class=\"triage-path\">");
        AppendTriageStepHtml(builder, 1, "Replay the failure window", "Start with the narrowest failing moment and adjacent events.", scrubCommand);
        AppendTriageStepHtml(builder, 2, "Read semantic signals", "Use graph facts and hypotheses to separate app, device, and transport causes.", graphCommand);
        AppendTriageStepHtml(builder, 3, "Check recurrence", "Compare sibling bundles before treating the failure as unique.", clusterCommand);
        builder.AppendLine("          </div>");
    }

    private static void AppendTriageStepHtml(
        StringBuilder builder,
        int number,
        string title,
        string description,
        string? command)
    {
        builder.AppendLine("            <div class=\"triage-step\">");
        builder.AppendLine($"              <div class=\"step-number\">{number}</div>");
        builder.AppendLine("              <div>");
        builder.AppendLine($"                <div class=\"step-title\">{HtmlEncode(title)}</div>");
        builder.AppendLine($"                <div class=\"root\">{HtmlEncode(description)}</div>");
        if (!string.IsNullOrWhiteSpace(command))
        {
            AppendCommandRowHtml(builder, command, "                ");
        }

        builder.AppendLine("              </div>");
        builder.AppendLine("            </div>");
    }

    private static void AppendMetaHtml(StringBuilder builder, string label, string value)
    {
        builder.AppendLine("            <div class=\"meta\">");
        builder.AppendLine($"              <span>{HtmlEncode(label)}</span>");
        builder.AppendLine($"              <strong>{HtmlEncode(value)}</strong>");
        builder.AppendLine("            </div>");
    }

    private static void AppendCommandRowHtml(StringBuilder builder, string command, string indent = "            ")
    {
        builder.AppendLine($"{indent}<div class=\"command-row\">");
        builder.AppendLine($"{indent}  <code>{HtmlEncode(command)}</code>");
        builder.AppendLine($"{indent}  <button class=\"copy-command\" type=\"button\" data-copy=\"{HtmlAttributeEncode(command)}\">Copy</button>");
        builder.AppendLine($"{indent}</div>");
    }

    private void AppendEvidenceHtml(StringBuilder builder, SessionReplaySummary summary, FailureCapsuleScenario? scenario)
    {
        var evidence = new List<FailureCapsuleArtifactLink>();
        if (scenario is not null)
        {
            evidence.AddRange(scenario.Artifacts);
        }

        if (!string.IsNullOrWhiteSpace(summary.FailureCapsulePath))
        {
            evidence.Add(new FailureCapsuleArtifactLink("failure capsule", summary.FailureCapsulePath, null, null));
        }

        if (summary.HasTimeline)
        {
            evidence.Add(new FailureCapsuleArtifactLink("timeline", summary.TimelinePath, null, null));
        }

        evidence.Add(new FailureCapsuleArtifactLink("metadata", summary.MetadataPath, null, null));

        builder.AppendLine("          <ul class=\"evidence-list\">");
        var index = 0;
        foreach (var item in evidence
            .Where(static item => !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Take(8))
        {
            var evidenceClass = index == 0 ? " class=\"primary-evidence\"" : string.Empty;
            builder.AppendLine($"            <li{evidenceClass} data-filter-item>");
            builder.AppendLine($"              <a href=\"{HtmlAttributeEncode(EscapeHtmlLink(item.Path))}\">{HtmlEncode(item.Path)}</a>");
            builder.AppendLine($"              <div class=\"root\">{HtmlEncode(item.Kind)}{FormatStepSuffix(item)}</div>");
            builder.AppendLine("            </li>");
            index++;
        }

        builder.AppendLine("          </ul>");
    }

    private static void AppendTimelineHtml(StringBuilder builder, SessionReplaySummary summary)
    {
        if (summary.TimelineHighlights.Count == 0)
        {
            builder.AppendLine("          <div class=\"root\">No timeline highlights were available.</div>");
            return;
        }

        builder.AppendLine("          <ul class=\"timeline\">");
        foreach (var entry in summary.TimelineHighlights.Take(8))
        {
            builder.AppendLine($"            <li>{HtmlEncode(FormatTimelineEntry(entry))}</li>");
        }

        builder.AppendLine("          </ul>");
    }

    private void AppendSemanticSignalsHtml(StringBuilder builder)
    {
        var signals = new ReplayGraphSignalReader(_root, _fileSystem).TryRead();
        if (signals is null)
        {
            return;
        }

        builder.AppendLine("        <div class=\"panel\" data-filter-item>");
        builder.AppendLine("          <h3>Semantic signals</h3>");
        builder.AppendLine("          <ul class=\"evidence-list\">");
        foreach (var item in signals.Items.Take(5))
        {
            builder.AppendLine("            <li data-filter-item>");
            builder.AppendLine($"              <div class=\"kind\">{HtmlEncode(item.Kind)}</div>");
            builder.AppendLine($"              <div>{HtmlEncode(item.Text)}</div>");
            if (!string.IsNullOrWhiteSpace(item.Command))
            {
                AppendCommandRowHtml(builder, item.Command, "              ");
            }

            builder.AppendLine("            </li>");
        }

        builder.AppendLine("          </ul>");
        builder.AppendLine($"          <div class=\"root\"><a href=\"{HtmlAttributeEncode(EscapeHtmlLink(signals.Path))}\">Open graph JSON</a></div>");
        builder.AppendLine("        </div>");
    }

    private static (SessionReplaySummary Summary, FailureCapsuleScenario? Scenario)? SelectPrimaryFailure(
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        foreach (var summary in replaySummaries.Where(static item => item.HasFailureSignals))
        {
            var scenario = summary.FailureCapsule?.Scenarios.FirstOrDefault(static item =>
                string.Equals(item.Status, "failed", StringComparison.OrdinalIgnoreCase) ||
                item.Error is not null ||
                item.FailedStep is not null);
            return (summary, scenario);
        }

        return null;
    }

    private static string FormatStepSuffix(FailureCapsuleArtifactLink item) =>
        string.IsNullOrWhiteSpace(item.StepName) ? string.Empty : $" for {item.StepName}";

    private void AppendReplaySessionsHtml(StringBuilder builder, IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        if (replaySummaries.Count == 0)
        {
            return;
        }

        builder.AppendLine("    <section id=\"replay-sessions\">");
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
            builder.AppendLine("          <span class=\"kind badge\">REPLAY</span>");
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

        builder.AppendLine("    <section class=\"workflow\" id=\"replay-front-door\">");
        builder.AppendLine("      <h2>Replay Front Door</h2>");
        builder.AppendLine("      <ul>");
        foreach (var command in BuildReplayWorkflowCommands(replaySummaries))
        {
            builder.AppendLine("        <li>");
            builder.AppendLine($"          <span class=\"kind\">{HtmlEncode(command.Kind)}</span>");
            builder.AppendLine($"          <div><code>{HtmlEncode(command.Command)}</code></div>");
            builder.AppendLine($"          <div class=\"root\">{HtmlEncode(command.Purpose)}</div>");
            builder.AppendLine("        </li>");
        }

        builder.AppendLine("      </ul>");
        builder.AppendLine("    </section>");
    }

    private static void AppendIndexScriptHtml(StringBuilder builder)
    {
        builder.AppendLine("  <script>");
        builder.AppendLine("    (() => {");
        builder.AppendLine("      const input = document.querySelector('[data-filter-input]');");
        builder.AppendLine("      const items = Array.from(document.querySelectorAll('[data-filter-item]'));");
        builder.AppendLine("      if (input) {");
        builder.AppendLine("        input.addEventListener('input', () => {");
        builder.AppendLine("          const query = input.value.trim().toLowerCase();");
        builder.AppendLine("          for (const item of items) {");
        builder.AppendLine("            item.hidden = query.length > 0 && !item.textContent.toLowerCase().includes(query);");
        builder.AppendLine("          }");
        builder.AppendLine("        });");
        builder.AppendLine("      }");
        builder.AppendLine("      for (const button of document.querySelectorAll('[data-copy]')) {");
        builder.AppendLine("        button.addEventListener('click', async () => {");
        builder.AppendLine("          const value = button.getAttribute('data-copy') || '';");
        builder.AppendLine("          try {");
        builder.AppendLine("            await navigator.clipboard.writeText(value);");
        builder.AppendLine("            const label = button.textContent;");
        builder.AppendLine("            button.textContent = 'Copied';");
        builder.AppendLine("            setTimeout(() => { button.textContent = label; }, 1200);");
        builder.AppendLine("          } catch {");
        builder.AppendLine("            button.textContent = 'Select';");
        builder.AppendLine("          }");
        builder.AppendLine("        });");
        builder.AppendLine("      }");
        builder.AppendLine("    })();");
        builder.AppendLine("  </script>");
    }

    private IEnumerable<ReplayWorkflowCommand> BuildReplayWorkflowCommands(IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        yield return new ReplayWorkflowCommand(
            "OPEN",
            $"luotsi replay open --artifacts {Quote(_root)}",
            "Start here: refresh the browser index and get the canonical replay workflow summary.");
        yield return new ReplayWorkflowCommand(
            "CAPSULE",
            $"luotsi replay capsule --artifacts {Quote(_root)} --write-readme --write-json",
            "Write the bundle summary, primary failure, artifact manifest, and recommended replay next steps.");

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

    private static bool IsReportArtifact(string path) =>
        string.Equals(GetArtifactCategory(path), "Reports", StringComparison.Ordinal);

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
