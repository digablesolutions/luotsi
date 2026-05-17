using System.Text.RegularExpressions;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed class AndroidLogAndTelemetryOperations(
    IAdbClient adb,
    ArtifactSession artifacts,
    TimeProvider timeProvider,
    AndroidTelemetryMonitor telemetryMonitor)
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly AndroidTelemetryMonitor _telemetryMonitor = telemetryMonitor ?? throw new ArgumentNullException(nameof(telemetryMonitor));

    public async Task<WaitLogResult> WaitForLogAsync(string text, int timeoutSec)
    {
        var containsText = RequireNonBlank(text, "waitLog requires text.");
        var validatedTimeoutSec = RequirePositive(timeoutSec, "waitLog requires timeoutSec greater than zero.");
        var started = _timeProvider.GetUtcNow();
        var monitor = await _adb.MonitorLogAsync(containsText, started, validatedTimeoutSec).ConfigureAwait(false);
        await _artifacts.WriteTextAsync(DeviceArtifactNames.TextFromBase(DeviceArtifactNames.WaitLogBaseName), monitor.LogOutput).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(
            DeviceArtifactNames.JsonFromBase(DeviceArtifactNames.WaitLogBaseName),
            new
            {
                schema = ResultSchemas.LogWait,
                contains = containsText,
                timeout_sec = validatedTimeoutSec,
                started_at = started,
                matched_line = monitor.MatchedLine,
                line_count = monitor.LineCount,
                invocation = monitor.Invocation
            }).ConfigureAwait(false);

        if (monitor.ExitCode != 0)
        {
            throw new InvalidOperationException($"adb logcat failed: {monitor.Stderr}".Trim());
        }

        if (monitor.MatchedLine is null)
        {
            throw new LogWaitTimeoutException(containsText, validatedTimeoutSec);
        }

        return new WaitLogResult(containsText, validatedTimeoutSec, monitor.MatchedLine, monitor.LineCount);
    }

    public async Task<LogcatResult> LogcatAsync(int tail)
    {
        var validatedTail = RequirePositive(tail, "logcat requires tail greater than zero.");
        var result = await _adb.RunAsync(["logcat", "-d", "-t", validatedTail.ToString()]).ConfigureAwait(false);
        result.EnsureSuccess("logcat failed");
        await _artifacts.WriteTextAsync(DeviceArtifactNames.LogcatText, result.Stdout).ConfigureAwait(false);
        return new LogcatResult(result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

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

    public async Task<ResetLogResult> ResetLogAsync()
    {
        var result = await _adb.RunAsync(["logcat", "-c"]).ConfigureAwait(false);
        result.EnsureSuccess("log reset failed");
        return new ResetLogResult(true);
    }

    public async Task<AssertEventResult> AssertEventAsync(string name, IReadOnlyList<string> contains, string? detailsPattern, int timeoutSec, DateTimeOffset? since = null)
    {
        var eventName = RequireNonBlank(name, "assertEvent requires event or text.");
        var validatedTimeoutSec = RequirePositive(timeoutSec, "assertEvent requires timeoutSec greater than zero.");
        var started = since ?? _timeProvider.GetUtcNow();
        var detailsRegex = CreateDetailsRegex(detailsPattern);
        var monitor = await _adb.MonitorLogAsync(
            started,
            validatedTimeoutSec,
            line => EventLineMatches(line, eventName, contains, detailsRegex)).ConfigureAwait(false);

        if (monitor.ExitCode != 0)
        {
            throw new InvalidOperationException($"adb logcat failed: {monitor.Stderr}".Trim());
        }

        await _artifacts.WriteTextAsync(DeviceArtifactNames.TextFromBase(DeviceArtifactNames.AssertEventBaseName), monitor.LogOutput).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(
            DeviceArtifactNames.JsonFromBase(DeviceArtifactNames.AssertEventBaseName),
            new
            {
                schema = ResultSchemas.AssertEvent,
                name = eventName,
                contains,
                details_pattern = detailsPattern,
                observed_since = started,
                timeout_sec = validatedTimeoutSec,
                invocation = monitor.Invocation,
                matched_line = monitor.MatchedLine
            }).ConfigureAwait(false);

        if (monitor.MatchedLine is null)
        {
            throw new SemanticWaitTimeoutException($"event '{eventName}'", validatedTimeoutSec);
        }

        return new AssertEventResult(eventName, contains, detailsPattern, monitor.MatchedLine);
    }

    private static bool EventLineMatches(string line, string name, IReadOnlyList<string> contains, Regex? detailsRegex)
    {
        if (!line.Contains($"Log.{name}", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (contains.Any(required => !line.Contains(required, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return detailsRegex is null || detailsRegex.IsMatch(line);
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

    private static Regex? CreateDetailsRegex(string? detailsPattern)
    {
        if (string.IsNullOrWhiteSpace(detailsPattern))
        {
            return null;
        }

        try
        {
            return new Regex(detailsPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            throw new UsageException($"assertEvent detailsPattern is not a valid regular expression: {ex.Message}");
        }
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