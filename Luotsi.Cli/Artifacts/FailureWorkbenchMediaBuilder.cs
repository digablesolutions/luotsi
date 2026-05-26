namespace Luotsi.Cli.Artifacts;

internal static class FailureWorkbenchMediaBuilder
{
    public static IReadOnlyList<MediaPreviewModel> Build(
        IReadOnlyList<string> files,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario) =>
        CollectPreviewArtifacts(files, summary, scenario)
            .Take(6)
            .Select(static item => new MediaPreviewModel(
                item.Path,
                ArtifactIndexPaths.EscapeHtmlLink(item.Path),
                item.Kind,
                Path.GetFileName(item.Path),
                ArtifactClassifier.IsImage(item.Path),
                ArtifactClassifier.IsBrowserVideo(item.Path)))
            .ToArray();

    private static IEnumerable<FailureCapsuleArtifactLink> CollectPreviewArtifacts(
        IReadOnlyList<string> files,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in EnumeratePreviewLinks(summary, scenario)
            .Where(static item => ArtifactClassifier.IsPreview(item.Path))
            .Where(item => seen.Add(item.Path)))
        {
            yield return item;
        }

        foreach (var file in files.Where(ArtifactClassifier.IsPreview).Where(file => seen.Add(file)))
        {
            yield return new FailureCapsuleArtifactLink(ArtifactClassifier.GetCategory(file).TrimEnd('s').ToLowerInvariant(), file, null, null);
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
}
