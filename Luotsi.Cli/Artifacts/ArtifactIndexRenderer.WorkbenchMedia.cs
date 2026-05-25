using System.Text;

namespace Luotsi.Cli.Artifacts;

internal sealed partial class ArtifactIndexRenderer
{
    private static void AppendMediaPreviewHtml(
        StringBuilder builder,
        IReadOnlyList<string> files,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario)
    {
        var media = CollectPreviewArtifacts(files, summary, scenario).Take(6).ToArray();
        if (media.Length == 0)
        {
            builder.AppendLine("          <div class=\"media-empty root\">No screenshot or video evidence was linked to the primary failure.</div>");
            return;
        }

        builder.AppendLine("          <div class=\"media-grid\" aria-label=\"Visual evidence preview\">");
        foreach (var item in media)
        {
            builder.AppendLine("            <a class=\"media-tile\" data-filter-item href=\"" + HtmlAttributeEncode(EscapeHtmlLink(item.Path)) + "\">");
            if (IsImageArtifact(item.Path))
            {
                builder.AppendLine($"              <img src=\"{HtmlAttributeEncode(EscapeHtmlLink(item.Path))}\" alt=\"{HtmlAttributeEncode(item.Kind)} preview\">");
            }
            else if (IsBrowserVideoArtifact(item.Path))
            {
                builder.AppendLine($"              <video src=\"{HtmlAttributeEncode(EscapeHtmlLink(item.Path))}\" muted preload=\"metadata\"></video>");
            }
            else
            {
                builder.AppendLine("              <div class=\"media-placeholder\">Open</div>");
            }

            builder.AppendLine("              <span>" + HtmlEncode(item.Kind) + "</span>");
            builder.AppendLine("              <strong>" + HtmlEncode(Path.GetFileName(item.Path)) + "</strong>");
            builder.AppendLine("            </a>");
        }

        builder.AppendLine("          </div>");
    }

    private static IEnumerable<FailureCapsuleArtifactLink> CollectPreviewArtifacts(
        IReadOnlyList<string> files,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in EnumeratePreviewLinks(summary, scenario).Where(static item => IsPreviewArtifact(item.Path)))
        {
            if (seen.Add(item.Path))
            {
                yield return item;
            }
        }

        foreach (var file in files.Where(IsPreviewArtifact).Where(file => seen.Add(file)))
        {
            yield return new FailureCapsuleArtifactLink(GetArtifactCategory(file).TrimEnd('s').ToLowerInvariant(), file, null, null);
        }
    }

    private static IEnumerable<FailureCapsuleArtifactLink> EnumeratePreviewLinks(
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario)
    {
        if (scenario is not null)
        {
            foreach (var item in scenario.Artifacts)
            {
                yield return item;
            }
        }

        if (summary.FailureCapsule is null)
        {
            yield break;
        }

        foreach (var item in summary.FailureCapsule.Screenshots)
        {
            yield return item;
        }

        foreach (var bundle in summary.FailureCapsule.FailureBundles)
        {
            foreach (var item in bundle.Artifacts)
            {
                yield return item;
            }
        }
    }

    private static bool IsPreviewArtifact(string path) =>
        IsImageArtifact(path) || IsBrowserVideoArtifact(path) || string.Equals(Path.GetExtension(path), ".h264", StringComparison.OrdinalIgnoreCase);

    private static bool IsImageArtifact(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBrowserVideoArtifact(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".webm", StringComparison.OrdinalIgnoreCase);
    }
}
