using System.Text;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Artifacts;

internal sealed partial class ArtifactIndexRenderer(string root, IFileSystem fileSystem)
{
    private readonly string _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ArtifactEvidenceDetailReader _evidenceDetailReader = new(root, fileSystem);

    public async Task<string> BuildMarkdownIndexAsync(IReadOnlyList<string> files)
    {
        var replaySummaries = new SessionReplaySummaryReader(_root, _fileSystem).ReadSummaries(files);
        return await BuildMarkdownIndexAsync(files, replaySummaries).ConfigureAwait(false);
    }

    public async Task<string> BuildMarkdownIndexAsync(
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Artifacts");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{_root}`");
        builder.AppendLine();
        if (files.Count == 0)
        {
            builder.AppendLine("No artifacts have been written yet.");
            return builder.ToString();
        }

        AppendReplaySessionsMarkdown(builder, replaySummaries);
        AppendReplayWorkflowMarkdown(builder, replaySummaries);

        foreach (var group in files.GroupBy(GetArtifactCategory))
        {
            builder.AppendLine($"## {group.Key}");
            builder.AppendLine();
            foreach (var file in group)
            {
                builder.AppendLine($"- [{file}]({EscapeMarkdownLink(file)})");
                var summary = await TryBuildArtifactSummaryAsync(file).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    builder.AppendLine($"  - {summary}");
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    public async Task<string> BuildHtmlIndexAsync(IReadOnlyList<string> files)
    {
        var replaySummaries = new SessionReplaySummaryReader(_root, _fileSystem).ReadSummaries(files);
        return await BuildHtmlIndexAsync(files, replaySummaries).ConfigureAwait(false);
    }

    public async Task<string> BuildHtmlIndexAsync(
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        var hasFailureWorkbench = replaySummaries.Any(static summary => summary.HasFailureSignals);
        var primaryFailure = SelectPrimaryFailure(replaySummaries);
        var pageTitle = hasFailureWorkbench ? "Luotsi Failure Workbench" : "Luotsi Artifacts";
        var pageEyebrow = hasFailureWorkbench ? "Replay triage" : "Replay artifacts";
        var pageHeading = hasFailureWorkbench ? "Failure Workbench" : "Luotsi Artifacts";
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\">");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine($"  <title>{HtmlEncode(pageTitle)}</title>");
        builder.AppendLine("  <style>");
        builder.Append(ArtifactIndexTheme.Css);
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine(hasFailureWorkbench ? "<body class=\"has-workbench\">" : "<body>");
        if (hasFailureWorkbench)
        {
            AppendAppRailHtml(builder);
        }

        builder.AppendLine("  <main>");
        builder.AppendLine("    <header>");
        AppendWorkbenchHeaderHtml(builder, pageEyebrow, pageHeading, replaySummaries, primaryFailure);
        AppendHeaderStatsHtml(builder, files, replaySummaries);
        AppendToolbarHtml(builder, replaySummaries);
        builder.AppendLine("    </header>");

        if (files.Count == 0)
        {
            builder.AppendLine("    <section><h2>Artifacts</h2><div class=\"empty\">No artifacts have been written yet.</div></section>");
        }
        else
        {
            AppendFailureWorkbenchHtml(builder, files, replaySummaries);
            AppendReplaySessionsHtml(builder, replaySummaries);
            AppendReplayWorkflowHtml(builder, replaySummaries);

            var isFirstArtifactGroup = true;
            foreach (var group in files.GroupBy(GetArtifactCategory))
            {
                builder.AppendLine(isFirstArtifactGroup ? "    <section id=\"artifacts\">" : "    <section>");
                isFirstArtifactGroup = false;
                builder.AppendLine($"      <h2>{HtmlEncode(group.Key)}</h2>");
                builder.AppendLine("      <ul>");
                foreach (var file in group)
                {
                    builder.AppendLine("        <li>");
                    builder.AppendLine("          <div>");
                    builder.AppendLine($"            <a href=\"{HtmlAttributeEncode(EscapeHtmlLink(file))}\">{HtmlEncode(file)}</a>");
                    var summary = await TryBuildArtifactSummaryAsync(file).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        builder.AppendLine($"            <div class=\"root\">{HtmlEncode(summary)}</div>");
                    }

                    builder.AppendLine("          </div>");
                    builder.AppendLine($"          <span class=\"kind\">{HtmlEncode(GetArtifactKind(file))}</span>");
                    builder.AppendLine("        </li>");
                }

                builder.AppendLine("      </ul>");
                builder.AppendLine("    </section>");
            }
        }

        builder.AppendLine("  </main>");
        AppendIndexScriptHtml(builder);
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

}
