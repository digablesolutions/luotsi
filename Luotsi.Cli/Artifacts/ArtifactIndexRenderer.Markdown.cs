using System.Text;

namespace Luotsi.Cli.Artifacts;

internal sealed partial class ArtifactIndexRenderer
{
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

}
