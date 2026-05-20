using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewSessionWindowCommandHandler
{
    private readonly IDeviceHost _deviceHost;
    private readonly ArtifactSession _artifacts;
    private readonly ViewOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _sessionId;
    private readonly Action<object> _writeEvent;
    private readonly Func<string> _activeDeviceSelector;
    private readonly Func<string, string?, bool> _requestReconnect;
    private readonly IArtifactFolderOpener _artifactFolderOpener;
    private readonly ViewSessionRecordingCoordinator _recording;
    private readonly Func<string, bool> _tryBlockReadOnly;

    private int _screenshotSequence;
    private bool _streamPaused;
    private Action<bool>? _streamPauseUpdater;

    public ViewSessionWindowCommandHandler(
        ViewSessionInteractionContext context,
        ViewSessionRecordingCoordinator recording,
        ViewSessionInteractionCallbacks callbacks,
        Func<string, bool> tryBlockReadOnly)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(tryBlockReadOnly);

        _deviceHost = context.DeviceHost ?? throw new ArgumentNullException(nameof(context.DeviceHost));
        _artifacts = context.Artifacts ?? throw new ArgumentNullException(nameof(context.Artifacts));
        _options = context.Options ?? throw new ArgumentNullException(nameof(context.Options));
        _timeProvider = context.TimeProvider ?? throw new ArgumentNullException(nameof(context.TimeProvider));
        _sessionId = string.IsNullOrWhiteSpace(context.SessionId)
            ? throw new ArgumentException("Session id is required.", nameof(context.SessionId))
            : context.SessionId;
        _writeEvent = context.WriteEvent ?? throw new ArgumentNullException(nameof(context.WriteEvent));
        _artifactFolderOpener = context.ArtifactFolderOpener ?? throw new ArgumentNullException(nameof(context.ArtifactFolderOpener));
        _recording = recording;
        _activeDeviceSelector = callbacks.ActiveDeviceSelector ?? throw new ArgumentNullException(nameof(callbacks.ActiveDeviceSelector));
        _requestReconnect = callbacks.RequestReconnect ?? throw new ArgumentNullException(nameof(callbacks.RequestReconnect));
        _tryBlockReadOnly = tryBlockReadOnly;
    }

    public void AttachStreamPauseUpdater(Action<bool> streamPauseUpdater) => _streamPauseUpdater = streamPauseUpdater;

    public async Task HandleAsync(ViewWindowCommand command)
    {
        switch (command)
        {
            case ViewWindowCommand.TakeScreenshot:
                if (TryBlockUnsupported("screenshot", "observer_session", !string.IsNullOrWhiteSpace(_options.JoinShareEndpoint)))
                {
                    return;
                }

                var label = $"view-window-{Interlocked.Increment(ref _screenshotSequence):000}";
                var result = await _deviceHost.TakeScreenshotAsync(label).ConfigureAwait(false);
                WriteEvent(new
                {
                    type = SessionEventTypes.View.ScreenshotCaptured,
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    label = result.Label,
                    file = result.File
                });
                return;

            case ViewWindowCommand.ToggleRecording:
                if (TryBlockUnsupported("recording", "observer_session", !string.IsNullOrWhiteSpace(_options.JoinShareEndpoint)))
                {
                    return;
                }

                await _recording.ToggleRecordingAsync().ConfigureAwait(false);
                return;

            case ViewWindowCommand.Reconnect:
                _requestReconnect("operator", null);
                return;

            case ViewWindowCommand.Back:
                await SendDeviceKeyAsync("KEYCODE_BACK", "back").ConfigureAwait(false);
                return;

            case ViewWindowCommand.Home:
                await SendDeviceKeyAsync("KEYCODE_HOME", "home").ConfigureAwait(false);
                return;

            case ViewWindowCommand.Recents:
                await SendDeviceKeyAsync("KEYCODE_APP_SWITCH", "recents").ConfigureAwait(false);
                return;

            case ViewWindowCommand.OpenArtifacts:
                await _artifactFolderOpener.OpenAsync(_artifacts.Root).ConfigureAwait(false);
                WriteEvent(new
                {
                    type = SessionEventTypes.View.ArtifactsOpened,
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    artifact_root = _artifacts.Root
                });
                return;

            case ViewWindowCommand.Rotate:
                await SendDeviceKeyAsync("KEYCODE_ROTATE_SCREEN", "rotate").ConfigureAwait(false);
                return;

            case ViewWindowCommand.PauseStream:
                _streamPaused = !_streamPaused;
                _streamPauseUpdater?.Invoke(_streamPaused);
                WriteEvent(new
                {
                    type = _streamPaused ? SessionEventTypes.View.StreamPaused : SessionEventTypes.View.StreamResumed,
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    device = _activeDeviceSelector()
                });
                return;

            default:
                return;
        }
    }

    private async Task SendDeviceKeyAsync(string keyCode, string command)
    {
        if (_tryBlockReadOnly(command))
        {
            return;
        }

        await _deviceHost.KeyEventAsync(keyCode).ConfigureAwait(false);
        WriteEvent(new
        {
            type = SessionEventTypes.View.KeyCommandSent,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            command,
            code = keyCode
        });
    }

    private bool TryBlockUnsupported(string requestType, string reason, bool unsupported)
    {
        if (!unsupported)
        {
            return false;
        }

        WriteEvent(new
        {
            type = SessionEventTypes.View.InputBlocked,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            request_type = requestType,
            reason
        });
        return true;
    }

    private void WriteEvent(object value) => _writeEvent(value);
}