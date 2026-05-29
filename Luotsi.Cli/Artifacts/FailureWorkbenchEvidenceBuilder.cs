using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Artifacts;

internal sealed class FailureWorkbenchEvidenceBuilder(string root, IFileSystem fileSystem)
{
    private readonly ArtifactEvidenceDetailReader _evidenceDetailReader = new(root, fileSystem);

    public IReadOnlyList<EvidenceGroupModel> BuildGroups(
        IReadOnlyList<string> files,
        IReadOnlyList<ReplayWorkflowCommandModel> workflowCommands,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario) =>
        BuildWorkbenchEvidenceGroups(files, workflowCommands, summary, scenario)
            .Select(group => new EvidenceGroupModel(
                group.Title,
                group.Summary,
                group.Kind,
                group.Items.Take(6).Select(static item => new EvidenceGroupItemModel(
                    item.Label,
                    item.Detail,
                    string.IsNullOrWhiteSpace(item.Path) ? null : ArtifactIndexPaths.EscapeHtmlLink(item.Path),
                    item.SupportingDetail,
                    item.IsCommand)).ToArray()))
            .ToArray();

    public static IReadOnlyList<EvidenceLinkModel> BuildLinks(SessionReplaySummary summary, FailureCapsuleScenario? scenario)
    {
        var evidence = new List<FailureCapsuleArtifactLink>();
        if (scenario is not null)
        {
            evidence.AddRange(scenario.Artifacts);
        }

        if (!string.IsNullOrWhiteSpace(summary.FailureCapsulePath))
        {
            evidence.Add(new FailureCapsuleArtifactLink("failure capsule", summary.FailureCapsulePath, null, null));
        }

        if (summary.HasTimeline)
        {
            evidence.Add(new FailureCapsuleArtifactLink("timeline", summary.TimelinePath, null, null));
        }

        evidence.Add(new FailureCapsuleArtifactLink("metadata", summary.MetadataPath, null, null));

        return evidence
            .Where(static item => !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Take(8)
            .Select(static (item, index) => new EvidenceLinkModel(
                item.Path,
                ArtifactIndexPaths.EscapeHtmlLink(item.Path),
                item.Kind,
                string.IsNullOrWhiteSpace(item.StepName) ? string.Empty : $" for {item.StepName}",
                index == 0))
            .ToArray();
    }

    private IReadOnlyList<WorkbenchEvidenceGroup> BuildWorkbenchEvidenceGroups(
        IReadOnlyList<string> files,
        IReadOnlyList<ReplayWorkflowCommandModel> workflowCommands,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario)
    {
        var groups = new List<WorkbenchEvidenceGroup>();
        AddFailureSignalGroup(groups, summary, scenario);
        AddDeviceAppFactsGroup(groups, summary);
        AddActionsCommandsGroup(groups, workflowCommands, summary);
        AddMediaReportsGroup(groups, files, summary, scenario);
        return groups;
    }

    private static void AddFailureSignalGroup(
        List<WorkbenchEvidenceGroup> groups,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario)
    {
        var items = new List<WorkbenchEvidenceItem>();
        if (!string.IsNullOrWhiteSpace(scenario?.Error?.Message))
        {
            var category = string.IsNullOrWhiteSpace(scenario.Error.Category) ? "failure" : scenario.Error.Category;
            items.Add(new WorkbenchEvidenceItem("Error", $"{category}: {scenario.Error.Message}", null));
        }

        if (scenario?.FailedStep is not null)
        {
            items.Add(new WorkbenchEvidenceItem("Failed step", $"{scenario.FailedStep.Name} ({scenario.FailedStep.Action})", null));
        }

        items.AddRange(summary.TimelineHighlights
            .Where(static entry => entry.IsFailureRelevant)
            .Select(static entry => new WorkbenchEvidenceItem(entry.Type, ArtifactTimelineFormatter.FormatDetail(entry), null)));

        if (items.Count == 0)
        {
            return;
        }

        groups.Add(new WorkbenchEvidenceGroup(
            "Failure signals",
            "Events and capsule fields that explain the primary failure shape.",
            "failure",
            DeduplicateEvidenceItems(items)));
    }

    private void AddDeviceAppFactsGroup(List<WorkbenchEvidenceGroup> groups, SessionReplaySummary summary)
    {
        var items = new List<WorkbenchEvidenceItem>
        {
            new("Session", ArtifactReplayFormatter.BuildTitle(summary), summary.MetadataPath, SupportingDetail: TryBuildEvidenceArtifactDetail(summary.MetadataPath)),
            new("Exit", $"reason={summary.Reason} | exit_code={summary.ExitCode}", null),
            new("Events", $"{summary.EventCount} events", summary.HasTimeline ? summary.TimelinePath : null, SupportingDetail: summary.HasTimeline ? TryBuildEvidenceArtifactDetail(summary.TimelinePath) : null)
        };

        if (!string.IsNullOrWhiteSpace(summary.Target))
        {
            items.Insert(0, new WorkbenchEvidenceItem("Target", summary.Target, null));
        }

        items.AddRange(summary.TimelineHighlights
            .Where(IsDeviceOrSessionFact)
            .Select(static entry => new WorkbenchEvidenceItem(entry.Type, ArtifactTimelineFormatter.FormatDetail(entry), null)));

        groups.Add(new WorkbenchEvidenceGroup(
            "Device and app facts",
            "The run target, session outcome, and health signals that frame the failure.",
            "context",
            DeduplicateEvidenceItems(items)));
    }

    private static void AddActionsCommandsGroup(
        List<WorkbenchEvidenceGroup> groups,
        IReadOnlyList<ReplayWorkflowCommandModel> workflowCommands,
        SessionReplaySummary summary)
    {
        var items = workflowCommands
            .Where(static command => string.Equals(command.Kind, "SCRUB", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command.Kind, "GRAPH", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command.Kind, "OPEN", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(static command => new WorkbenchEvidenceItem(command.Kind, command.Command, null, IsCommand: true))
            .ToList();

        items.AddRange(summary.TimelineHighlights
            .Where(IsActionOrCommandEvent)
            .Select(static entry => new WorkbenchEvidenceItem(entry.Type, ArtifactTimelineFormatter.FormatDetail(entry), null)));

        if (items.Count == 0)
        {
            items.Add(new WorkbenchEvidenceItem("Timeline", "No explicit action events were highlighted; scrub the failure window first.", summary.TimelinePath));
        }

        groups.Add(new WorkbenchEvidenceGroup(
            "Actions and commands",
            "The commands and interactions most likely to reproduce or narrow the issue.",
            "action",
            DeduplicateEvidenceItems(items)));
    }

    private void AddMediaReportsGroup(
        List<WorkbenchEvidenceGroup> groups,
        IReadOnlyList<string> files,
        SessionReplaySummary summary,
        FailureCapsuleScenario? scenario)
    {
        var items = new List<WorkbenchEvidenceItem>();
        if (scenario is not null)
        {
            items.AddRange(scenario.Artifacts
                .Where(static artifact => !string.IsNullOrWhiteSpace(artifact.Path))
                .Take(4)
                .Select(artifact => new WorkbenchEvidenceItem(artifact.Kind, artifact.Path, artifact.Path, SupportingDetail: TryBuildEvidenceArtifactDetail(artifact.Path))));
        }

        if (!string.IsNullOrWhiteSpace(summary.FailureCapsulePath))
        {
            items.Add(new WorkbenchEvidenceItem("failure capsule", summary.FailureCapsulePath, summary.FailureCapsulePath, SupportingDetail: TryBuildEvidenceArtifactDetail(summary.FailureCapsulePath)));
        }

        items.AddRange(files
            .Where(ArtifactClassifier.IsReport)
            .Take(Math.Max(0, 6 - items.Count))
            .Select(file => new WorkbenchEvidenceItem("report", file, file, SupportingDetail: TryBuildEvidenceArtifactDetail(file))));

        if (items.Count == 0)
        {
            return;
        }

        groups.Add(new WorkbenchEvidenceGroup(
            "Media and reports",
            "Screenshots, videos, logs, capsules, and machine-readable reports to open next.",
            "artifact",
            DeduplicateEvidenceItems(items)));
    }

    private static bool IsDeviceOrSessionFact(SessionReplayTimelineEntry entry) =>
        entry.Type.Contains("stats", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("started", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("ended", StringComparison.OrdinalIgnoreCase) ||
        entry.Detail.Contains("device=", StringComparison.OrdinalIgnoreCase) ||
        entry.Detail.Contains("target=", StringComparison.OrdinalIgnoreCase) ||
        entry.Detail.Contains("decoded_frames=", StringComparison.OrdinalIgnoreCase) ||
        entry.Detail.Contains("scenario=", StringComparison.OrdinalIgnoreCase);

    private static bool IsActionOrCommandEvent(SessionReplayTimelineEntry entry) =>
        entry.Type.Contains("action", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("command", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("tap", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("key", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("reconnect", StringComparison.OrdinalIgnoreCase) ||
        entry.Type.Contains("share_client", StringComparison.OrdinalIgnoreCase);

    private string? TryBuildEvidenceArtifactDetail(string path) => _evidenceDetailReader.TryBuild(path);

    private static IReadOnlyList<WorkbenchEvidenceItem> DeduplicateEvidenceItems(IReadOnlyList<WorkbenchEvidenceItem> items) =>
        items
            .Where(static item => !string.IsNullOrWhiteSpace(item.Detail))
            .GroupBy(static item => $"{item.Label}\n{item.Detail}\n{item.Path}\n{item.SupportingDetail}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
}
