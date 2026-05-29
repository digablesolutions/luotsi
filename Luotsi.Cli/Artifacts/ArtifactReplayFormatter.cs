namespace Luotsi.Cli.Artifacts;

internal static class ArtifactReplayFormatter
{
    public static string BuildTitle(SessionReplaySummary summary) =>
        string.IsNullOrWhiteSpace(summary.Target)
            ? summary.SessionKind
            : $"{summary.SessionKind} {summary.Target}";

    public static string BuildOutcome(SessionReplaySummary summary)
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
}
