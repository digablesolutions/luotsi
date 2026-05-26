using System.Text;

namespace Luotsi.Cli.Artifacts;

internal sealed class ArtifactMarkdownIndexRenderer
{
    public string Render(ArtifactIndexModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Artifacts");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{model.Root}`");
        builder.AppendLine();
        if (!model.HasArtifacts)
        {
            builder.AppendLine("No artifacts have been written yet.");
            return builder.ToString();
        }

        AppendReplaySessions(builder, model.ReplaySessions);
        AppendReplayWorkflow(builder, model.ReplayWorkflowCommands);
        AppendArtifactSections(builder, model.ArtifactSections);
        return builder.ToString();
    }

    private static void AppendReplaySessions(StringBuilder builder, IReadOnlyList<ReplaySessionIndexModel> replaySessions)
    {
        if (replaySessions.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Replay Sessions");
        builder.AppendLine();
        foreach (var summary in replaySessions)
        {
            builder.AppendLine($"### {summary.Title}");
            builder.AppendLine();
            builder.Append($"- {summary.Outcome}");
            foreach (var link in summary.Links)
            {
                builder.Append($" | [{link.Label}]({ArtifactIndexPaths.EscapeMarkdownLink(link.Href)})");
            }

            builder.AppendLine();
            if (summary.TimelineEntries.Count > 0)
            {
                builder.AppendLine(summary.TimelineLabel == "Failure timeline" ? "- Failure timeline:" : "- Session timeline:");
                foreach (var entry in summary.TimelineEntries)
                {
                    builder.AppendLine($"  - {entry.FullText}");
                }
            }

            builder.AppendLine();
        }
    }

    private static void AppendReplayWorkflow(StringBuilder builder, IReadOnlyList<ReplayWorkflowCommandModel> replayWorkflowCommands)
    {
        if (replayWorkflowCommands.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Replay Front Door");
        builder.AppendLine();
        foreach (var command in replayWorkflowCommands)
        {
            builder.AppendLine($"- `{command.Command}`");
            builder.AppendLine($"  - {command.Purpose}");
        }

        builder.AppendLine();
    }

    private static void AppendArtifactSections(StringBuilder builder, IReadOnlyList<ArtifactSectionModel> artifactSections)
    {
        foreach (var section in artifactSections)
        {
            builder.AppendLine($"## {section.Title}");
            builder.AppendLine();
            foreach (var file in section.Items)
            {
                builder.AppendLine($"- [{file.Path}]({ArtifactIndexPaths.EscapeMarkdownLink(file.Path)})");
                if (!string.IsNullOrWhiteSpace(file.Summary))
                {
                    builder.AppendLine($"  - {file.Summary}");
                }
            }

            builder.AppendLine();
        }
    }
}
