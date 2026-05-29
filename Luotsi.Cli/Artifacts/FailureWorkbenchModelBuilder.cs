using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Artifacts;

internal sealed class FailureWorkbenchModelBuilder(string root, IFileSystem fileSystem)
{
    private readonly string _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly FailureWorkbenchEvidenceBuilder _evidenceBuilder = new(root, fileSystem);
    private readonly FailureWorkbenchSemanticSignalBuilder _semanticSignalBuilder = new(root, fileSystem);

    public FailureWorkbenchModel? Build(
        IReadOnlyList<string> files,
        IReadOnlyList<ReplayWorkflowCommandModel> workflowCommands,
        (SessionReplaySummary Summary, FailureCapsuleScenario? Scenario)? primaryFailure)
    {
        if (primaryFailure is null)
        {
            return null;
        }

        var summary = primaryFailure.Value.Summary;
        var scenario = primaryFailure.Value.Scenario;
        var step = scenario?.FailedStep;
        var error = scenario?.Error;
        var actionCommand = $"luotsi replay scrub --artifacts {ArtifactIndexPaths.Quote(_root)} --failures --context 3 --write-markdown";

        return new FailureWorkbenchModel(
            scenario is not null ? scenario.Scenario : ArtifactReplayFormatter.BuildTitle(summary),
            BuildFailureChips(summary),
            BuildFailureBrief(summary, scenario, actionCommand),
            [
                new MetaItemModel("Session", ArtifactReplayFormatter.BuildTitle(summary)),
                new MetaItemModel("Reason", summary.Reason),
                new MetaItemModel("Step", step?.Name ?? "unknown"),
                new MetaItemModel("Action", step?.Action ?? "unknown")
            ],
            BuildErrorMessage(error),
            actionCommand,
            BuildTimelineFilters(),
            summary.TimelineHighlights.Take(8).Select(ToTimelineEntry).ToArray(),
            FailureWorkbenchMediaBuilder.Build(files, summary, scenario),
            _evidenceBuilder.BuildGroups(files, workflowCommands, summary, scenario),
            FailureWorkbenchEvidenceBuilder.BuildLinks(summary, scenario),
            _semanticSignalBuilder.Build(),
            BuildTriageSteps(actionCommand, workflowCommands),
            workflowCommands.Take(4).ToArray());
    }

    public static (SessionReplaySummary Summary, FailureCapsuleScenario? Scenario)? SelectPrimaryFailure(
        IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        foreach (var summary in replaySummaries.Where(static item => item.HasFailureSignals))
        {
            var scenario = summary.FailureCapsule?.Scenarios.FirstOrDefault(static item =>
                string.Equals(item.Status, "failed", StringComparison.OrdinalIgnoreCase) ||
                item.Error is not null ||
                item.FailedStep is not null);
            return (summary, scenario);
        }

        return null;
    }

    private static IReadOnlyList<ChipModel> BuildFailureChips(SessionReplaySummary summary)
    {
        var chips = new List<ChipModel>
        {
            new("needs triage", "chip chip-danger"),
            new(summary.SessionKind),
            new($"{summary.EventCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} events")
        };

        if (!string.IsNullOrWhiteSpace(summary.Target))
        {
            chips.Add(new ChipModel(summary.Target));
        }

        return chips;
    }

    private static IReadOnlyList<BriefCardModel> BuildFailureBrief(
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario,
        string actionCommand) =>
        [
            new BriefCardModel(
                "What failed",
                scenario?.Error?.Message ?? summary.TimelineHighlights.FirstOrDefault(static entry => entry.IsFailureRelevant)?.Detail ?? summary.Reason),
            new BriefCardModel("What changed", BuildWhatChanged(summary)),
            new BriefCardModel("Run next", actionCommand)
        ];

    private static string BuildWhatChanged(SessionReplaySummary summary)
    {
        var failureIndex = summary.TimelineHighlights
            .Select(static (entry, index) => new { Entry = entry, Index = index })
            .FirstOrDefault(static item => item.Entry.IsFailureRelevant)
            ?.Index;
        if (failureIndex is > 0)
        {
            return ArtifactTimelineFormatter.FormatEntry(summary.TimelineHighlights[failureIndex.Value - 1]);
        }

        var firstSignal = summary.TimelineHighlights.FirstOrDefault(static entry =>
            entry.Type.Contains("diagnostic", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("stats", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("activity", StringComparison.OrdinalIgnoreCase) ||
            entry.Type.Contains("screen", StringComparison.OrdinalIgnoreCase));
        return firstSignal is null ? "No pre-failure change signal was captured." : ArtifactTimelineFormatter.FormatEntry(firstSignal);
    }

    private static FailureMessageModel? BuildErrorMessage(ErrorInfo? error) =>
        string.IsNullOrWhiteSpace(error?.Message)
            ? null
            : new FailureMessageModel(string.IsNullOrWhiteSpace(error.Category) ? "failure" : error.Category, error.Message);

    private static IReadOnlyList<FilterChipModel> BuildTimelineFilters() =>
        [
            new FilterChipModel("Failures", "failure"),
            new FilterChipModel("Actions", "action"),
            new FilterChipModel("Screens", "screen"),
            new FilterChipModel("Telemetry", "telemetry")
        ];

    private static IReadOnlyList<TriageStepModel> BuildTriageSteps(
        string actionCommand,
        IReadOnlyList<ReplayWorkflowCommandModel> workflowCommands)
    {
        var graphCommand = workflowCommands.FirstOrDefault(static command => string.Equals(command.Kind, "GRAPH", StringComparison.OrdinalIgnoreCase))?.Command;
        var clusterCommand = workflowCommands.FirstOrDefault(static command => string.Equals(command.Kind, "CLUSTER", StringComparison.OrdinalIgnoreCase))?.Command;
        return
        [
            new TriageStepModel(1, "Replay the failure window", "Start with the narrowest failing moment and adjacent events.", actionCommand),
            new TriageStepModel(2, "Read semantic signals", "Use graph facts and hypotheses to separate app, device, and transport causes.", graphCommand),
            new TriageStepModel(3, "Check recurrence", "Compare sibling bundles before treating the failure as unique.", clusterCommand)
        ];
    }

    private static TimelineEntryModel ToTimelineEntry(SessionReplayTimelineEntry entry) =>
        new(
            entry.Type,
            ArtifactTimelineFormatter.FormatDetail(entry),
            ArtifactTimelineFormatter.FormatEntry(entry),
            ArtifactTimelineFormatter.BuildFilterTags(entry),
            entry.IsFailureRelevant);
}
