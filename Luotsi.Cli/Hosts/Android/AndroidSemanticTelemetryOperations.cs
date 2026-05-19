using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed class AndroidSemanticTelemetryOperations(
    IAdbClient adb,
    AndroidTelemetryMonitor telemetryMonitor)
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly AndroidTelemetryMonitor _telemetryMonitor = telemetryMonitor ?? throw new ArgumentNullException(nameof(telemetryMonitor));

    public async Task<TelemetryResult> TelemetryTailAsync(int tail)
    {
        var validatedTail = RequirePositive(tail, "telemetryTail requires tail greater than zero.");
        var result = await _adb.RunAsync(["logcat", "-d", "-v", "brief", "-t", validatedTail.ToString()]).ConfigureAwait(false);
        result.EnsureSuccess("telemetry tail failed");
        return await _telemetryMonitor.CaptureTelemetryAsync(
            DeviceArtifactNames.TelemetryTailBaseName,
            result.Stdout,
            new
            {
                schema = ResultSchemas.TelemetryTail,
                tail = validatedTail,
                invocation = result.Invocation
            }).ConfigureAwait(false);
    }

    public async Task<TelemetryResult> TelemetryWatchAsync(int timeoutSec)
    {
        var validatedTimeoutSec = RequirePositive(timeoutSec, "telemetryWatch requires timeoutSec greater than zero.");
        var telemetrySession = await _telemetryMonitor.MonitorTelemetryAsync(validatedTimeoutSec).ConfigureAwait(false);
        return await _telemetryMonitor.CaptureTelemetryAsync(
            DeviceArtifactNames.TelemetryWatchBaseName,
            telemetrySession.LogOutput,
            new
            {
                schema = ResultSchemas.TelemetryWatch,
                started_at = telemetrySession.StartedAt,
                timeout_sec = validatedTimeoutSec,
                invocation = telemetrySession.Invocation
            },
            telemetrySession.Parsed).ConfigureAwait(false);
    }

    public Task<TelemetryMatchResult> WaitForStepAsync(string step, int timeoutSec)
    {
        var expectedStep = NormalizeTelemetryStep(RequireNonBlank(step, "waitStep requires step."));
        var validatedTimeoutSec = RequirePositive(timeoutSec, "waitStep requires timeoutSec greater than zero.");
        return _telemetryMonitor.WaitForTelemetryEventAsync(
            validatedTimeoutSec,
            telemetry => string.Equals(telemetry.Event, "step", StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizeTelemetryStep(telemetry.Step), expectedStep, StringComparison.Ordinal),
            telemetry => new TelemetryMatchResult(expectedStep, null, telemetry.RawLine, telemetry.Event!, telemetry.Payload),
            DeviceArtifactNames.WaitStepBaseName,
            invocation => new
            {
                schema = ResultSchemas.WaitStep,
                step = expectedStep,
                timeout_sec = validatedTimeoutSec,
                invocation
            },
            () => new SemanticWaitTimeoutException($"device step '{expectedStep}'", validatedTimeoutSec));
    }

    public Task<TelemetryMatchResult> WaitForActionReadyAsync(string action, string? step, int timeoutSec)
    {
        var expectedAction = RequireNonBlank(action, "waitActionReady requires action.");
        var normalizedStep = NormalizeTelemetryStep(step);
        var validatedTimeoutSec = RequirePositive(timeoutSec, "waitActionReady requires timeoutSec greater than zero.");
        return _telemetryMonitor.WaitForTelemetryEventAsync(
            validatedTimeoutSec,
            telemetry =>
                string.Equals(telemetry.Event, "action_ready", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(telemetry.Action, expectedAction, StringComparison.OrdinalIgnoreCase) &&
                (normalizedStep is null || string.Equals(NormalizeTelemetryStep(telemetry.Step), normalizedStep, StringComparison.Ordinal)),
            telemetry => new TelemetryMatchResult(normalizedStep, expectedAction, telemetry.RawLine, telemetry.Event!, telemetry.Payload),
            DeviceArtifactNames.WaitActionReadyBaseName,
            invocation => new
            {
                schema = ResultSchemas.WaitActionReady,
                action = expectedAction,
                step = normalizedStep,
                timeout_sec = validatedTimeoutSec,
                invocation
            },
            () => new SemanticWaitTimeoutException($"device action ready '{expectedAction}'" + (normalizedStep is null ? string.Empty : $" on '{normalizedStep}'"), validatedTimeoutSec));
    }

    private static string RequireNonBlank(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UsageException(message);
        }

        return value;
    }

    private static int RequirePositive(int value, string message)
    {
        if (value <= 0)
        {
            throw new UsageException(message);
        }

        return value;
    }

    private static string? NormalizeTelemetryStep(string? step)
    {
        if (string.IsNullOrWhiteSpace(step))
        {
            return null;
        }

        var normalized = step.Trim().ToUpperInvariant().Replace('-', '_');
        return normalized.StartsWith("STEP_", StringComparison.Ordinal) ? normalized : $"STEP_{normalized}";
    }
}