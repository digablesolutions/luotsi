using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewSessionInputCommandHandler
{
    private readonly IDeviceHost _deviceHost;
    private readonly ArtifactSession _artifacts;
    private readonly ViewOptions _options;
    private readonly SessionControlledViewRecorder _recorder;
    private readonly TimeProvider _timeProvider;
    private readonly string _sessionId;
    private readonly Action<object> _writeEvent;
    private readonly Func<Task> _publishChromeAsync;
    private readonly Func<string> _activeDeviceSelector;
    private readonly Func<string, string?, bool> _requestReconnect;
    private readonly IArtifactFolderOpener _artifactFolderOpener;

    private bool _initialRecordingStarted;
    private int _screenshotSequence;
    private int _recordingSequence;
    private bool _streamPaused;
    private Action<bool>? _streamPauseUpdater;

    public ViewSessionInputCommandHandler(ViewSessionInteractionContext context, ViewSessionInteractionCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(callbacks);

        _deviceHost = context.DeviceHost ?? throw new ArgumentNullException(nameof(context.DeviceHost));
        _artifacts = context.Artifacts ?? throw new ArgumentNullException(nameof(context.Artifacts));
        _options = context.Options ?? throw new ArgumentNullException(nameof(context.Options));
        _recorder = context.Recorder ?? throw new ArgumentNullException(nameof(context.Recorder));
        _timeProvider = context.TimeProvider ?? throw new ArgumentNullException(nameof(context.TimeProvider));
        _sessionId = string.IsNullOrWhiteSpace(context.SessionId)
            ? throw new ArgumentException("Session id is required.", nameof(context.SessionId))
            : context.SessionId;
        _writeEvent = context.WriteEvent ?? throw new ArgumentNullException(nameof(context.WriteEvent));
        _artifactFolderOpener = context.ArtifactFolderOpener ?? throw new ArgumentNullException(nameof(context.ArtifactFolderOpener));
        _publishChromeAsync = callbacks.PublishChromeAsync ?? throw new ArgumentNullException(nameof(callbacks.PublishChromeAsync));
        _activeDeviceSelector = callbacks.ActiveDeviceSelector ?? throw new ArgumentNullException(nameof(callbacks.ActiveDeviceSelector));
        _requestReconnect = callbacks.RequestReconnect ?? throw new ArgumentNullException(nameof(callbacks.RequestReconnect));
    }

    public void AttachConnection(ViewConnectionInfo connectionInfo) => _ = _recorder.InitializeAsync(connectionInfo);

    public void AttachStreamPauseUpdater(Action<bool> streamPauseUpdater) => _streamPauseUpdater = streamPauseUpdater;

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

        var recordPath = _recorder.ActiveRecordPath;
        await _recorder.StopAsync().ConfigureAwait(false);
        await _publishChromeAsync().ConfigureAwait(false);
        WriteEvent(new
        {
            type = SessionEventTypes.View.RecordingStopped,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            record_path = recordPath,
            reason = "reconnect"
        });
    }

    public async Task<bool> TryHandleAsync(ViewInteractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request)
        {
            case ViewTapRequest tapRequest:
                if (TryBlockReadOnly("tap"))
                {
                    return true;
                }

                await _deviceHost.TapPointAsync("view-window", null, null, tapRequest.XRatio, tapRequest.YRatio, 0).ConfigureAwait(false);
                return true;

            case ViewWindowCommandRequest windowCommandRequest:
                await HandleCommandAsync(windowCommandRequest.Command).ConfigureAwait(false);
                return true;

            case ViewTextInputRequest textInputRequest:
                if (TryBlockReadOnly("text_input"))
                {
                    return true;
                }

                await _deviceHost.TypeTextAsync(textInputRequest.Text).ConfigureAwait(false);
                return true;

            case ViewKeyInputRequest keyInputRequest:
                if (TryBlockReadOnly("key_input"))
                {
                    return true;
                }

                await _deviceHost.KeyEventAsync(keyInputRequest.Code).ConfigureAwait(false);
                return true;

            case ViewScrollRequest scrollRequest:
                if (TryBlockReadOnly("scroll"))
                {
                    return true;
                }

                await _deviceHost.ScrollAsync(scrollRequest.HorizontalTicks, scrollRequest.VerticalTicks).ConfigureAwait(false);
                return true;

            case ViewClipboardPasteRequest clipboardPasteRequest:
                if (TryBlockReadOnly("clipboard"))
                {
                    return true;
                }

                await _deviceHost.TypeTextAsync(clipboardPasteRequest.Text).ConfigureAwait(false);
                WriteEvent(new
                {
                    type = SessionEventTypes.View.ClipboardPasted,
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    length = clipboardPasteRequest.Text.Length
                });
                return true;

            case ViewFileDropRequest fileDropRequest:
                if (TryBlockReadOnly("file_drop"))
                {
                    return true;
                }

                await HandleFileDropAsync(fileDropRequest.FilePath).ConfigureAwait(false);
                return true;

            case ViewFilePullRequest filePullRequest:
                if (TryBlockReadOnly("file_pull"))
                {
                    return true;
                }

                await HandleFilePullAsync(filePullRequest).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    private async Task HandleCommandAsync(ViewWindowCommand command)
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

                await ToggleRecordingAsync().ConfigureAwait(false);
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

    private async Task ToggleRecordingAsync()
    {
        if (_recorder.IsRecording)
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
                reason = "operator"
            });
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
        return Path.Combine(directory, safeFileName);
    }

    private async Task HandleFileDropAsync(string filePath)
    {
        if (string.Equals(Path.GetExtension(filePath), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            var installResult = await _deviceHost.InstallPackageAsync(filePath).ConfigureAwait(false);
            WriteEvent(new
            {
                type = SessionEventTypes.View.PackageInstalled,
                session_id = _sessionId,
                occurred_at = _timeProvider.GetUtcNow(),
                package_path = installResult.PackagePath
            });
            return;
        }

        var pushResult = await _deviceHost.PushFileAsync(filePath).ConfigureAwait(false);
        WriteEvent(new
        {
            type = SessionEventTypes.View.FilePushed,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            local_path = pushResult.LocalPath,
            remote_path = pushResult.RemotePath
        });
    }

    private async Task HandleFilePullAsync(ViewFilePullRequest request)
    {
        var pullResult = await _deviceHost.PullFileAsync(request.RemotePath, request.LocalDirectory ?? _artifacts.Root).ConfigureAwait(false);
        WriteEvent(new
        {
            type = SessionEventTypes.View.FilePulled,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            remote_path = pullResult.RemotePath,
            local_path = pullResult.LocalPath
        });
    }

    private async Task SendDeviceKeyAsync(string keyCode, string command)
    {
        if (TryBlockReadOnly(command))
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

    private bool TryBlockReadOnly(string requestType)
    {
        if (!_options.ReadOnly)
        {
            return false;
        }

        WriteEvent(new
        {
            type = SessionEventTypes.View.InputBlocked,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            request_type = requestType,
            reason = "read_only"
        });
        return true;
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