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
            builder.AppendLine($"            <li{className} data-filter-item><span class=\"timeline-type\">{HtmlEncode(entry.Type)}</span> {HtmlEncode(FormatTimelineEntry(entry))}</li>");
        }

        builder.AppendLine("          </ul>");
    }
}
