using System.Text.Json;

namespace VisitLab.Cli;

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