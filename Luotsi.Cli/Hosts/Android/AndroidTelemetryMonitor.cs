using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.Telemetry;

namespace Luotsi.Cli.Hosts.Android;

internal sealed record TelemetryMonitorResult(
    DateTimeOffset StartedAt,
    string Invocation,
    string LogOutput,
    TelemetryParseResult Parsed,
    TelemetryEvent? MatchedEvent);

internal sealed class AndroidTelemetryMonitor(
    IAdbClient adb,
    ArtifactSession artifacts,
    TimeProvider timeProvider,
    ITelemetryParser telemetryParser)
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ITelemetryParser _telemetryParser = telemetryParser ?? throw new ArgumentNullException(nameof(telemetryParser));

    public async Task<TelemetryResult> CaptureTelemetryAsync(
        string artifactBaseName,
        string logOutput,
        object metadata,
        TelemetryParseResult? parsed = null)
    {
        parsed ??= _telemetryParser.ParseLog(logOutput);
        var result = new TelemetryResult(
            parsed.InspectedLineCount,
            parsed.TelemetryLineCount,
            parsed.Events.Count,
            parsed.ParseErrors.Count,
            parsed.Events,
            parsed.ParseErrors);
        await _artifacts.WriteTextAsync(DeviceArtifactNames.TextFromBase(artifactBaseName), logOutput).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(
            DeviceArtifactNames.JsonFromBase(artifactBaseName),
            new
            {
                metadata,
                inspected_line_count = result.InspectedLineCount,
                telemetry_line_count = result.TelemetryLineCount,
                event_count = result.EventCount,
                parse_error_count = result.ParseErrorCount,
                events = result.Events,
                parse_errors = result.ParseErrors
            }).ConfigureAwait(false);

        return result;
    }

    public async Task<TelemetryMatchResult> WaitForTelemetryEventAsync(
        int timeoutSec,
        Func<TelemetryEvent, bool> eventMatch,
        Func<TelemetryEvent, TelemetryMatchResult> successDataFactory,
        string artifactBaseName,
        Func<string, object> metadataFactory,
        Func<Exception> timeoutExceptionFactory)
    {
        var telemetrySession = await MonitorTelemetryAsync(timeoutSec, eventMatch).ConfigureAwait(false);
        var match = telemetrySession.MatchedEvent;

        await _artifacts.WriteTextAsync(DeviceArtifactNames.TextFromBase(artifactBaseName), telemetrySession.LogOutput).ConfigureAwait(false);

        if (match is not null)
        {
            await _artifacts.WriteJsonAsync(
                DeviceArtifactNames.JsonFromBase(artifactBaseName),
                new
                {
                    metadata = metadataFactory(telemetrySession.Invocation),
                    event_count = telemetrySession.Parsed.Events.Count,
                    parse_error_count = telemetrySession.Parsed.ParseErrors.Count,
                    matched = successDataFactory(match),
                    events = telemetrySession.Parsed.Events,
                    parse_errors = telemetrySession.Parsed.ParseErrors
                }).ConfigureAwait(false);

            return successDataFactory(match);
        }

        await _artifacts.WriteJsonAsync(
            DeviceArtifactNames.JsonFromBase(artifactBaseName),
            new
            {
                metadata = metadataFactory(telemetrySession.Invocation),
                event_count = telemetrySession.Parsed.Events.Count,
                parse_error_count = telemetrySession.Parsed.ParseErrors.Count,
                events = telemetrySession.Parsed.Events,
                parse_errors = telemetrySession.Parsed.ParseErrors
            }).ConfigureAwait(false);

        throw timeoutExceptionFactory();
    }

    public async Task<TelemetryMonitorResult> MonitorTelemetryAsync(int timeoutSec, Func<TelemetryEvent, bool>? eventMatch = null)
    {
        var started = _timeProvider.GetUtcNow();
        var accumulator = new TelemetryStreamAccumulator(_telemetryParser, eventMatch);

        var monitor = await _adb.MonitorLogAsync(
            started,
            timeoutSec,
            eventMatch is null ? null : accumulator.ShouldStop,
            accumulator.ObserveLine).ConfigureAwait(false);

        if (monitor.ExitCode != 0)
        {
            throw new InvalidOperationException($"adb logcat failed: {monitor.Stderr}".Trim());
        }

        return new TelemetryMonitorResult(started, monitor.Invocation, monitor.LogOutput, accumulator.ToParseResult(), accumulator.MatchedEvent);
    }

    private sealed class TelemetryStreamAccumulator(ITelemetryParser telemetryParser, Func<TelemetryEvent, bool>? eventMatch)
    {
        private readonly List<TelemetryEvent> _events = [];
        private readonly List<TelemetryParseError> _parseErrors = [];
        private int _inspectedLineCount;
        private int _telemetryLineCount;

        public TelemetryEvent? MatchedEvent { get; private set; }

        public void ObserveLine(string line)
        {
            var parsedLine = telemetryParser.ParseLine(line);
            if (!parsedLine.Inspected)
            {
                return;
            }

            _inspectedLineCount++;
            if (parsedLine.TelemetryLine)
            {
                _telemetryLineCount++;
            }

            if (parsedLine.Event is not null)
            {
                _events.Add(parsedLine.Event);
                if (MatchedEvent is null && eventMatch?.Invoke(parsedLine.Event) is true)
                {
                    MatchedEvent = parsedLine.Event;
                }
            }

            if (parsedLine.ParseError is not null)
            {
                _parseErrors.Add(parsedLine.ParseError);
            }
        }

        public bool ShouldStop(string _) => MatchedEvent is not null;

        public TelemetryParseResult ToParseResult() => new(_events, _parseErrors, _inspectedLineCount, _telemetryLineCount);
    }
}