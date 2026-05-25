using System.Text;

namespace Luotsi.Cli.Artifacts;

internal sealed partial class ArtifactIndexRenderer
{
    private static void AppendTimelineFilterHtml(StringBuilder builder)
    {
        builder.AppendLine("          <div class=\"filter-chips\" aria-label=\"Timeline quick filters\">");
        AppendFilterChipHtml(builder, "Failures", "failure");
        AppendFilterChipHtml(builder, "Actions", "action");
        AppendFilterChipHtml(builder, "Screens", "screen");
        AppendFilterChipHtml(builder, "Telemetry", "telemetry");
        builder.AppendLine("          </div>");
    }

    private static void AppendFilterChipHtml(StringBuilder builder, string label, string query) =>
        builder.AppendLine($"            <button class=\"filter-chip\" type=\"button\" data-filter-set=\"{HtmlAttributeEncode(query)}\">{HtmlEncode(label)}</button>");

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
            var className = entry.IsFailureRelevant ? " class=\"timeline-failure\"" : string.Empty;
            builder.AppendLine($"            <li{className} data-filter-item><span class=\"timeline-tags\">{HtmlEncode(BuildTimelineFilterTags(entry))}</span><span class=\"timeline-type\">{HtmlEncode(entry.Type)}</span> {HtmlEncode(FormatTimelineDetail(entry))}</li>");
        }

        builder.AppendLine("          </ul>");
    }

    private static string FormatTimelineDetail(SessionReplayTimelineEntry entry)
    {
        var builder = new StringBuilder();
        if (entry.Timestamp is not null)
        {
            builder.Append(entry.Timestamp.Value.ToString("O"));
        }

        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            if (builder.Length > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(entry.Detail);
        }

        return builder.ToString();
    }

    private static string BuildTimelineFilterTags(SessionReplayTimelineEntry entry)
    {
        var tags = new List<string>();
        if (entry.IsFailureRelevant ||
            entry.Type.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            entry.Detail.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("failure");
        }

        if (entry.Type.Contains("action", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("tap", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("key", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("action");
        }

        if (entry.Type.Contains("screen", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("screenshot", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("visible", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("screen");
        }

        if (entry.Type.Contains("telemetry", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("stats", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("telemetry");
        }

        return string.Join(' ', tags);
    }
}
