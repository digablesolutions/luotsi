using System.Text.Json;

namespace DeviceE2ELab.Cli;

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
/// Default parser for the kiosk <c>DEVICE_TEST_TELEMETRY</c> logcat contract.
/// </summary>
public sealed class DeviceTestTelemetryParser : ITelemetryParser
{
    private const string Prefix = "DEVICE_TEST_TELEMETRY";

    /// <summary>
    /// Parses telemetry events and malformed lines from a raw logcat payload.
    /// </summary>
    /// <param name="logOutput">Raw logcat text.</param>
    /// <returns>Parsed telemetry results.</returns>
    public TelemetryParseResult ParseLog(string logOutput)
    {
        var events = new List<TelemetryEvent>();
        var parseErrors = new List<TelemetryParseError>();
        var lines = (logOutput ?? string.Empty).Split('\n', StringSplitOptions.None);
        var telemetryLineCount = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var prefixIndex = line.IndexOf(Prefix, StringComparison.Ordinal);
            if (prefixIndex < 0)
            {
                continue;
            }

            telemetryLineCount++;
            var jsonStart = line.IndexOf('{', prefixIndex + Prefix.Length);
            if (jsonStart < 0)
            {
                parseErrors.Add(new TelemetryParseError(line, "Telemetry line did not contain a JSON payload."));
                continue;
            }

            var jsonText = line[jsonStart..].Trim();
            try
            {
                using var document = JsonDocument.Parse(jsonText);
                var payload = document.RootElement.Clone();
                events.Add(new TelemetryEvent(
                    TryGetString(payload, "schema"),
                    TryGetInt64(payload, "seq"),
                    TryGetString(payload, "session"),
                    TryGetString(payload, "timestamp"),
                    TryGetString(payload, "event"),
                    TryGetString(payload, "step"),
                    TryGetString(payload, "action"),
                    payload,
                    line));
            }
            catch (JsonException ex)
            {
                parseErrors.Add(new TelemetryParseError(line, ex.Message));
            }
        }

        return new TelemetryParseResult(events, parseErrors, lines.Count(static line => !string.IsNullOrWhiteSpace(line)), telemetryLineCount);
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static long? TryGetInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : null;
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