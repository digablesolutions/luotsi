using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewSessionRecordingCoordinator
{
    private readonly ArtifactSession _artifacts;
    private readonly ViewOptions _options;
    private readonly SessionControlledViewRecorder _recorder;
    private readonly TimeProvider _timeProvider;
    private readonly string _sessionId;
    private readonly Action<object> _writeEvent;
    private readonly Func<Task> _publishChromeAsync;

    private bool _initialRecordingStarted;
    private bool _resumeRecordingAfterReconnect;
    private int _recordingSequence;

    public ViewSessionRecordingCoordinator(ViewSessionRecordingContext context, Func<Task> publishChromeAsync)
    {
        ArgumentNullException.ThrowIfNull(context);

        _artifacts = context.Artifacts ?? throw new ArgumentNullException(nameof(context.Artifacts));
        _options = context.Options ?? throw new ArgumentNullException(nameof(context.Options));
        _recorder = context.Recorder ?? throw new ArgumentNullException(nameof(context.Recorder));
        _timeProvider = context.Events.TimeProvider ?? throw new ArgumentNullException(nameof(context.Events.TimeProvider));
        _sessionId = string.IsNullOrWhiteSpace(context.Events.SessionId)
            ? throw new ArgumentException("Session id is required.", nameof(context.Events.SessionId))
            : context.Events.SessionId;
        _writeEvent = context.Events.WriteEvent ?? throw new ArgumentNullException(nameof(context.Events.WriteEvent));
        _publishChromeAsync = publishChromeAsync ?? throw new ArgumentNullException(nameof(publishChromeAsync));
    }

    public void AttachConnection(ViewConnectionInfo connectionInfo) => _ = _recorder.InitializeAsync(connectionInfo);

    public async Task StartInitialRecordingIfNeededAsync()
    {
        if (_initialRecordingStarted || string.IsNullOrWhiteSpace(_options.RecordPath))
        {
            return;
        }

        _initialRecordingStarted = true;
        await _recorder.StartAsync(_options.RecordPath).ConfigureAwait(false);
        await _publishChromeAsync().ConfigureAwait(false);
        WriteEvent(new
        {
            type = SessionEventTypes.View.RecordingStarted,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            record_path = _options.RecordPath,
            source = "startup"
        });
    }

    public async Task StopRecordingForReconnectAsync()
    {
        if (!_recorder.IsRecording)
        {
            return;
        }

        _resumeRecordingAfterReconnect = true;
        await StopRecordingAsync("reconnect").ConfigureAwait(false);
    }

    public async Task ResumeRecordingAfterReconnectIfNeededAsync()
    {
        if (!_resumeRecordingAfterReconnect)
        {
            return;
        }

        _resumeRecordingAfterReconnect = false;
        var nextPath = BuildNextRecordingPath();
        await _recorder.StartAsync(nextPath).ConfigureAwait(false);
        await _publishChromeAsync().ConfigureAwait(false);
        WriteEvent(new
        {
            type = SessionEventTypes.View.RecordingStarted,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            record_path = nextPath,
            source = "reconnect"
        });
    }

    public async Task ToggleRecordingAsync()
    {
        if (_recorder.IsRecording)
        {
            await StopRecordingAsync("operator").ConfigureAwait(false);
            return;
        }

        var nextPath = BuildNextRecordingPath();
        await _recorder.StartAsync(nextPath).ConfigureAwait(false);
        await _publishChromeAsync().ConfigureAwait(false);
        WriteEvent(new
        {
            type = SessionEventTypes.View.RecordingStarted,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            record_path = nextPath,
            source = "operator"
        });
    }

    private async Task StopRecordingAsync(string reason)
    {
        var recordPath = _recorder.ActiveRecordPath;
        await _recorder.StopAsync().ConfigureAwait(false);
        await _publishChromeAsync().ConfigureAwait(false);
        WriteEvent(new
        {
            type = SessionEventTypes.View.RecordingStopped,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            record_path = recordPath,
            reason
        });
    }

    private string BuildNextRecordingPath()
    {
        var sequence = Interlocked.Increment(ref _recordingSequence);
        if (sequence == 1 && !string.IsNullOrWhiteSpace(_options.RecordPath) && !_initialRecordingStarted)
        {
            return _options.RecordPath;
        }

        var preferredPath = _options.RecordPath;
        var extension = string.IsNullOrWhiteSpace(preferredPath) ? ".h264" : Path.GetExtension(preferredPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".h264";
        }

        var directory = string.IsNullOrWhiteSpace(preferredPath)
            ? _artifacts.Root
            : Path.GetDirectoryName(Path.GetFullPath(preferredPath)) ?? _artifacts.Root;
        var fileBaseName = string.IsNullOrWhiteSpace(preferredPath)
            ? "view-window-record"
            : Path.GetFileNameWithoutExtension(preferredPath);
        var safeFileName = Path.GetFileName($"{fileBaseName}-{sequence:000}{extension}");
        if (Path.IsPathRooted(safeFileName))
        {
            throw new InvalidOperationException("Recording file name must be relative.");
        }

        return Path.Join(directory, safeFileName);
    }

    private void WriteEvent(object value) => _writeEvent(value);
}
