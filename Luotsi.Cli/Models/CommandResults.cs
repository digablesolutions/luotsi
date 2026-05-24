using System.Text.Json;
using Luotsi.Cli.Telemetry;

namespace Luotsi.Cli.Models;

// Device listing
public sealed record DeviceInfo(string? Serial, string? Status, string Details);

public sealed record DeviceListResult(IReadOnlyList<DeviceInfo> Devices);

public sealed record DeviceInventoryResult(IReadOnlyList<DeviceState> Devices);

public sealed record DeviceStatusResult(DeviceState Device, PreflightResult Readiness);

public sealed record LabDeviceDecision(string? Serial, string Status, string Reason, bool Selected, IReadOnlyList<string>? Capabilities = null);

public sealed record LabStatusResult(
    int Total,
    int Available,
    int Unavailable,
    IReadOnlyList<DeviceState> Devices,
    IReadOnlyList<LabDeviceDecision> Decisions);

public sealed record LabDoctorResult(
    string Status,
    LabStatusResult Inventory,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> RecommendedActions,
    IReadOnlyList<string>? AppliedFixes = null,
    IReadOnlyList<LabDoctorProbe>? Probes = null);

public sealed record LabDoctorProbe(string Name, bool Succeeded, int ExitCode, string Invocation);

public sealed record LabPlanResult(
    string Status,
    string? Query,
    string? SelectedSerial,
    string Summary,
    IReadOnlyList<string> RecommendedCommands,
    IReadOnlyList<LabDeviceDecision> Decisions);

public sealed record LabLeaseResult(
    string LeaseId,
    string Serial,
    string Owner,
    DateTimeOffset ClaimedAt,
    DateTimeOffset ExpiresAt,
    string LeaseFile);

public sealed record LabLeaseReleaseResult(
    string LeaseId,
    bool Released,
    string? LeaseFile,
    string? Serial = null);

public sealed record LabLeaseExtendResult(
    string LeaseId,
    string Serial,
    bool Extended,
    DateTimeOffset? PreviousExpiresAt,
    DateTimeOffset? ExpiresAt,
    string? LeaseFile);

public sealed record LabLeasesResult(
    int Count,
    IReadOnlyList<LabLeaseResult> Leases);

public sealed record LabQuarantineResult(
    string Serial,
    string Reason,
    string Owner,
    DateTimeOffset QuarantinedAt,
    string QuarantineFile);

public sealed record LabQuarantineReleaseResult(
    string Serial,
    bool Released,
    string? QuarantineFile);

public sealed record LabQuarantinesResult(
    int Count,
    IReadOnlyList<LabQuarantineResult> Quarantines);

public sealed record DeviceState(
    string? Serial,
    string State,
    string Transport,
    string Type,
    string? Model,
    string? Product,
    string? Device,
    string Details,
    string Availability,
    string? RecommendedFix);

public sealed record ScenarioDeviceAllocation(
    string Status,
    string? Serial,
    DeviceState? Device,
    PreflightResult? Readiness,
    bool RequireReady,
    int WaitTimeoutSec,
    string? Package = null,
    LabLeaseResult? Lease = null);

// Preflight
public sealed record PreflightResult(
    string Model,
    string AndroidRelease,
    string Sdk,
    string CurrentFocus,
    string? Package,
    string? PackageInfo,
    string Fingerprint,
    string Abi,
    string Serial,
    string? ForegroundPackage = null,
    int? DisplayWidth = null,
    int? DisplayHeight = null,
    string? DisplayOrientation = null);

// Tap
public sealed record TapResult(int X, int Y);

// Type text
public sealed record TypeTextResult(string Text);

// Key event
public sealed record KeyEventResult(string Code);

// Wait for log
public sealed record WaitLogResult(string Contains, int TimeoutSec, string MatchedLine, int LineCount);

// Logcat
public sealed record LogcatResult(string[] Lines);

// Telemetry (shared between tail and watch)
public sealed record TelemetryResult(
    int InspectedLineCount,
    int TelemetryLineCount,
    int EventCount,
    int ParseErrorCount,
    IReadOnlyList<TelemetryEvent> Events,
    IReadOnlyList<TelemetryParseError> ParseErrors);

// Wait for step / action ready
public sealed record TelemetryMatchResult(
    string? Step,
    string? Action,
    string Line,
    string EventName,
    JsonElement Payload);

// Record video
public sealed record RecordResult(string Output, int TimeLimitSec);

// Scroll gesture
public sealed record ScrollResult(int HorizontalTicks, int VerticalTicks, int StartX, int StartY, int EndX, int EndY, int DurationMs);

// Push file to device
public sealed record PushFileResult(string LocalPath, string RemotePath);

// Pull file from device
public sealed record PullFileResult(string RemotePath, string LocalPath);

// Install package on device
public sealed record InstallPackageResult(string PackagePath);

// ADB port forwarding
public sealed record PortForwardEntry(string? Serial, string Local, string Remote);

public sealed record PortForwardListResult(IReadOnlyList<PortForwardEntry> Entries);

public sealed record PortForwardResult(string Local, string Remote, bool NoRebind);

public sealed record PortForwardRemoveResult(string Local);

public sealed record PortReverseEntry(string? Serial, string Remote, string Local);

public sealed record PortReverseListResult(IReadOnlyList<PortReverseEntry> Entries);

public sealed record PortReverseResult(string Remote, string Local, bool NoRebind);

public sealed record PortReverseRemoveResult(string Remote);

// App lifecycle and package state
public sealed record StartAppResult(string Package, string? Activity, string? Component, bool Wait, string Output);

public sealed record StartUriResult(string Uri, string? Package, string? Activity, string? Component, string Action, bool Wait, string Output);

public sealed record AppPackageCommandResult(string Package);

public sealed record ActivityWaitResult(string Activity, int TimeoutSec, string CurrentActivity, int AttemptCount);

public sealed record AppInstalledResult(string Package, bool Installed);

public sealed record InstalledPackageListResult(IReadOnlyList<string> Packages, bool ThirdPartyOnly);

public sealed record PermissionCommandResult(string Package, string Permission);

// Enable adb-over-TCP and connect to the target endpoint.
public sealed record WirelessConnectResult(string Host, int Port, string Endpoint);

public sealed record WirelessMdnsService(
    string ServiceName,
    string ServiceType,
    string Host,
    int Port,
    string Endpoint,
    string Selector,
    string Kind);

public sealed record WirelessScanResult(
    IReadOnlyList<WirelessMdnsService> Services,
    IReadOnlyList<WirelessMdnsService> PairingServices,
    IReadOnlyList<WirelessMdnsService> ConnectServices,
    IReadOnlyList<WirelessMdnsService> LegacyServices);

public sealed record WirelessPairResult(
    string Endpoint,
    string? ServiceName,
    string? ServiceType,
    string? Selector,
    bool Paired,
    bool InteractiveRequired,
    string Message,
    string? Stdout);

public sealed record WirelessMdnsConnectResult(
    string Endpoint,
    string? ServiceName,
    string? ServiceType,
    string? Selector,
    string ConnectTarget,
    string DeviceSelector,
    bool Connected,
    string Message,
    string? Stdout);

public sealed record AdbCommandOutput(
    string Invocation,
    IReadOnlyList<string> Args,
    int ExitCode,
    bool Succeeded,
    string Stdout,
    string Stderr,
    int AttemptCount,
    string? RetryReason,
    IReadOnlyList<AdbRecoveryActionResult> RecoveryActions);

public sealed record AdbDiagnosticResult(string Schema, string Name, AdbCommandOutput Command);

public sealed record AdbReadinessResult(
    string Schema,
    bool Ready,
    string? Serial,
    bool DeviceSelected,
    bool PingVerified,
    int TimeoutSec,
    AdbCommandOutput Wait,
    AdbCommandOutput? Ping,
    string? PingOutput);

public sealed record ViewProfileListResult(IReadOnlyList<string> Profiles);

public sealed record ViewProfileDeleteResult(string Name, bool Deleted);

public sealed record ReplaySummarizeResult(
    string Schema,
    string ArtifactRoot,
    int SessionCount,
    int FailureCount,
    IReadOnlyList<ReplaySummaryCommandHintResult> Commands,
    IReadOnlyList<ReplaySessionSummaryResult> Sessions);

public sealed record ReplaySummaryCommandHintResult(
    string Kind,
    string Description,
    string Command);

public sealed record ReplaySessionSummaryResult(
    string MetadataPath,
    string TimelinePath,
    string? FailureCapsulePath,
    ReplayFailureCapsuleResult? FailureCapsule,
    string SessionKind,
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    long DurationMs,
    string Reason,
    int ExitCode,
    string? Target,
    int EventCount,
    IReadOnlyList<string> EventTypes,
    bool HasTimeline,
    bool HasFailureSignals,
    IReadOnlyList<ReplayTimelineHighlightResult> TimelineHighlights);

public sealed record ReplayTimelineHighlightResult(
    DateTimeOffset? Timestamp,
    string Type,
    string Detail,
    bool FailureRelevant);

public sealed record ReplayFailureCapsuleResult(
    string Path,
    ReplayFailureCapsuleReportLinksResult Reports,
    IReadOnlyList<ReplayFailureCapsuleScenarioResult> Scenarios,
    IReadOnlyList<ReplayFailureCapsuleArtifactResult> Screenshots,
    IReadOnlyList<ReplayFailureCapsuleArtifactResult> Logcat,
    IReadOnlyList<ReplayFailureCapsuleArtifactResult> Hierarchies,
    IReadOnlyList<ReplayFailureCapsuleArtifactResult> ScreenStates,
    IReadOnlyList<ReplayFailureCapsuleBundleResult> FailureBundles);

public sealed record ReplayFailureCapsuleReportLinksResult(
    string? JsonPath,
    string? JunitPath);

public sealed record ReplayFailureCapsuleScenarioResult(
    string Scenario,
    string? ScenarioId,
    string Status,
    string? File,
    ReplayFailureCapsuleFailedStepResult? FailedStep,
    IReadOnlyList<ReplayFailureCapsuleArtifactResult> Artifacts,
    ErrorInfo? Error);

public sealed record ReplayFailureCapsuleFailedStepResult(
    int Index,
    string Name,
    string Action,
    string Phase);

public sealed record ReplayFailureCapsuleArtifactResult(
    string Kind,
    string Path,
    int? StepIndex,
    string? StepName);

public sealed record ReplayFailureCapsuleBundleResult(
    string Path,
    string? Scenario,
    string? ScenarioId,
    string? File,
    ReplayFailureCapsuleFailedStepResult? FailedStep,
    IReadOnlyList<ReplayFailureCapsuleArtifactResult> Artifacts,
    ErrorInfo? Error);

public sealed record ReplayOpenResult(
    string Schema,
    string ArtifactRoot,
    string IndexHtmlPath,
    string IndexMarkdownPath,
    int SessionCount,
    int FailureCount,
    ReplayOpenPrimaryFailureResult? PrimaryFailure,
    ReplayOpenNextActionResult? RecommendedNextAction,
    IReadOnlyList<ReplayOpenCommandHintResult> Commands,
    bool Opened,
    string? Opener,
    IReadOnlyList<string> OpenerArgs);

public sealed record ReplayOpenPrimaryFailureResult(
    string? Scenario,
    string? Step,
    string? Action,
    string? Message,
    string? TimelinePath,
    string? FailureCapsulePath);

public sealed record ReplayOpenNextActionResult(
    string Kind,
    string Title,
    string Reason,
    string Command);

public sealed record ReplayOpenCommandHintResult(
    string Kind,
    string Description,
    string Command);

public sealed record ReplayScenarioDraftResult(
    string Schema,
    string ArtifactRoot,
    string? Output,
    string? JsonPath,
    string? MarkdownPath,
    string Confidence,
    IReadOnlyList<string> Warnings,
    ScenarioFile Scenario,
    IReadOnlyList<ReplayScenarioDraftSuggestion> Suggestions,
    IReadOnlyList<ReplayScenarioDraftReviewItem> ReviewItems,
    IReadOnlyList<ReplayScenarioDraftSourceSummary> SourceSummaries,
    IReadOnlyList<ReplayScenarioDraftStepOrigin> StepOrigins,
    IReadOnlyList<ReplayScenarioDraftNormalization> Normalizations,
    IReadOnlyList<ReplayScenarioDraftCommandHint> SuggestedCommands);

public sealed record ReplayScenarioDraftCommandHint(
    string Command,
    string Purpose);

public sealed record ReplayScenarioDraftSuggestion(
    int StepIndex,
    string Kind,
    string Confidence,
    string Message);

public sealed record ReplayScenarioDraftReviewItem(
    string Severity,
    string Category,
    int? StepIndex,
    string Message,
    string? Command);

public sealed record ReplayScenarioDraftSourceSummary(
    string Source,
    int StepCount,
    int NormalizationCount,
    IReadOnlyList<string> EventTypes,
    string Confidence);

public sealed record ReplayScenarioDraftStepOrigin(
    int StepIndex,
    string Source,
    string EventType,
    string? Command,
    string? Detail,
    string Confidence,
    string? SourcePath,
    int? Sequence,
    DateTimeOffset? Timestamp,
    string? SourceCommand);

public sealed record ReplayScenarioDraftNormalization(
    string Kind,
    string Detail,
    string Source,
    string EventType,
    string Confidence,
    string? SourcePath,
    int? Sequence,
    DateTimeOffset? Timestamp,
    string? SourceCommand);

public sealed record ReplaySearchResult(
    string Schema,
    string ArtifactRoot,
    string Query,
    int MatchCount,
    int ScannedFileCount,
    bool Truncated,
    IReadOnlyList<ReplaySearchCommandHint> Commands,
    IReadOnlyList<ReplaySearchMatchResult> Matches);

public sealed record ReplaySearchCommandHint(
    string Kind,
    string Description,
    string Command);

public sealed record ReplaySearchMatchResult(
    string Path,
    int Line,
    string Kind,
    string Preview);

public sealed record ReplayCapsuleResult(
    string Schema,
    string ArtifactRoot,
    int SessionCount,
    int FailureCount,
    bool HasFailureCapsule,
    bool ScenarioDraftAvailable,
    string ScenarioDraftReason,
    ReplayCapsuleScenarioDraftArtifacts ScenarioDraftArtifacts,
    ReplayCapsuleScenarioDraftSummary? ScenarioDraftSummary,
    string? ReadmePath,
    string? JsonPath,
    ReplayCapsulePrimaryFailureResult? PrimaryFailure,
    ReplayCapsuleArtifactCounts ArtifactCounts,
    IReadOnlyList<ReplayCapsuleArtifactManifestEntry> ArtifactManifest,
    IReadOnlyList<ReplayCapsuleTimelineHighlightResult> FailureTimeline,
    IReadOnlyList<ReplayCapsuleNextStep> RecommendedNextSteps,
    IReadOnlyList<ReplayCapsuleCommandHint> SuggestedCommands);

public sealed record ReplayCapsulePrimaryFailureResult(
    string? Scenario,
    string? Step,
    string? Action,
    string? Message,
    string? FailureCapsulePath,
    string? TimelinePath,
    string? SourceCommand);

public sealed record ReplayCapsuleScenarioDraftArtifacts(
    string? SummaryPath,
    string? MarkdownPath,
    string? ScenarioPath);

public sealed record ReplayCapsuleScenarioDraftSummary(
    string? Confidence,
    int StepCount,
    int WarningCount,
    int ReviewItemCount,
    int NormalizationCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ReplayScenarioDraftReviewItem> ReviewItems);

public sealed record ReplayCapsuleArtifactCounts(
    int Screenshots,
    int Videos,
    int Logs,
    int Hierarchies,
    int ScreenStates,
    int Reports,
    int Timelines);

public sealed record ReplayCapsuleArtifactManifestEntry(
    string Path,
    string Kind,
    string Role,
    string? Session);

public sealed record ReplayCapsuleTimelineHighlightResult(
    string MetadataPath,
    string TimelinePath,
    int Sequence,
    DateTimeOffset? Timestamp,
    string Type,
    string Detail,
    bool FailureRelevant,
    string? ScenarioId,
    string? Scenario,
    int? StepIndex,
    string SourceCommand);

public sealed record ReplayCapsuleCommandHint(
    string Command,
    string Purpose);

public sealed record ReplayCapsuleNextStep(
    string Kind,
    string Title,
    string Reason,
    string Command);

public sealed record ReplayTimelineResult(
    string Schema,
    string ArtifactRoot,
    int EventCount,
    int ScannedFileCount,
    bool Truncated,
    string? JsonPath,
    string? JsonlPath,
    string? MarkdownPath,
    IReadOnlyList<ReplayTimelineCommandHint> Commands,
    IReadOnlyList<ReplayTimelineEventResult> Events);

public sealed record ReplayTimelineCommandHint(
    string Kind,
    string Description,
    string Command);

public sealed record ReplayTimelineEventResult(
    string Path,
    int Sequence,
    DateTimeOffset? Timestamp,
    string Type,
    bool FailureRelevant,
    string Detail,
    IReadOnlyDictionary<string, string?> Properties);

public sealed record ReplayScrubResult(
    string Schema,
    string ArtifactRoot,
    int EventCount,
    int FocusIndex,
    string? JsonPath,
    string? MarkdownPath,
    ReplayTimelineEventResult? FocusEvent,
    ReplayTimelineEventResult? PreviousEvent,
    ReplayTimelineEventResult? NextEvent,
    IReadOnlyList<ReplayTimelineEventResult> Events,
    IReadOnlyList<ReplayScrubCommandHint> Commands);

public sealed record ReplayScrubCommandHint(
    string Command,
    string Purpose);

public sealed record ReplayGraphResult(
    string Schema,
    string ArtifactRoot,
    ReplayGraphQueryResult Query,
    int NodeCount,
    int EdgeCount,
    int TotalNodeCount,
    int TotalEdgeCount,
    int MatchedNodeCount,
    int MatchedEdgeCount,
    bool Truncated,
    IReadOnlyDictionary<string, int> NodeKinds,
    IReadOnlyDictionary<string, int> EdgeKinds,
    ReplayGraphTaxonomyResult Taxonomy,
    ReplayGraphAgentSummaryResult AgentSummary,
    IReadOnlyList<ReplayGraphInsightResult> Insights,
    IReadOnlyList<ReplayGraphActionResult> Actions,
    IReadOnlyDictionary<string, int> EvidenceKinds,
    IReadOnlyList<ReplayGraphEvidenceResult> Evidence,
    IReadOnlyList<ReplayGraphFactResult> Facts,
    IReadOnlyList<ReplayGraphCausalChainResult> CausalChains,
    IReadOnlyList<ReplayGraphHypothesisResult> Hypotheses,
    IReadOnlyList<ReplayGraphFailurePathResult> FailurePaths,
    string? JsonPath,
    string? JsonlPath,
    string? MarkdownPath,
    IReadOnlyList<ReplayGraphNodeResult> Nodes,
    IReadOnlyList<ReplayGraphEdgeResult> Edges);

public sealed record ReplayGraphQueryResult(
    string? NodeKind,
    string? EdgeKind,
    string? Action,
    string? Selector,
    string? Contains,
    string? Insight,
    string? Severity,
    string? Evidence,
    string? Fact,
    string? Node,
    int Depth,
    bool FailedOnly,
    int Limit);

public sealed record ReplayGraphTaxonomyResult(
    IReadOnlyList<ReplayGraphTaxonomyEntryResult> NodeKinds,
    IReadOnlyList<ReplayGraphTaxonomyEntryResult> EdgeKinds,
    IReadOnlyList<ReplayGraphTaxonomyEntryResult> EvidenceKinds,
    IReadOnlyList<ReplayGraphQueryExampleResult> QueryExamples);

public sealed record ReplayGraphTaxonomyEntryResult(
    string Kind,
    string Description);

public sealed record ReplayGraphQueryExampleResult(
    string Kind,
    string Description,
    string Command);

public sealed record ReplayGraphAgentSummaryResult(
    string WhatFailed,
    string WhatChanged,
    string WhatCanActOn,
    IReadOnlyList<string> FailureNodeIds,
    IReadOnlyList<string> TransitionEdgeIds,
    IReadOnlyList<string> EvidenceNodeIds,
    IReadOnlyList<string> Commands);

public sealed record ReplayGraphInsightResult(
    string Kind,
    string Severity,
    string Message,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> EdgeIds);

public sealed record ReplayGraphActionResult(
    string Kind,
    string Message,
    string? Command);

public sealed record ReplayGraphEvidenceResult(
    string Kind,
    string NodeId,
    string Title,
    string? Detail,
    string? ArtifactPath,
    string? Command,
    IReadOnlyList<string> EdgeIds);

public sealed record ReplayGraphFactResult(
    string Category,
    string Subject,
    string Predicate,
    string Object,
    double Confidence,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> EdgeIds,
    string? Command);

public sealed record ReplayGraphCausalChainResult(
    string FailureNodeId,
    string Summary,
    IReadOnlyList<ReplayGraphCausalHopResult> Hops,
    string? Command);

public sealed record ReplayGraphCausalHopResult(
    string From,
    string To,
    string Relation,
    string? Category,
    string? Detail);

public sealed record ReplayGraphHypothesisResult(
    string Kind,
    string Severity,
    string Summary,
    double Confidence,
    IReadOnlyList<string> EvidenceNodeIds,
    IReadOnlyList<string> EdgeIds,
    string? Command);

public sealed record ReplayGraphFailurePathResult(
    string FailureNodeId,
    string? FailureEventNodeId,
    string Summary,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> EdgeIds);

public sealed record ReplayGraphNodeResult(
    string Id,
    string Kind,
    string Label,
    IReadOnlyDictionary<string, string?> Properties);

public sealed record ReplayGraphEdgeResult(
    string From,
    string To,
    string Kind,
    IReadOnlyDictionary<string, string?> Properties);

public sealed record ReplayClustersResult(
    string Schema,
    string ArtifactRoot,
    int SessionCount,
    int FailureCount,
    int ClusterCount,
    ReplayClusterQueryResult Query,
    string? JsonPath,
    string? MarkdownPath,
    IReadOnlyList<ReplayFailureClusterResult> Clusters);

public sealed record ReplayClusterQueryResult(
    int MinCount,
    string? Similarity,
    string? Contains);

public sealed record ReplayFailureClusterResult(
    string Id,
    string Signature,
    int Count,
    string? Category,
    string? Message,
    string? Action,
    string? Step,
    ReplayFailureClusterIntelligenceResult Intelligence,
    IReadOnlyList<ReplayFailureClusterHintResult> Hints,
    IReadOnlyList<ReplayFailureClusterInstanceResult> Instances);

public sealed record ReplayFailureClusterIntelligenceResult(
    string Similarity,
    double SimilarityScore,
    string LikelyCause,
    string BestReplayArtifactRoot,
    string? BestGraphCommand,
    string? BestScrubCommand,
    IReadOnlyList<string> SupportingSignals,
    IReadOnlyList<ReplayFailureClusterSignalComparisonResult> SignalComparisons);

public sealed record ReplayFailureClusterSignalComparisonResult(
    string Name,
    string Stability,
    IReadOnlyList<string> Values);

public sealed record ReplayFailureClusterHintResult(
    string Kind,
    string Message,
    string? Command);

public sealed record ReplayFailureClusterInstanceResult(
    string SessionId,
    string SessionKind,
    DateTimeOffset StartedAt,
    string? Target,
    string MetadataPath,
    string? FailureCapsulePath,
    string? ScenarioId,
    string? Scenario,
    string? File,
    int? StepIndex,
    string? Step,
    string? Action,
    string? ErrorCategory,
    string? ErrorMessage);

public sealed record DoctorCheck(
    string Name,
    bool Ok,
    string Summary,
    string? Detail = null,
    string? Recommendation = null);

public sealed record DoctorResult(
    bool Ready,
    bool Fix,
    string AdbExecutable,
    string? Package,
    IReadOnlyList<DoctorCheck> Checks,
    PreflightResult? PackagePreflight,
    View.Diagnostics.ViewDoctorResult View,
    IReadOnlyList<View.Diagnostics.ViewSetupStep> Repairs);

// Wait not visible
public sealed record WaitNotVisibleResult(string Text, int AttemptCount, bool Visible);

// Tap point
public sealed record TapPointResult(string? Label, int X, int Y, double? XRatio, double? YRatio, int PostTapDelayMs);

// Double tap header logo
public sealed record DoubleTapHeaderLogoResult(string Target, int X, int Y, int IntervalMs);

// Type pin
public sealed record TypePinResult(int PinLength, int PerDigitDelayMs);

// Reset log
public sealed record ResetLogResult(bool Cleared);

// Assert event
public sealed record AssertEventResult(string Name, IReadOnlyList<string> Contains, string? DetailsPattern, string MatchedLine);

// Take screenshot
public sealed record TakeScreenshotResult(string Label, string File, int? Width = null, int? Height = null, string? Sha256 = null);

public sealed record ScreenshotAssertionResult(
    string Label,
    string File,
    int? Width,
    int? Height,
    string? Sha256,
    int? ExpectedWidth,
    int? ExpectedHeight,
    string? ExpectedSha256,
    string? BaselineFile = null,
    bool BaselineUpdated = false,
    ScreenshotAssertionRegion? Region = null,
    string? RegionSha256 = null,
    string? ExpectedRegionSha256 = null,
    string? DiffArtifact = null);

public sealed record ScreenshotAssertionRegion(int X, int Y, int Width, int Height);

// Capture artifacts
public sealed record CaptureArtifactsResult(string Label, string Screenshot, string Logcat, string ScreenState, string Hierarchy);

// Assert text input ready
public sealed record AssertTextInputReadyResult(bool RequireKeyboard, bool KeyboardVisible, string? Text, string? ResourceId, string? Bounds);

// Assert below
public sealed record AssertBelowResult(string Text, string Below, int GapPx, int MaxGapPx);

// Assert aligned
public sealed record AssertAlignedResult(string Text, string With, int DeltaPx, int MaxDeltaPx);

// Assert app version
public sealed record AssertAppVersionResult(
    string Package,
    string Label,
    int TopInsetPx,
    int RightInsetPx,
    int MaxTopInsetPx,
    int MaxRightInsetPx);

// Sleep
public sealed record SleepResult(int Milliseconds);

// Artifact data
public sealed record ArtifactData(string ArtifactRoot, string PollArtifacts);
