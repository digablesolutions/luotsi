using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli;

internal sealed class InspectSession(IDeviceHost deviceHost, IConsoleIo console, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions InputJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly IDeviceHost _deviceHost = deviceHost ?? throw new ArgumentNullException(nameof(deviceHost));
    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<int> RunAsync()
    {
        var sessionId = Guid.NewGuid().ToString("N");

        try
        {
            WriteJsonLine(new
            {
                type = "session_started",
                session_id = sessionId,
                started_at = _timeProvider.GetUtcNow()
            });

            var currentState = await _deviceHost.GetScreenStateAsync().ConfigureAwait(false);
            WriteStateSnapshot(sessionId, null, currentState);

            while (true)
            {
                var line = _console.ReadLine();
                if (line is null)
                {
                    WriteJsonLine(new
                    {
                        type = "session_ended",
                        session_id = sessionId,
                        ended_at = _timeProvider.GetUtcNow(),
                        reason = "stdin_closed"
                    });
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                InspectCommandRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<InspectCommandRequest>(line, InputJsonOptions);
                }
                catch (JsonException ex)
                {
                    WriteJsonLine(new
                    {
                        type = "protocol_error",
                        session_id = sessionId,
                        received_at = _timeProvider.GetUtcNow(),
                        message = ex.Message,
                        raw_line = line
                    });
                    continue;
                }

                if (request is null || string.IsNullOrWhiteSpace(request.Command))
                {
                    WriteJsonLine(new
                    {
                        type = "protocol_error",
                        session_id = sessionId,
                        received_at = _timeProvider.GetUtcNow(),
                        message = "Inspect command must include 'command'.",
                        raw_line = line
                    });
                    continue;
                }

                var normalizedCommand = NormalizeCommand(request.Command);
                if (normalizedCommand is "exit" or "quit")
                {
                    WriteJsonLine(new
                    {
                        type = "session_ended",
                        session_id = sessionId,
                        id = request.Id,
                        ended_at = _timeProvider.GetUtcNow(),
                        reason = "client_exit"
                    });
                    return 0;
                }

                var startedAt = _timeProvider.GetUtcNow();

                try
                {
                    var data = await ExecuteAsync(request, normalizedCommand).ConfigureAwait(false);
                    WriteJsonLine(new
                    {
                        type = "command_result",
                        session_id = sessionId,
                        id = request.Id,
                        command = normalizedCommand,
                        ok = true,
                        started_at = startedAt,
                        ended_at = _timeProvider.GetUtcNow(),
                        data
                    });

                    if (ShouldCaptureScreenState(normalizedCommand))
                    {
                        var nextState = await _deviceHost.GetScreenStateAsync().ConfigureAwait(false);
                        WriteStateSnapshot(sessionId, request.Id, nextState, ScreenStateDelta.Create(currentState, nextState));
                        currentState = nextState;
                    }
                }
                catch (Exception ex)
                {
                    var category = ex is UsageException ? "usage_error" : ErrorInfo.Classify(ex.Message);
                    WriteJsonLine(new
                    {
                        type = "command_result",
                        session_id = sessionId,
                        id = request.Id,
                        command = normalizedCommand,
                        ok = false,
                        started_at = startedAt,
                        ended_at = _timeProvider.GetUtcNow(),
                        error = ErrorInfo.From(ex, category)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            WriteJsonLine(new
            {
                type = "session_error",
                received_at = _timeProvider.GetUtcNow(),
                error = ErrorInfo.From(ex, ErrorInfo.Classify(ex.Message))
            });
            return 1;
        }
    }

    private async Task<object> ExecuteAsync(InspectCommandRequest request, string normalizedCommand)
    {
        return normalizedCommand switch
        {
            "refresh" or "screen_state" or "snapshot" => new { refreshed = true },
            "tap" => await _deviceHost.TapAsync(RequireInt(request.X, "x").ToString(System.Globalization.CultureInfo.InvariantCulture), RequireInt(request.Y, "y").ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false),
            "tap_text" => await _deviceHost.TapTextAsync(RequireText(request.Text, "text"), request.TimeoutSec ?? 15).ConfigureAwait(false),
            "wait_visible" => await _deviceHost.WaitVisibleAsync(RequireText(request.Text, "text"), request.TimeoutSec ?? 15).ConfigureAwait(false),
            "type_text" => await _deviceHost.TypeTextAsync(RequireText(request.Text, "text")).ConfigureAwait(false),
            "keyevent" => await _deviceHost.KeyEventAsync(RequireText(request.Code, "code")).ConfigureAwait(false),
            "telemetry_tail" => await _deviceHost.TelemetryTailAsync(request.Tail ?? 200).ConfigureAwait(false),
            "telemetry_watch" => await _deviceHost.TelemetryWatchAsync(request.TimeoutSec ?? 15).ConfigureAwait(false),
            _ => throw new UsageException($"Unknown inspect command '{request.Command}'.")
        };
    }

    private static string NormalizeCommand(string command) => command.Trim().Replace('-', '_').ToLowerInvariant();

    private static bool ShouldCaptureScreenState(string normalizedCommand) => normalizedCommand is
        "refresh" or
        "screen_state" or
        "snapshot" or
        "tap" or
        "tap_text" or
        "wait_visible" or
        "type_text" or
        "keyevent";

    private static string RequireText(string? value, string optionName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new UsageException($"Inspect command requires '{optionName}'.")
            : value;

    private static int RequireInt(int? value, string optionName) =>
        value ?? throw new UsageException($"Inspect command requires '{optionName}'.");

    private void WriteStateSnapshot(string sessionId, string? requestId, ScreenState state, ScreenStateDelta? delta = null)
    {
        WriteJsonLine(new
        {
            type = delta is null ? "screen_snapshot" : "screen_delta",
            session_id = sessionId,
            id = requestId,
            captured_at = state.CapturedAt,
            screen_hash = ScreenStateDelta.CreateHash(state),
            delta,
            state
        });
    }

    private void WriteJsonLine(object value) => _console.WriteLine(JsonSerializer.Serialize(value, OutputJsonOptions));

    private sealed record InspectCommandRequest(
        string? Id,
        string? Command,
        string? Text,
        string? Code,
        int? TimeoutSec,
        int? Tail,
        int? X,
        int? Y);

    private sealed record ScreenStateDelta(
        string PreviousHash,
        string CurrentHash,
        int AddedCount,
        int RemovedCount,
        int ChangedCount,
        IReadOnlyList<ScreenElement> Added,
        IReadOnlyList<string> Removed,
        IReadOnlyList<ScreenElementChange> Changed)
    {
        public static ScreenStateDelta Create(ScreenState previous, ScreenState current)
        {
            var previousMap = previous.Elements.ToDictionary(GetElementKey, static element => element, StringComparer.Ordinal);
            var currentMap = current.Elements.ToDictionary(GetElementKey, static element => element, StringComparer.Ordinal);

            var added = new List<ScreenElement>();
            var removed = new List<string>();
            var changed = new List<ScreenElementChange>();

            foreach (var pair in currentMap)
            {
                if (!previousMap.TryGetValue(pair.Key, out var previousElement))
                {
                    added.Add(pair.Value);
                    continue;
                }

                if (!Equals(previousElement, pair.Value))
                {
                    changed.Add(new ScreenElementChange(pair.Key, previousElement, pair.Value));
                }
            }

            foreach (var key in previousMap.Keys)
            {
                if (!currentMap.ContainsKey(key))
                {
                    removed.Add(key);
                }
            }

            return new ScreenStateDelta(
                CreateHash(previous),
                CreateHash(current),
                added.Count,
                removed.Count,
                changed.Count,
                added,
                removed,
                changed);
        }

        public static string CreateHash(ScreenState state)
        {
            var builder = new StringBuilder();
            foreach (var element in state.Elements.OrderBy(GetElementKey, StringComparer.Ordinal))
            {
                builder.Append(GetElementKey(element))
                    .Append('|')
                    .Append(element.Text)
                    .Append('|')
                    .Append(element.ContentDescription)
                    .Append('|')
                    .Append(element.ResourceId)
                    .Append('|')
                    .Append(element.ClassName)
                    .Append('|')
                    .Append(element.Enabled)
                    .Append('|')
                    .Append(element.Clickable)
                    .Append('|')
                    .Append(element.Left)
                    .Append(',')
                    .Append(element.Top)
                    .Append(',')
                    .Append(element.Right)
                    .Append(',')
                    .Append(element.Bottom)
                    .AppendLine();
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string GetElementKey(ScreenElement element) =>
            !string.IsNullOrWhiteSpace(element.StableId)
                ? element.StableId
                : string.Join('|', element.ClassName, element.Left, element.Top, element.Right, element.Bottom, element.Text, element.ContentDescription);
    }

    private sealed record ScreenElementChange(string StableId, ScreenElement Previous, ScreenElement Current);
}