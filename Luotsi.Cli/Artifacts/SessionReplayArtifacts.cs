using System.Text;
using System.Text.Json;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Artifacts;

internal sealed class SessionReplayArtifacts(ArtifactSession artifacts, string sessionKind, string sessionId, DateTimeOffset startedAt)
{
    internal const string TimelineFileName = "session-timeline.jsonl";
    internal const string MetadataFileName = "session-replay.json";

    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly string _sessionKind = string.IsNullOrWhiteSpace(sessionKind) ? throw new ArgumentException("Session kind must be provided.", nameof(sessionKind)) : sessionKind;
    private readonly string _sessionId = string.IsNullOrWhiteSpace(sessionId) ? throw new ArgumentException("Session id must be provided.", nameof(sessionId)) : sessionId;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _eventTypes = new(StringComparer.Ordinal);
    private readonly StreamWriter _timelineWriter = new(artifacts.OpenArtifactWrite(TimelineFileName), new UTF8Encoding(false))
    {
        AutoFlush = true
    };
    private int _eventCount;
    private bool _timelineDisposed;
    private string? _target;

    public void SetTarget(string? target)
    {
        if (!string.IsNullOrWhiteSpace(target))
        {
            _target = target;
        }
    }

    public void RecordSerializedEvent(string jsonLine)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            return;
        }

        var eventType = TryGetEventType(jsonLine);

        lock (_gate)
        {
            ThrowIfPersisted();
            _timelineWriter.WriteLine(jsonLine);
            _eventCount++;
            if (!string.IsNullOrWhiteSpace(eventType))
            {
                _eventTypes.Add(eventType);
            }
        }
    }

    public async Task PersistAsync(DateTimeOffset endedAt, string reason, int exitCode)
    {
        int eventCount;
        string[] eventTypes;

        lock (_gate)
        {
            if (!_timelineDisposed)
            {
                _timelineWriter.Dispose();
                _timelineDisposed = true;
            }

            eventCount = _eventCount;
            eventTypes = _eventTypes.Order(StringComparer.Ordinal).ToArray();
        }

        await _artifacts.WriteJsonAsync(MetadataFileName, new SessionReplayMetadata(
            ResultSchemas.SessionReplay,
            _sessionKind,
            _sessionId,
            startedAt,
            endedAt,
            reason,
            exitCode,
            _target,
            TimelineFileName,
            eventCount,
            eventTypes)).ConfigureAwait(false);
    }

    private static string? TryGetEventType(string jsonLine)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonLine);
            if (document.RootElement.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
            {
                var type = typeElement.GetString();
                return string.IsNullOrWhiteSpace(type) ? null : type;
            }

            return null;
        }
        catch (JsonException)
        {
            return "invalid-json";
        }
    }

    private void ThrowIfPersisted()
    {
        if (_timelineDisposed)
        {
            throw new InvalidOperationException("Replay timeline recording has already been finalized.");
        }
    }
}

internal sealed record SessionReplayMetadata(
    string Schema,
    string SessionKind,
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Reason,
    int ExitCode,
    string? Target,
    string TimelineFileName,
    int EventCount,
    IReadOnlyList<string> EventTypes);