using System.Text;

namespace Luotsi.Cli.Artifacts;

internal sealed partial class ArtifactIndexRenderer
{
    private void AppendEvidenceGroupsHtml(
        StringBuilder builder,
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario)
    {
        var groups = BuildWorkbenchEvidenceGroups(files, replaySummaries, summary, scenario);
        if (groups.Count == 0)
        {
            return;
        }

        builder.AppendLine("          <h3>Evidence groups</h3>");
        builder.AppendLine("          <div class=\"evidence-groups\">");
        foreach (var group in groups)
        {
            builder.AppendLine($"            <article class=\"evidence-group evidence-group-{HtmlAttributeEncode(group.Kind)}\" data-filter-item>");
            builder.AppendLine("              <div class=\"evidence-group-header\">");
            builder.AppendLine($"                <span class=\"kind\">{HtmlEncode(group.Kind)}</span>");
            builder.AppendLine($"                <strong>{HtmlEncode(group.Title)}</strong>");
            builder.AppendLine("              </div>");
            builder.AppendLine($"              <div class=\"root\">{HtmlEncode(group.Summary)}</div>");
            builder.AppendLine("              <ul class=\"evidence-group-items\">");
            foreach (var item in group.Items.Take(5))
            {
                builder.AppendLine("                <li data-filter-item>");
                builder.AppendLine($"                  <span>{HtmlEncode(item.Label)}</span>");
                if (item.IsCommand)
                {
                    builder.AppendLine($"                  <code>{HtmlEncode(item.Detail)}</code>");
                }
                else if (!string.IsNullOrWhiteSpace(item.Path))
                {
                    builder.AppendLine($"                  <a href=\"{HtmlAttributeEncode(EscapeHtmlLink(item.Path))}\">{HtmlEncode(item.Detail)}</a>");
                }
                else
                {
                    builder.AppendLine($"                  <strong>{HtmlEncode(item.Detail)}</strong>");
                }

                builder.AppendLine("                </li>");
            }

            builder.AppendLine("              </ul>");
            builder.AppendLine("            </article>");
        }

        builder.AppendLine("          </div>");
    }

    private IReadOnlyList<WorkbenchEvidenceGroup> BuildWorkbenchEvidenceGroups(
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario)
    {
        var groups = new List<WorkbenchEvidenceGroup>();
        AddFailureSignalGroup(groups, summary, scenario);
        AddDeviceAppFactsGroup(groups, summary);
        AddActionsCommandsGroup(groups, replaySummaries, summary);
        AddMediaReportsGroup(groups, files, summary, scenario);
        return groups;
    }

    private static void AddFailureSignalGroup(
        List<WorkbenchEvidenceGroup> groups,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario)
    {
        var items = new List<WorkbenchEvidenceItem>();
        if (!string.IsNullOrWhiteSpace(scenario?.Error?.Message))
        {
            var category = string.IsNullOrWhiteSpace(scenario.Error.Category) ? "failure" : scenario.Error.Category;
            items.Add(new WorkbenchEvidenceItem("Error", $"{category}: {scenario.Error.Message}", null));
        }

        if (scenario?.FailedStep is not null)
        {
            items.Add(new WorkbenchEvidenceItem("Failed step", $"{scenario.FailedStep.Name} ({scenario.FailedStep.Action})", null));
        }

        items.AddRange(summary.TimelineHighlights
            .Where(static entry => entry.IsFailureRelevant)
            .Select(static entry => new WorkbenchEvidenceItem(entry.Type, FormatTimelineDetail(entry), null)));

        if (items.Count == 0)
        {
            return;
        }

        groups.Add(new WorkbenchEvidenceGroup(
            "Failure signals",
            "Events and capsule fields that explain the primary failure shape.",
            "failure",
            DeduplicateEvidenceItems(items)));
    }

    private static void AddDeviceAppFactsGroup(List<WorkbenchEvidenceGroup> groups, SessionReplaySummary summary)
    {
        var items = new List<WorkbenchEvidenceItem>
        {
            new("Session", BuildReplayTitle(summary), summary.MetadataPath),
            new("Exit", $"reason={summary.Reason} | exit_code={summary.ExitCode}", null),
            new("Events", $"{summary.EventCount} events", summary.HasTimeline ? summary.TimelinePath : null)
        };

        if (!string.IsNullOrWhiteSpace(summary.Target))
        {
            items.Insert(0, new WorkbenchEvidenceItem("Target", summary.Target, null));
        }

        items.AddRange(summary.TimelineHighlights
            .Where(IsDeviceOrSessionFact)
            .Select(static entry => new WorkbenchEvidenceItem(entry.Type, FormatTimelineDetail(entry), null)));

        groups.Add(new WorkbenchEvidenceGroup(
            "Device and app facts",
            "The run target, session outcome, and health signals that frame the failure.",
            "context",
            DeduplicateEvidenceItems(items)));
    }

    private void AddActionsCommandsGroup(
        List<WorkbenchEvidenceGroup> groups,
        IReadOnlyList<SessionReplaySummary> replaySummaries,
        SessionReplaySummary summary)
    {
        var items = BuildReplayWorkflowCommands(replaySummaries)
            .Where(static command => string.Equals(command.Kind, "SCRUB", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command.Kind, "GRAPH", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command.Kind, "OPEN", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(static command => new WorkbenchEvidenceItem(command.Kind, command.Command, null, IsCommand: true))
            .ToList();

        items.AddRange(summary.TimelineHighlights
            .Where(IsActionOrCommandEvent)
            .Select(static entry => new WorkbenchEvidenceItem(entry.Type, FormatTimelineDetail(entry), null)));

        if (items.Count == 0)
        {
            items.Add(new WorkbenchEvidenceItem("Timeline", "No explicit action events were highlighted; scrub the failure window first.", summary.TimelinePath));
        }

        groups.Add(new WorkbenchEvidenceGroup(
            "Actions and commands",
            "The commands and interactions most likely to reproduce or narrow the issue.",
            "action",
            DeduplicateEvidenceItems(items)));
    }

    private static void AddMediaReportsGroup(
        List<WorkbenchEvidenceGroup> groups,
        IReadOnlyList<string> files,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario)
    {
        var items = new List<WorkbenchEvidenceItem>();
        if (scenario is not null)
        {
            items.AddRange(scenario.Artifacts
                .Where(static artifact => !string.IsNullOrWhiteSpace(artifact.Path))
                .Select(static artifact => new WorkbenchEvidenceItem(artifact.Kind, artifact.Path, artifact.Path)));
        }

        if (!string.IsNullOrWhiteSpace(summary.FailureCapsulePath))
        {
            items.Add(new WorkbenchEvidenceItem("failure capsule", summary.FailureCapsulePath, summary.FailureCapsulePath));
        }

        items.AddRange(files
            .Where(IsReportArtifact)
            .Select(static file => new WorkbenchEvidenceItem("report", file, file)));

        if (items.Count == 0)
        {
            return;
        }

        groups.Add(new WorkbenchEvidenceGroup(
            "Media and reports",
            "Screenshots, videos, logs, capsules, and machine-readable reports to open next.",
            "artifact",
            DeduplicateEvidenceItems(items)));
    }

    private static bool IsDeviceOrSessionFact(SessionReplayTimelineEntry entry) =>
        entry.Type.Contains("stats", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("started", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("ended", StringComparison.OrdinalIgnoreCase) ||
        entry.Detail.Contains("device=", StringComparison.OrdinalIgnoreCase) ||
        entry.Detail.Contains("target=", StringComparison.OrdinalIgnoreCase) ||
        entry.Detail.Contains("decoded_frames=", StringComparison.OrdinalIgnoreCase) ||
        entry.Detail.Contains("scenario=", StringComparison.OrdinalIgnoreCase);

    private static bool IsActionOrCommandEvent(SessionReplayTimelineEntry entry) =>
        entry.Type.Contains("action", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("command", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("tap", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("key", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("reconnect", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("share_client", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<WorkbenchEvidenceItem> DeduplicateEvidenceItems(IReadOnlyList<WorkbenchEvidenceItem> items) =>
        items
            .Where(static item => !string.IsNullOrWhiteSpace(item.Detail))
            .GroupBy(static item => $"{item.Label}\n{item.Detail}\n{item.Path}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

    private sealed record WorkbenchEvidenceGroup(
        string Title,
        string Summary,
        string Kind,
        IReadOnlyList<WorkbenchEvidenceItem> Items);

    private sealed record WorkbenchEvidenceItem(string Label, string Detail, string? Path, bool IsCommand = false);
}
