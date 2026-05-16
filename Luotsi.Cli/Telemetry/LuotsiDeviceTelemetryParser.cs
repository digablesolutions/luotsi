using System.Text.Json;

namespace Luotsi.Cli.Telemetry;

/// <summary>
/// Default parser for the kiosk <c>LUOTSI_DEVICE_TELEMETRY</c> logcat contract.
/// Temporarily also accepts the legacy <c>DEVICE_TEST_TELEMETRY</c> marker.
/// </summary>
public sealed class LuotsiDeviceTelemetryParser : ITelemetryParser
{
    private static readonly string[] Prefixes = ["LUOTSI_DEVICE_TELEMETRY", "DEVICE_TEST_TELEMETRY"];

    /// <summary>
    /// Parses telemetry events and malformed lines from a raw logcat payload.
    /// </summary>
    /// <param name="logOutput">Raw logcat text.</param>
    /// <returns>Parsed telemetry results.</returns>
    public TelemetryParseResult ParseLog(string? logOutput)
    {
        var events = new List<TelemetryEvent>();
        var parseErrors = new List<TelemetryParseError>();
        var lines = (logOutput ?? string.Empty).Split('\n');
        var inspectedLineCount = 0;
        var telemetryLineCount = 0;

        foreach (var rawLine in lines)
        {
            var parsedLine = ParseLine(rawLine);
            if (!parsedLine.Inspected)
            {
                continue;
            }

            inspectedLineCount++;
            if (parsedLine.TelemetryLine)
            {
                telemetryLineCount++;
            }

            if (parsedLine.Event is not null)
            {
                events.Add(parsedLine.Event);
            }

            if (parsedLine.ParseError is not null)
            {
                parseErrors.Add(parsedLine.ParseError);
            }
        }

        return new TelemetryParseResult(events, parseErrors, inspectedLineCount, telemetryLineCount);
    }

    public TelemetryLineParseResult ParseLine(string? rawLine)
    {
        var line = rawLine?.TrimEnd('\r') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return new TelemetryLineParseResult(false, false, null, null);
        }

        var prefix = FindPrefix(line, out var prefixIndex);
        if (prefix is null)
        {
            return new TelemetryLineParseResult(true, false, null, null);
        }

        var jsonStart = line.IndexOf('{', prefixIndex + prefix.Length);
        if (jsonStart < 0)
        {
            return new TelemetryLineParseResult(true, true, null, new TelemetryParseError(line, "Telemetry line did not contain a JSON payload."));
        }

        var jsonText = line[jsonStart..].Trim();
        try
        {
            using var document = JsonDocument.Parse(jsonText);
            var payload = document.RootElement.Clone();
            return new TelemetryLineParseResult(
                true,
                true,
                new TelemetryEvent(
                    TryGetString(payload, "schema"),
                    TryGetInt64(payload, "seq"),
                    TryGetString(payload, "session"),
                    TryGetString(payload, "timestamp"),
                    TryGetString(payload, "event"),
                    TryGetString(payload, "step"),
                    TryGetString(payload, "action"),
                    payload,
                    line),
                null);
        }
        catch (JsonException ex)
        {
            return new TelemetryLineParseResult(true, true, null, new TelemetryParseError(line, ex.Message));
        }
    }

    private static string? FindPrefix(string line, out int prefixIndex)
    {
        foreach (var prefix in Prefixes)
        {
            var index = line.IndexOf(prefix, StringComparison.Ordinal);
            if (index >= 0)
            {
                prefixIndex = index;
                return prefix;
            }
        }

        prefixIndex = -1;
        return null;
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