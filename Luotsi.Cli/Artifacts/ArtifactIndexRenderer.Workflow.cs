using System.Net;
using System.Text;

namespace Luotsi.Cli.Artifacts;

internal sealed partial class ArtifactIndexRenderer
{
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
