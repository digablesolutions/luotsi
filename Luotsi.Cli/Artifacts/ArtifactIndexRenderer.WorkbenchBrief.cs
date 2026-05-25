using System.Text;

namespace Luotsi.Cli.Artifacts;

internal sealed partial class ArtifactIndexRenderer
{
    private static void AppendFailureBriefHtml(
        StringBuilder builder,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario,
        string actionCommand)
    {
        builder.AppendLine("          <div class=\"failure-brief\" aria-label=\"Failure brief\">");
        AppendBriefCardHtml(
            builder,
            "What failed",
            scenario?.Error?.Message ?? summary.TimelineHighlights.FirstOrDefault(static entry => entry.IsFailureRelevant)?.Detail ?? summary.Reason);
        AppendBriefCardHtml(builder, "What changed", BuildWhatChanged(summary));
        AppendBriefCardHtml(builder, "Run next", actionCommand);
        builder.AppendLine("          </div>");
    }

    private static void AppendBriefCardHtml(StringBuilder builder, string label, string value)
    {
        builder.AppendLine("            <div class=\"brief-card\">");
        builder.AppendLine($"              <span>{HtmlEncode(label)}</span>");
        builder.AppendLine($"              <strong>{HtmlEncode(value)}</strong>");
        builder.AppendLine("            </div>");
    }

    private static string BuildWhatChanged(SessionReplaySummary summary)
    {
        var failureIndex = summary.TimelineHighlights
            .Select(static (entry, index) => new { Entry = entry, Index = index })
            .FirstOrDefault(static item => item.Entry.IsFailureRelevant)
            ?.Index;
        if (failureIndex is > 0)
        {
            return FormatTimelineEntry(summary.TimelineHighlights[failureIndex.Value - 1]);
        }

        var firstSignal = summary.TimelineHighlights.FirstOrDefault(static entry =>
            entry.Type.Contains("diagnostic", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("stats", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("activity", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("screen", StringComparison.OrdinalIgnoreCase));
        return firstSignal is null ? "No pre-failure change signal was captured." : FormatTimelineEntry(firstSignal);
    }
}
