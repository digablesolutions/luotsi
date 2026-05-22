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
    private readonly List<string> _eventLines = [];
    private readonly HashSet<string> _eventTypes = new(StringComparer.Ordinal);
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

        _eventLines.Add(jsonLine);
        try
        {
            using var document = JsonDocument.Parse(jsonLine);
            if (document.RootElement.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
            {
                var type = typeElement.GetString();
                if (!string.IsNullOrWhiteSpace(type))
                {
                    _eventTypes.Add(type);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    public async Task PersistAsync(DateTimeOffset endedAt, string reason, int exitCode)
    {
        var timeline = _eventLines.Count == 0 ? string.Empty : string.Join('\n', _eventLines) + "\n";
        await _artifacts.WriteTextAsync(TimelineFileName, timeline).ConfigureAwait(false);
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
            _eventLines.Count,
            _eventTypes.Order(StringComparer.Ordinal).ToArray())).ConfigureAwait(false);
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