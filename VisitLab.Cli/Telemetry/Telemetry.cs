using System.Text.Json;

namespace VisitLab.Cli;

/// <summary>
/// Parses semantic device telemetry emitted through logcat.
/// </summary>
public interface ITelemetryParser
{
    /// <summary>
    /// Parses telemetry events and malformed lines from a raw logcat payload.
    /// </summary>
    /// <param name="logOutput">Raw logcat text.</param>
    /// <returns>Parsed telemetry results.</returns>
    TelemetryParseResult ParseLog(string logOutput);
}

/// <summary>
/// Parsed semantic telemetry event.
/// </summary>
/// <param name="Schema">Telemetry schema name.</param>
/// <param name="Seq">Optional event sequence number.</param>
/// <param name="Session">Optional session identifier.</param>
/// <param name="Timestamp">Optional event timestamp string.</param>
/// <param name="Event">Semantic event name.</param>
/// <param name="Step">Optional semantic step name.</param>
/// <param name="Action">Optional semantic action name.</param>
/// <param name="Payload">Full telemetry payload.</param>
/// <param name="RawLine">Original logcat line.</param>
public sealed record TelemetryEvent(
    string? Schema,
    long? Seq,
    string? Session,
    string? Timestamp,
    string? Event,
    string? Step,
    string? Action,
    JsonElement Payload,
    string RawLine);

/// <summary>
/// Telemetry line that matched the prefix but could not be parsed as JSON.
/// </summary>
/// <param name="RawLine">Original logcat line.</param>
/// <param name="Message">Parse failure message.</param>
public sealed record TelemetryParseError(string RawLine, string Message);

/// <summary>
/// Collection of parsed telemetry events and parse failures.
/// </summary>
/// <param name="Events">Parsed telemetry events.</param>
/// <param name="ParseErrors">Malformed telemetry lines.</param>
/// <param name="InspectedLineCount">Total non-empty logcat lines inspected.</param>
/// <param name="TelemetryLineCount">Total lines containing the telemetry prefix.</param>
public sealed record TelemetryParseResult(
    IReadOnlyList<TelemetryEvent> Events,
    IReadOnlyList<TelemetryParseError> ParseErrors,
    int InspectedLineCount,
    int TelemetryLineCount);