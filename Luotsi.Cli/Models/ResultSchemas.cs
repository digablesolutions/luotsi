namespace Luotsi.Cli.Models;

/// <summary>
/// Shared schema names for command envelopes and device-runtime result artifacts.
/// </summary>
public static class ResultSchemas
{
    public const string CommandEnvelope = "luotsi-command.v1";
    public const string LogWait = "luotsi-log-wait.v1";
    public const string TelemetryTail = "luotsi-telemetry-tail.v1";
    public const string TelemetryWatch = "luotsi-telemetry-watch.v1";
    public const string WaitStep = "luotsi-wait-step.v1";
    public const string WaitActionReady = "luotsi-wait-action-ready.v1";
    public const string AssertEvent = "luotsi-assert-event.v1";
    public const string DeviceFingerprint = "device-fingerprint.v1";
    public const string FailureBundle = "luotsi-failure-bundle.v1";
}