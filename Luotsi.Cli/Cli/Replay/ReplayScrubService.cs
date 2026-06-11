using System.Globalization;
using System.Text;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal sealed class ReplayScrubService(ReplayTimelineService timelineService)
{
    private const string ScrubJsonFileName = "replay-scrub.json";
    private const string ScrubMarkdownFileName = "replay-scrub.md";
    private readonly ReplayTimelineService _timelineService = timelineService ?? throw new ArgumentNullException(nameof(timelineService));

    public async Task<ReplayScrubResult> CreateAsync(CliOptions options, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var timeline = await _timelineService.ReadEventsAsync(options, artifacts).ConfigureAwait(false);
        var focusIndex = FindFocusIndex(timeline.Events);
        var focus = GetEvent(timeline.Events, focusIndex);
        var previous = GetEvent(timeline.Events, focusIndex - 1);
        var next = GetEvent(timeline.Events, focusIndex + 1);
        var jsonPath = options.HasFlag("write-json")
            ? Path.Join(artifacts.Root, ScrubJsonFileName)
            : null;
        var markdownPath = options.HasFlag("write-markdown")
            ? Path.Join(artifacts.Root, ScrubMarkdownFileName)
            : null;
        var result = new ReplayScrubResult(
            ResultSchemas.ReplayScrub,
            artifacts.Root,
            timeline.Events.Count,
            focusIndex,
            jsonPath,
            markdownPath,
            focus,
            previous,
            next,
            timeline.Events,
            BuildCommandHints(artifacts.Root, focus, previous, next).ToArray());

        if (jsonPath is not null)
        {
            await artifacts.WriteJsonAsync(ScrubJsonFileName, result).ConfigureAwait(false);
        }

        if (markdownPath is not null)
        {
            await artifacts.WriteTextAsync(ScrubMarkdownFileName, BuildMarkdown(result)).ConfigureAwait(false);
        }

        return result;
    }

    private static int FindFocusIndex(IReadOnlyList<ReplayTimelineEventResult> events)
    {
        if (events.Count == 0)
        {
            return -1;
        }

        var failureIndex = events.ToList().FindIndex(static evt => evt.FailureRelevant);
        return failureIndex >= 0 ? failureIndex : 0;
    }

    private static ReplayTimelineEventResult? GetEvent(IReadOnlyList<ReplayTimelineEventResult> events, int index) =>
        index >= 0 && index < events.Count ? events[index] : null;

    private static IEnumerable<ReplayScrubCommandHint> BuildCommandHints(
        string artifactRoot,
        ReplayTimelineEventResult? focus,
        ReplayTimelineEventResult? previous,
        ReplayTimelineEventResult? next)
    {
        yield return new ReplayScrubCommandHint(
            $"luotsi replay packet --artifacts {Quote(artifactRoot)}",
            "Write the durable first-minute packet for this artifact root.");
        yield return new ReplayScrubCommandHint(
            $"luotsi replay packet --artifacts {Quote(artifactRoot)} --check",
            "Validate the durable packet before handoff or deeper replay.");
        yield return new ReplayScrubCommandHint(
            $"luotsi replay open --artifacts {Quote(artifactRoot)}",
            "Open the replay front door for this artifact root.");
        yield return new ReplayScrubCommandHint(
            $"luotsi replay capsule --artifacts {Quote(artifactRoot)} --write-readme --write-json",
            "Write the replay capsule README and JSON summary.");

        if (focus is not null)
        {
            yield return new ReplayScrubCommandHint(
                BuildTimelineCommand(artifactRoot, focus, 5),
                "Reopen the focused event with nearby timeline context.");
            yield return new ReplayScrubCommandHint(
                $"luotsi replay search --artifacts {Quote(artifactRoot)} --contains {Quote(focus.Detail)}",
                "Search the artifact bundle for the focused event detail.");

            if (focus.FailureRelevant)
            {
                yield return new ReplayScrubCommandHint(
                    $"luotsi replay graph --artifacts {Quote(artifactRoot)} --failed --write-json --write-markdown",
                    "Open semantic graph context for the focused failure.");
            }
        }

        if (previous is not null)
        {
            yield return new ReplayScrubCommandHint(
                BuildTimelineCommand(artifactRoot, previous, 2),
                "Move to the previous event in this scrub window.");
        }

        if (next is not null)
        {
            yield return new ReplayScrubCommandHint(
                BuildTimelineCommand(artifactRoot, next, 2),
                "Move to the next event in this scrub window.");
        }
    }

    private static string BuildMarkdown(ReplayScrubResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Replay Scrub");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{result.ArtifactRoot}`");
        builder.AppendLine($"Events: `{result.EventCount}`");
        builder.AppendLine($"Focus index: `{result.FocusIndex}`");
        builder.AppendLine();
        AppendEvent(builder, "Previous Event", result.PreviousEvent);
        AppendEvent(builder, "Focused Event", result.FocusEvent);
        AppendEvent(builder, "Next Event", result.NextEvent);
        builder.AppendLine("## Scrub Window");
        builder.AppendLine();
        builder.AppendLine("| # | Time | Type | Failure | Detail | Source |");
        builder.AppendLine("|---:|---|---|---|---|---|");
        foreach (var evt in result.Events)
        {
            builder.Append("| ");
            builder.Append(evt.Sequence.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(evt.Timestamp?.ToString("O") ?? string.Empty));
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(evt.Type));
            builder.Append(" | ");
            builder.Append(evt.FailureRelevant ? "yes" : "no");
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(evt.Detail));
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(evt.Path));
            builder.AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Commands");
        builder.AppendLine();
        foreach (var command in result.Commands)
        {
            builder.AppendLine($"- `{command.Command}`");
            builder.AppendLine($"  {command.Purpose}");
        }

        return builder.ToString();
    }

    private static void AppendEvent(StringBuilder builder, string heading, ReplayTimelineEventResult? evt)
    {
        builder.AppendLine("## " + heading);
        builder.AppendLine();
        if (evt is null)
        {
            builder.AppendLine("None");
            builder.AppendLine();
            return;
        }

        builder.AppendLine($"- Source: `{evt.Path}`");
        builder.AppendLine($"- Sequence: `{evt.Sequence}`");
        builder.AppendLine($"- Time: `{evt.Timestamp?.ToString("O") ?? string.Empty}`");
        builder.AppendLine($"- Type: `{evt.Type}`");
        builder.AppendLine($"- Failure: `{evt.FailureRelevant.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Detail: `{evt.Detail}`");
        builder.AppendLine($"- Reopen: `{BuildPortableTimelineCommand(evt, 5)}`");
        AppendProperties(builder, evt);
        builder.AppendLine();
    }

    private static void AppendProperties(StringBuilder builder, ReplayTimelineEventResult evt)
    {
        if (evt.Properties.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("| Property | Value |");
        builder.AppendLine("|---|---|");
        foreach (var property in evt.Properties.OrderBy(static property => property.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("| ");
            builder.Append(EscapeMarkdown(property.Key));
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(property.Value ?? string.Empty));
            builder.AppendLine(" |");
        }
    }

    private static string BuildTimelineCommand(string artifactRoot, ReplayTimelineEventResult evt, int context) =>
        $"luotsi replay timeline --artifacts {Quote(artifactRoot)} --source-path {Quote(evt.Path)} --sequence {evt.Sequence} --context {context.ToString(CultureInfo.InvariantCulture)}";

    private static string BuildPortableTimelineCommand(ReplayTimelineEventResult evt, int context) =>
        $"luotsi replay timeline --artifacts <artifact-root> --source-path {Quote(evt.Path)} --sequence {evt.Sequence} --context {context.ToString(CultureInfo.InvariantCulture)}";

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;

    private static string EscapeMarkdown(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
