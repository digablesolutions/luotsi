using System.Text;

namespace Luotsi.Cli.Artifacts;

internal static class ArtifactTimelineFormatter
{
    public static string FormatEntry(SessionReplayTimelineEntry entry)
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

    public static string FormatDetail(SessionReplayTimelineEntry entry)
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

    public static string BuildFilterTags(SessionReplayTimelineEntry entry)
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
