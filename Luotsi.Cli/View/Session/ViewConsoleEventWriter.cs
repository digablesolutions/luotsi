using System.Text.Json;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.System;
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
                _console.WriteLine($"{Warn()} Falling back from {GetString(root, \"failed_capture_backend\") ?? \"unknown\"} to {GetString(root, \"fallback_capture_backend\") ?? \"unknown\"}: {GetString(root, \"reason\") ?? \"no reason reported\"}");
                break;
            case SessionEventTypes.View.Diagnostic:
                _console.WriteLine($"{Warn()} {GetString(root, "message") ?? "View diagnostic reported."}");
                WriteIndented("Next", GetString(root, "next_command"));
                break;
            case SessionEventTypes.View.Error:
                WriteError(root);
                break;
            case SessionEventTypes.View.Ended:
                _console.WriteLine($"View ended: {GetString(root, "reason") ?? "unknown"}");
                break;
            case SessionEventTypes.View.Reconnected:
                _console.WriteLine($"{Ok()} View reconnected to {GetString(root, "device") ?? "device"}.");
                break;
            case SessionEventTypes.View.ShareStarted:
                _console.WriteLine($"{Ok()} View sharing at {GetString(root, "endpoint") ?? "unknown endpoint"}.");
                break;
            case SessionEventTypes.View.ShareClientConnected:
                _console.WriteLine($"{Ok()} Share client connected: {GetString(root, "remote_endpoint") ?? "unknown client"}.");
                break;
            case SessionEventTypes.View.ShareClientDisconnected:
                _console.WriteLine($"{Muted("-- ")} Share client disconnected: {GetString(root, "remote_endpoint") ?? "unknown client"}.");
                break;
            case SessionEventTypes.View.RecordingStarted:
                _console.WriteLine($"{Ok()} Recording started: {Accent(GetRecordingPath(root) ?? "recording output")}.");
                break;
            case SessionEventTypes.View.RecordingStopped:
                _console.WriteLine($"{Ok()} Recording stopped: {Accent(GetRecordingPath(root) ?? "recording output")}.");
                break;
            case SessionEventTypes.View.ScreenshotCaptured:
                _console.WriteLine($"{Ok()} Screenshot captured: {Accent(GetString(root, "path") ?? "screenshot output")}.");
                break;
            case SessionEventTypes.View.ReconnectRequested:
                _console.WriteLine($"{Muted("-- ")} Reconnect requested: {GetString(root, "reason") ?? "no reason reported"}.");
                break;
            case SessionEventTypes.View.StreamPaused:
                _console.WriteLine($"{Muted("-- ")} View stream paused.");
                break;
            case SessionEventTypes.View.StreamResumed:
                _console.WriteLine($"{Ok()} View stream resumed.");
                break;
            case SessionEventTypes.View.InteractionFailed:
                _console.WriteLine($"{Warn()} Interaction failed: {GetString(root, "message") ?? GetString(root, "reason") ?? "no reason reported"}");
                break;
            case SessionEventTypes.View.InputBlocked:
                _console.WriteLine($"{Warn()} Input blocked: {GetString(root, "reason") ?? "read-only session"}.");
                break;
            case SessionEventTypes.View.Stats:
            case SessionEventTypes.View.DeviceShelf:
                break;
            default:
                if (!string.IsNullOrWhiteSpace(type))
                {
                    _console.WriteLine($"{Muted("-- ")} {type}");
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
            ViewStartupPhaseStatus.Succeeded => Ok(),
            ViewStartupPhaseStatus.Failed => Fail(),
            ViewStartupPhaseStatus.Skipped => Muted("-- "),
            _ => Muted(".. ")
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
        _console.WriteLine(ConsoleStyling.Accent(_console, "View started"));
        WriteIndented("device", GetString(root, "device"));
        WriteIndented("backend", JoinNonEmpty(GetString(root, "capture_backend"), GetString(root, "backend")));
        WriteIndented("decoder", GetString(root, "decoder"));
        WriteIndented("stream", FormatStream(root));
        if (TryGetProperty(root, "artifacts", out var artifacts))
        {
            WriteIndented("artifacts", GetString(artifacts, "artifact_root"));
        }

        _console.WriteLine($"  {ConsoleStyling.Muted(_console, "Press F10 for help. Ctrl+C closes the session.")}");
    }

    private void WriteError(JsonElement root)
    {
        if (!TryGetProperty(root, "error", out var error))
        {
            _console.WriteLine($"{Fail()} View failed.");
            return;
        }

        var category = GetString(error, "category") ?? GetString(error, "type") ?? "error";
        var message = GetString(error, "message") ?? "View failed.";
        _console.WriteLine($"{Fail()} {category}: {message}");
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
            _console.WriteLine($"  {ConsoleStyling.Muted(_console, label)}: {StyleIndentedValue(label, value)}");
        }
    }

    private string Ok() => ConsoleStyling.Success(_console, "OK ");

    private string Warn() => ConsoleStyling.Warning(_console, "WARN");

    private string Fail() => ConsoleStyling.Failure(_console, "FAIL");

    private string Muted(string value) => ConsoleStyling.Muted(_console, value);

    private string Accent(string value) => ConsoleStyling.Accent(_console, value);

    private string StyleIndentedValue(string label, string value)
        => label.Equals("artifacts", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("Next", StringComparison.OrdinalIgnoreCase)
                ? ConsoleStyling.Accent(_console, value)
                : value;

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
