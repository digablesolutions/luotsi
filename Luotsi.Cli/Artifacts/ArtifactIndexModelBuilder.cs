using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Artifacts;

internal sealed class ArtifactIndexModelBuilder(string root, IFileSystem fileSystem)
{
    private readonly string _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly ArtifactSummaryBuilder _summaryBuilder = new(root, fileSystem);
    private readonly ArtifactReplayWorkflowCommands _workflowCommands = new(root);
    private readonly FailureWorkbenchModelBuilder _workbenchBuilder = new(root, fileSystem);

    public async Task<ArtifactIndexModel> BuildAsync(
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(replaySummaries);

        var primaryFailure = FailureWorkbenchModelBuilder.SelectPrimaryFailure(replaySummaries);
        var hasFailureWorkbench = primaryFailure is not null;
        var pageTitle = hasFailureWorkbench ? "Luotsi Failure Workbench" : "Luotsi Artifacts";
        var pageEyebrow = hasFailureWorkbench ? "Replay triage" : "Replay artifacts";
        var pageHeading = hasFailureWorkbench ? "Failure Workbench" : "Luotsi Artifacts";
        var workflowCommands = _workflowCommands.Build(replaySummaries);

        return new ArtifactIndexModel(
            _root,
            pageTitle,
            pageEyebrow,
            pageHeading,
            BuildHeader(pageEyebrow, pageHeading, files, replaySummaries, primaryFailure),
            _workbenchBuilder.Build(files, workflowCommands, primaryFailure),
            BuildReplaySessions(replaySummaries),
            workflowCommands,
            await BuildArtifactSectionsAsync(files).ConfigureAwait(false));
    }

    private ArtifactIndexHeaderModel BuildHeader(
        string pageEyebrow,
        string pageHeading,
        IReadOnlyList<string> files,
        IReadOnlyList<SessionReplaySummary> replaySummaries,
        (SessionReplaySummary Summary, FailureCapsuleScenario? Scenario)? primaryFailure)
    {
        var summary = primaryFailure?.Summary;
        var scenario = primaryFailure?.Scenario;
        var step = scenario?.FailedStep;
        var heading = scenario is not null ? scenario.Scenario : pageHeading;

        return new ArtifactIndexHeaderModel(
            $"Luotsi / {pageEyebrow}",
            pageEyebrow,
            heading,
            _root,
            BuildSubtitleItems(summary, step),
            [
                new MetricModel(replaySummaries.Count(static item => item.HasFailureSignals), "Failure signals"),
                new MetricModel(replaySummaries.Count, "Sessions"),
                new MetricModel(replaySummaries.Sum(static item => item.EventCount), "Events")
            ],
            [
                new MetricModel(replaySummaries.Count, "Replay sessions"),
                new MetricModel(replaySummaries.Count(static summary => summary.HasFailureSignals), "Failure signals"),
                new MetricModel(files.Count, "Artifacts"),
                new MetricModel(files.Count(ArtifactClassifier.IsReport), "Reports")
            ]);
    }

    private IReadOnlyList<ChipModel> BuildSubtitleItems(SessionReplaySummary? summary, FailureCapsuleFailedStep? step)
    {
        if (summary is null)
        {
            return [new ChipModel(_root)];
        }

        var items = new List<ChipModel>
        {
            new("Unhandled", "chip chip-danger"),
            new(summary.Reason)
        };
        if (!string.IsNullOrWhiteSpace(step?.Name))
        {
            items.Add(new ChipModel(step.Name));
        }

        if (!string.IsNullOrWhiteSpace(summary.Target))
        {
            items.Add(new ChipModel(summary.Target));
        }

        return items;
    }

    private async Task<IReadOnlyList<ArtifactSectionModel>> BuildArtifactSectionsAsync(IReadOnlyList<string> files)
    {
        var sections = new List<ArtifactSectionModel>();
        var isFirst = true;
        foreach (var group in files.GroupBy(ArtifactClassifier.GetCategory))
        {
            var items = new List<ArtifactItemModel>();
            foreach (var file in group)
            {
                items.Add(new ArtifactItemModel(
                    file,
                    ArtifactIndexPaths.EscapeHtmlLink(file),
                    ArtifactClassifier.GetKind(file),
                    await _summaryBuilder.TryBuildAsync(file).ConfigureAwait(false)));
            }

            sections.Add(new ArtifactSectionModel(group.Key, isFirst ? "artifacts" : null, items));
            isFirst = false;
        }

        return sections;
    }

    private static IReadOnlyList<ReplaySessionIndexModel> BuildReplaySessions(IReadOnlyList<SessionReplaySummary> replaySummaries) =>
        replaySummaries
            .Select(summary =>
            {
                var links = new List<LinkModel>
                {
                    new("metadata", ArtifactIndexPaths.EscapeHtmlLink(summary.MetadataPath))
                };
                if (summary.HasTimeline)
                {
                    links.Add(new LinkModel("timeline", ArtifactIndexPaths.EscapeHtmlLink(summary.TimelinePath)));
                }

                if (!string.IsNullOrWhiteSpace(summary.FailureCapsulePath))
                {
                    links.Add(new LinkModel("failure capsule", ArtifactIndexPaths.EscapeHtmlLink(summary.FailureCapsulePath)));
                }

                return new ReplaySessionIndexModel(
                    ArtifactReplayFormatter.BuildTitle(summary),
                    ArtifactReplayFormatter.BuildOutcome(summary),
                    links,
                    summary.HasFailureSignals ? "Failure timeline" : "Session timeline",
                    summary.TimelineHighlights.Select(ToTimelineEntry).ToArray());
            })
            .ToArray();

    private static TimelineEntryModel ToTimelineEntry(SessionReplayTimelineEntry entry) =>
        new(
            entry.Type,
            ArtifactTimelineFormatter.FormatDetail(entry),
            ArtifactTimelineFormatter.FormatEntry(entry),
            ArtifactTimelineFormatter.BuildFilterTags(entry),
            entry.IsFailureRelevant);
}
