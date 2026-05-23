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
    string? Package = null);

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
    IReadOnlyList<ReplaySessionSummaryResult> Sessions);

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
    bool Opened,
    string? Opener,
    IReadOnlyList<string> OpenerArgs);

public sealed record ReplayScenarioDraftResult(
    string Schema,
    string ArtifactRoot,
    string? Output,
    string Confidence,
    IReadOnlyList<string> Warnings,
    ScenarioFile Scenario,
    IReadOnlyList<ReplayScenarioDraftSuggestion> Suggestions);

public sealed record ReplayScenarioDraftSuggestion(
    int StepIndex,
    string Kind,
    string Confidence,
    string Message);

public sealed record ReplaySearchResult(
    string Schema,
    string ArtifactRoot,
    string Query,
    int MatchCount,
    int ScannedFileCount,
    bool Truncated,
    IReadOnlyList<ReplaySearchMatchResult> Matches);

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
    string? ReadmePath,
    string? JsonPath,
    ReplayCapsulePrimaryFailureResult? PrimaryFailure,
    ReplayCapsuleArtifactCounts ArtifactCounts,
    IReadOnlyList<ReplayCapsuleCommandHint> SuggestedCommands);

public sealed record ReplayCapsulePrimaryFailureResult(
    string? Scenario,
    string? Step,
    string? Action,
    string? Message,
    string? FailureCapsulePath,
    string? TimelinePath);

public sealed record ReplayCapsuleArtifactCounts(
    int Screenshots,
    int Videos,
    int Logs,
    int Hierarchies,
    int ScreenStates,
    int Reports,
    int Timelines);

public sealed record ReplayCapsuleCommandHint(
    string Command,
    string Purpose);

public sealed record ReplayTimelineResult(
    string Schema,
    string ArtifactRoot,
    int EventCount,
    int ScannedFileCount,
    bool Truncated,
    string? JsonPath,
    string? JsonlPath,
    string? MarkdownPath,
    IReadOnlyList<ReplayTimelineEventResult> Events);

public sealed record ReplayTimelineEventResult(
    string Path,
    int Sequence,
    DateTimeOffset? Timestamp,
    string Type,
    bool FailureRelevant,
    string Detail);

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
