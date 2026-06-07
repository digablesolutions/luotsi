namespace Luotsi.Cli.Models;

/// <summary>
/// Shared schema names for command envelopes and device-runtime result artifacts.
/// </summary>
public static class ResultSchemas
{
    public const string CommandEnvelope = "luotsi-command.v1";
    public const string Quickstart = "luotsi-quickstart.v1";
    public const string LogWait = "luotsi-log-wait.v1";
    public const string TelemetryTail = "luotsi-telemetry-tail.v1";
    public const string TelemetryWatch = "luotsi-telemetry-watch.v1";
    public const string WaitStep = "luotsi-wait-step.v1";
    public const string WaitActionReady = "luotsi-wait-action-ready.v1";
    public const string AssertEvent = "luotsi-assert-event.v1";
    public const string AdbDiagnostic = "luotsi-adb-diagnostic.v1";
    public const string AdbReadiness = "luotsi-adb-readiness.v1";
    public const string DeviceFingerprint = "device-fingerprint.v1";
    public const string SessionReplay = "luotsi-session-replay.v1";
    public const string SessionReplaySummary = "luotsi-session-replay-summary.v1";
    public const string ArtifactPackage = "luotsi-artifact-package.v1";
    public const string FailureBundle = "luotsi-failure-bundle.v1";
    public const string FailureCapsule = "luotsi-failure-capsule.v1";
    public const string ReplayOpen = "luotsi-replay-open.v1";
    public const string ScenarioDraft = "luotsi-scenario-draft.v1";
    public const string ReplaySearch = "luotsi-replay-search.v1";
    public const string ReplayCapsule = "luotsi-replay-capsule.v1";
    public const string ReplayTimeline = "luotsi-replay-timeline.v1";
    public const string ReplayScrub = "luotsi-replay-scrub.v1";
    public const string ReplayGraph = "luotsi-replay-graph.v1";
    public const string ReplayClusters = "luotsi-replay-clusters.v1";
    public const string DiscoveryResult = "luotsi-discovery-result.v1";
    public const string DiscoveryMap = "luotsi-discovery-map.v1";
    public const string DiscoveryEvent = "luotsi-discovery-event.v1";
}
