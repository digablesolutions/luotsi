using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal sealed record ScenarioRunReport(
    string Schema,
    string Path,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double DurationMs,
    int TotalCount,
    int MatchedCount,
    int SelectedCount,
    int PassedCount,
    int FailedCount,
    int ShardedOutCount,
    int? ShardCount,
    int? ShardIndex,
    string? ShardStrategy,
    IReadOnlyDictionary<string, double> Metrics,
    ScenarioDeviceAllocation? DeviceAllocation,
    BuildProvenance Provenance,
    IReadOnlyList<ScenarioReportScenario> Scenarios,
    ScenarioGovernanceVerdict? Governance = null,
    ScenarioDeviceHealthSnapshot? DeviceHealth = null,
    ScenarioCiPolicyResult? CiPolicy = null,
    string? ProgressMode = null,
    ErrorInfo? Error = null);

internal sealed record ScenarioReportScenario(
    string Scenario,
    string? ScenarioId,
    string Status,
    string? File,
    double? DurationMs,
    ScenarioRunTiming? Timing,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyList<ScenarioStepResult> Steps,
    ScenarioFailedStepResult? FailedStep,
    IReadOnlyList<ScenarioReportArtifact> Artifacts,
    ErrorInfo? Error,
    ScenarioMetadata? Metadata = null,
    IReadOnlyList<ScenarioMetadataWarning>? MetadataWarnings = null,
    ScenarioGovernanceVerdict? Governance = null,
    ScenarioDeviceHealthSnapshot? DeviceHealth = null,
    ScenarioCiPolicyResult? CiPolicy = null);

internal sealed record ScenarioReportArtifact(string Kind, string FileName, int? StepIndex, string? StepName);
