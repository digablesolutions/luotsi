using System.Text.Json;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewConsoleEventWriter
{
    private readonly IConsoleIo _console;
    private readonly string _mode;

    public ViewConsoleEventWriter(IConsoleIo console, ViewOptions options)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        ArgumentNullException.ThrowIfNull(options);
        _mode = options.ConsoleOutput;
    }

    public void Write(string json)
    {
        if (string.Equals(_mode, ViewConsoleOutputModes.Jsonl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_mode, ViewConsoleOutputModes.Json, StringComparison.OrdinalIgnoreCase))
        {
            _console.WriteLine(json);
            return;
        }

        if (string.Equals(_mode, ViewConsoleOutputModes.Quiet, StringComparison.OrdinalIgnoreCase))
        {
            WriteQuiet(json);
            return;
        }

        WriteHuman(json);
    }

    private void WriteQuiet(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = GetString(root, "type");
        if (string.Equals(type, SessionEventTypes.View.Diagnostic, StringComparison.Ordinal) ||
            string.Equals(type, SessionEventTypes.View.Error, StringComparison.Ordinal))
        {
            WriteHuman(root, type);
        }
    }

    private void WriteHuman(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        WriteHuman(root, GetString(root, "type"));
    }

    private void WriteHuman(JsonElement root, string? type)
    {
        switch (type)
        {
            case SessionEventTypes.View.StartupPhase:
                WriteStartupPhase(root);
                break;
            case SessionEventTypes.View.Started:
                WriteStarted(root);
                break;
            case SessionEventTypes.View.CaptureBackendFallback:
                _console.WriteLine($"WARN Falling back from {GetString(root, "failed_capture_backend") ?? "unknown"} to {GetString(root, "fallback_capture_backend") ?? "unknown"}: {GetString(root, "reason") ?? "no reason reported"}");
                break;
            case SessionEventTypes.View.Diagnostic:
                _console.WriteLine($"WARN {GetString(root, "message") ?? "View diagnostic reported."}");
                WriteIndented("Next", GetString(root, "next_command"));
                break;
            case SessionEventTypes.View.Error:
                WriteError(root);
                break;
            case SessionEventTypes.View.Ended:
                _console.WriteLine($"View ended: {GetString(root, "reason") ?? "unknown"}");
                break;
            case SessionEventTypes.View.Reconnected:
                _console.WriteLine($"OK  View reconnected to {GetString(root, "device") ?? "device"}.");
                break;
            case SessionEventTypes.View.ShareStarted:
                _console.WriteLine($"OK  View sharing at {GetString(root, "endpoint") ?? "unknown endpoint"}.");
                break;
            case SessionEventTypes.View.ShareClientConnected:
                _console.WriteLine($"OK  Share client connected: {GetString(root, "remote_endpoint") ?? "unknown client"}.");
                break;
            case SessionEventTypes.View.ShareClientDisconnected:
                _console.WriteLine($"--  Share client disconnected: {GetString(root, "remote_endpoint") ?? "unknown client"}.");
                break;
            case SessionEventTypes.View.RecordingStarted:
                _console.WriteLine($"OK  Recording started: {GetRecordingPath(root) ?? "recording output"}.");
                break;
            case SessionEventTypes.View.RecordingStopped:
                _console.WriteLine($"OK  Recording stopped: {GetRecordingPath(root) ?? "recording output"}.");
                break;
            case SessionEventTypes.View.ScreenshotCaptured:
                _console.WriteLine($"OK  Screenshot captured: {GetString(root, "path") ?? "screenshot output"}.");
                break;
            case SessionEventTypes.View.ReconnectRequested:
                _console.WriteLine($"--  Reconnect requested: {GetString(root, "reason") ?? "no reason reported"}.");
                break;
            case SessionEventTypes.View.StreamPaused:
                _console.WriteLine("--  View stream paused.");
                break;
            case SessionEventTypes.View.StreamResumed:
                _console.WriteLine("OK  View stream resumed.");
                break;
            case SessionEventTypes.View.InteractionFailed:
                _console.WriteLine($"WARN Interaction failed: {GetString(root, "message") ?? GetString(root, "reason") ?? "no reason reported"}");
                break;
            case SessionEventTypes.View.InputBlocked:
                _console.WriteLine($"WARN Input blocked: {GetString(root, "reason") ?? "read-only session"}.");
                break;
            case SessionEventTypes.View.Stats:
            case SessionEventTypes.View.DeviceShelf:
                break;
            default:
                if (!string.IsNullOrWhiteSpace(type))
                {
                    _console.WriteLine($"--  {type}");
                }

                break;
        }
    }

    private void WriteStartupPhase(JsonElement root)
    {
        var status = GetString(root, "status");
        var summary = GetString(root, "summary") ?? GetString(root, "phase") ?? "View startup phase.";
        var prefix = status switch
        {
            ViewStartupPhaseStatus.Succeeded => "OK ",
            ViewStartupPhaseStatus.Failed => "FAIL",
            ViewStartupPhaseStatus.Skipped => "-- ",
            _ => ".. "
        };

        _console.WriteLine($"{prefix} {summary}");

        if (string.Equals(status, ViewStartupPhaseStatus.Failed, StringComparison.OrdinalIgnoreCase))
        {
            WriteIndented("Detail", GetString(root, "detail"));
            WriteIndented("Next", GetString(root, "recommendation"));
        }
    }

    private void WriteStarted(JsonElement root)
    {
        _console.WriteLine("View started");
        WriteIndented("device", GetString(root, "device"));
        WriteIndented("backend", JoinNonEmpty(GetString(root, "capture_backend"), GetString(root, "backend")));
        WriteIndented("decoder", GetString(root, "decoder"));
        WriteIndented("stream", FormatStream(root));
        if (TryGetProperty(root, "artifacts", out var artifacts))
        {
            WriteIndented("artifacts", GetString(artifacts, "artifact_root"));
        }

        _console.WriteLine("  Press F10 for help. Ctrl+C closes the session.");
    }

    private void WriteError(JsonElement root)
    {
        if (!TryGetProperty(root, "error", out var error))
        {
            _console.WriteLine("FAIL View failed.");
            return;
        }

        var category = GetString(error, "category") ?? GetString(error, "type") ?? "error";
        var message = GetString(error, "message") ?? "View failed.";
        _console.WriteLine($"FAIL {category}: {message}");
    }

    private static string? FormatStream(JsonElement root)
    {
        if (!TryGetProperty(root, "connection", out var connection))
        {
            return null;
        }

        var width = GetInt(connection, "width");
        var height = GetInt(connection, "height");
        var codec = GetString(connection, "codec");
        if (width is null || height is null)
        {
            return codec;
        }

        return string.IsNullOrWhiteSpace(codec) ? $"{width}x{height}" : $"{width}x{height} {codec}";
    }

    private static string? GetRecordingPath(JsonElement root)
        => GetString(root, "record_path") ?? GetString(root, "path") ?? GetString(root, "output");

    private void WriteIndented(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _console.WriteLine($"  {label}: {value}");
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out property))
        {
            return true;
        }

        property = default;
        return false;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return property.TryGetInt32(out var value) ? value : null;
    }

    private static string? JoinNonEmpty(params string?[] values)
    {
        var parts = values.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return parts.Length == 0 ? null : string.Join(" / ", parts);
    }
}
