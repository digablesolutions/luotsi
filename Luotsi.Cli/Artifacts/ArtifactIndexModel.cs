namespace Luotsi.Cli.Artifacts;

internal sealed record ArtifactIndexModel(
    string Root,
    string PageTitle,
    string PageEyebrow,
    string PageHeading,
    ArtifactIndexHeaderModel Header,
    FailureWorkbenchModel? FailureWorkbench,
    IReadOnlyList<ReplaySessionIndexModel> ReplaySessions,
    IReadOnlyList<ReplayWorkflowCommandModel> ReplayWorkflowCommands,
    IReadOnlyList<ArtifactSectionModel> ArtifactSections)
{
    public bool HasFailureWorkbench => FailureWorkbench is not null;
    public bool HasReplaySessions => ReplaySessions.Count > 0;
    public bool HasReplayWorkflowCommands => ReplayWorkflowCommands.Count > 0;
    public bool HasArtifacts => ArtifactSections.Any(static section => section.Items.Count > 0);
}

internal sealed record ArtifactIndexHeaderModel(
    string Breadcrumbs,
    string Eyebrow,
    string Heading,
    string Root,
    IReadOnlyList<ChipModel> SubtitleItems,
    IReadOnlyList<MetricModel> Metrics,
    IReadOnlyList<MetricModel> Stats);

internal sealed record MetricModel(int Value, string Label);

internal sealed record ChipModel(string Text, string CssClass = "chip");

internal sealed record ArtifactSectionModel(
    string Title,
    string? SectionId,
    IReadOnlyList<ArtifactItemModel> Items);

internal sealed record ArtifactItemModel(
    string Path,
    string Href,
    string Kind,
    string? Summary)
{
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
}

internal sealed record ReplaySessionIndexModel(
    string Title,
    string Outcome,
    IReadOnlyList<LinkModel> Links,
    string TimelineLabel,
    IReadOnlyList<TimelineEntryModel> TimelineEntries)
{
    public bool HasTimelineEntries => TimelineEntries.Count > 0;
}

internal sealed record ReplayWorkflowCommandModel(
    string Kind,
    string Command,
    string Purpose);

internal sealed record FailureWorkbenchModel(
    string Title,
    IReadOnlyList<ChipModel> Chips,
    IReadOnlyList<BriefCardModel> BriefCards,
    IReadOnlyList<MetaItemModel> MetaItems,
    FailureMessageModel? Error,
    string ActionCommand,
    IReadOnlyList<FilterChipModel> TimelineFilters,
    IReadOnlyList<TimelineEntryModel> TimelineEntries,
    IReadOnlyList<MediaPreviewModel> Media,
    IReadOnlyList<EvidenceGroupModel> EvidenceGroups,
    IReadOnlyList<EvidenceLinkModel> EvidenceLinks,
    SemanticSignalsModel? SemanticSignals,
    IReadOnlyList<TriageStepModel> TriageSteps,
    IReadOnlyList<ReplayWorkflowCommandModel> ReplayActions)
{
    public bool HasError => Error is not null;
    public bool HasTimelineEntries => TimelineEntries.Count > 0;
    public bool HasMedia => Media.Count > 0;
    public bool HasEvidenceGroups => EvidenceGroups.Count > 0;
    public bool HasSemanticSignals => SemanticSignals is not null;
}

internal sealed record BriefCardModel(string Label, string Value);

internal sealed record MetaItemModel(string Label, string Value);

internal sealed record FailureMessageModel(string Category, string Message);

internal sealed record FilterChipModel(string Label, string Query);

internal sealed record TimelineEntryModel(
    string Type,
    string Detail,
    string FullText,
    string Tags,
    bool IsFailureRelevant);

internal sealed record MediaPreviewModel(
    string Path,
    string Href,
    string Kind,
    string FileName,
    bool IsImage,
    bool IsVideo);

internal sealed record EvidenceGroupModel(
    string Title,
    string Summary,
    string Kind,
    IReadOnlyList<EvidenceGroupItemModel> Items);

internal sealed record EvidenceGroupItemModel(
    string Label,
    string Detail,
    string? Href,
    string? SupportingDetail,
    bool IsCommand)
{
    public bool HasHref => !string.IsNullOrWhiteSpace(Href);
    public bool HasSupportingDetail => !string.IsNullOrWhiteSpace(SupportingDetail);
}

internal sealed record EvidenceLinkModel(
    string Path,
    string Href,
    string Kind,
    string StepSuffix,
    bool IsPrimary);

internal sealed record SemanticSignalsModel(
    string GraphHref,
    IReadOnlyList<SemanticSignalItemModel> Items);

internal sealed record SemanticSignalItemModel(
    string Kind,
    string Text,
    string? Command)
{
    public bool HasCommand => !string.IsNullOrWhiteSpace(Command);
}

internal sealed record TriageStepModel(
    int Number,
    string Title,
    string Description,
    string? Command)
{
    public bool HasCommand => !string.IsNullOrWhiteSpace(Command);
}

internal sealed record LinkModel(string Label, string Href);

internal sealed record WorkbenchEvidenceGroup(
    string Title,
    string Summary,
    string Kind,
    IReadOnlyList<WorkbenchEvidenceItem> Items);

internal sealed record WorkbenchEvidenceItem(
    string Label,
    string Detail,
    string? Path,
    string? SupportingDetail = null,
    bool IsCommand = false);
