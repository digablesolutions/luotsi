using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli.Inspect;

internal sealed class InspectSessionProtocol(IConsoleIo console, Action<string>? onWriteJsonLine = null)
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

    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));

    public string? ReadLine() => _console.ReadLine();

    public ParseInspectCommandResult ParseCommand(string line)
    {
        try
        {
            var request = JsonSerializer.Deserialize<InspectCommandRequest>(line, InputJsonOptions);
            if (request is null || string.IsNullOrWhiteSpace(request.Command))
            {
                return ParseInspectCommandResult.ProtocolError("Inspect command must include 'command'.", line);
            }

            return ParseInspectCommandResult.Success(request);
        }
        catch (JsonException ex)
        {
            return ParseInspectCommandResult.ProtocolError(ex.Message, line);
        }
    }

    public void WriteSessionStarted(string sessionId, DateTimeOffset startedAt) =>
        WriteJsonLine(new
        {
            type = SessionEventTypes.Inspect.SessionStarted,
            session_id = sessionId,
            started_at = startedAt
        });

    public void WriteSessionEnded(string sessionId, string? requestId, DateTimeOffset endedAt, string reason) =>
        WriteJsonLine(new
        {
            type = SessionEventTypes.Inspect.SessionEnded,
            session_id = sessionId,
            id = requestId,
            ended_at = endedAt,
            reason
        });

    public void WriteProtocolError(string sessionId, DateTimeOffset receivedAt, string message, string rawLine) =>
        WriteJsonLine(new
        {
            type = SessionEventTypes.Inspect.ProtocolError,
            session_id = sessionId,
            received_at = receivedAt,
            message,
            raw_line = rawLine
        });

    public void WriteCommandResult(string sessionId, string? requestId, string command, bool ok, DateTimeOffset startedAt, DateTimeOffset endedAt, object? data = null, ErrorInfo? error = null) =>
        WriteJsonLine(new
        {
            type = SessionEventTypes.Inspect.CommandResult,
            session_id = sessionId,
            id = requestId,
            command,
            ok,
            started_at = startedAt,
            ended_at = endedAt,
            data,
            error
        });

    public void WriteStateSnapshot(string sessionId, string? requestId, ScreenState state, InspectScreenStateDelta? delta = null) =>
        WriteJsonLine(new
        {
            type = delta is null ? SessionEventTypes.Inspect.ScreenSnapshot : SessionEventTypes.Inspect.ScreenDelta,
            session_id = sessionId,
            id = requestId,
            captured_at = state.CapturedAt,
            screen_hash = InspectScreenStateDelta.CreateHash(state),
            delta,
            state
        });

    public void WriteSessionError(DateTimeOffset receivedAt, Exception exception, string? sessionId = null, string? requestId = null)
    {
        var category = exception is ICommandFailureDetails failure
            ? failure.CategoryOverride
            : ErrorInfo.Classify(exception.Message);
        WriteJsonLine(new
        {
            type = SessionEventTypes.Inspect.SessionError,
            session_id = sessionId,
            id = requestId,
            received_at = receivedAt,
            error = ErrorInfo.From(exception, category)
        });
    }

    private void WriteJsonLine(object value)
    {
        var json = JsonSerializer.Serialize(value, OutputJsonOptions);
        _console.WriteLine(json);
        onWriteJsonLine?.Invoke(json);
    }
}

internal sealed record InspectCommandRequest(
    string? Id,
    string? Command,
    string? Text,
    string? Label,
    string? Code,
    string? Output,
    int? TimeoutSec,
    int? TimeLimitSec,
    int? Tail,
    int? X,
    int? Y);

internal sealed record ParseInspectCommandResult(InspectCommandRequest? Request, string? ErrorMessage, string? RawLine)
{
    public bool IsSuccess => Request is not null;

    public static ParseInspectCommandResult Success(InspectCommandRequest request) =>
        new(request, null, null);

    public static ParseInspectCommandResult ProtocolError(string message, string rawLine) =>
        new(null, message, rawLine);
}
