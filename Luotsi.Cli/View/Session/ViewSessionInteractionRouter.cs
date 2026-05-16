using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Backends.Ffmpeg;

namespace Luotsi.Cli.View;

internal sealed class ViewSessionInteractionRouter(
    IDeviceHost deviceHost,
    ArtifactSession artifacts,
    ViewOptions options,
    SessionControlledViewRecorder recorder,
    TimeProvider timeProvider,
    string sessionId,
    Action<object> writeJsonLine)
{
    private readonly IDeviceHost _deviceHost = deviceHost ?? throw new ArgumentNullException(nameof(deviceHost));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly ViewOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly SessionControlledViewRecorder _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly string _sessionId = string.IsNullOrWhiteSpace(sessionId) ? throw new ArgumentException("Session id is required.", nameof(sessionId)) : sessionId;
    private readonly Action<object> _writeJsonLine = writeJsonLine ?? throw new ArgumentNullException(nameof(writeJsonLine));

    private CancellationTokenSource? _iterationCancellation;
    private TaskCompletionSource _reconnectRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _initialRecordingStarted;
    private int _screenshotSequence;
    private int _recordingSequence;
    private Func<ViewChromeState, Task>? _chromeUpdater;
    private IReadOnlyList<ViewChromeDevice> _devices = [];
    private string? _shareEndpoint = options.JoinShareEndpoint;
    private int _observerCount;

    public string ActiveDeviceSelector { get; private set; } = options.DeviceSelector;

    public void BeginIteration(string deviceSelector, CancellationTokenSource iterationCancellation)
    {
        ActiveDeviceSelector = string.IsNullOrWhiteSpace(deviceSelector) ? _options.DeviceSelector : deviceSelector;
        _iterationCancellation = iterationCancellation;
        UpdateActiveDeviceFlags();
    }

    public void AttachConnection(ViewConnectionInfo connectionInfo) => _ = _recorder.InitializeAsync(connectionInfo);

    public void AttachChromeUpdater(Func<ViewChromeState, Task> chromeUpdater) => _chromeUpdater = chromeUpdater;

    public Task WaitForReconnectAsync() => _reconnectRequested.Task;

    public void ResetReconnectSignal() => _reconnectRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task StartInitialRecordingIfNeededAsync()
    {
        if (_initialRecordingStarted || string.IsNullOrWhiteSpace(_options.RecordPath))
        {
            return;
        }

        _initialRecordingStarted = true;
        await _recorder.StartAsync(_options.RecordPath).ConfigureAwait(false);
        await PublishChromeAsync().ConfigureAwait(false);
        WriteEvent(new
        {
            type = "view_recording_started",
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
        await PublishChromeAsync().ConfigureAwait(false);
        WriteEvent(new
        {
            type = "view_recording_stopped",
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            record_path = recordPath,
            reason = "reconnect"
        });
    }

    public async Task HandleAsync(ViewInteractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request)
        {
            case ViewTapRequest tapRequest:
                if (TryBlockReadOnly("tap"))
                {
                    return;
                }

                await _deviceHost.TapPointAsync("view-window", null, null, tapRequest.XRatio, tapRequest.YRatio, 0).ConfigureAwait(false);
                break;

            case ViewWindowCommandRequest windowCommandRequest:
                await HandleCommandAsync(windowCommandRequest.Command).ConfigureAwait(false);
                break;

            case ViewTextInputRequest textInputRequest:
                if (TryBlockReadOnly("text_input"))
                {
                    return;
                }

                await _deviceHost.TypeTextAsync(textInputRequest.Text).ConfigureAwait(false);
                break;

            case ViewKeyInputRequest keyInputRequest:
                if (TryBlockReadOnly("key_input"))
                {
                    return;
                }

                await _deviceHost.KeyEventAsync(keyInputRequest.Code).ConfigureAwait(false);
                break;

            case ViewScrollRequest scrollRequest:
                if (TryBlockReadOnly("scroll"))
                {
                    return;
                }

                await _deviceHost.ScrollAsync(scrollRequest.HorizontalTicks, scrollRequest.VerticalTicks).ConfigureAwait(false);
                break;

            case ViewClipboardPasteRequest clipboardPasteRequest:
                if (TryBlockReadOnly("clipboard"))
                {
                    return;
                }

                await _deviceHost.TypeTextAsync(clipboardPasteRequest.Text).ConfigureAwait(false);
                WriteEvent(new
                {
                    type = "view_clipboard_pasted",
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    length = clipboardPasteRequest.Text.Length
                });
                break;

            case ViewFileDropRequest fileDropRequest:
                if (TryBlockReadOnly("file_drop"))
                {
                    return;
                }

                await HandleFileDropAsync(fileDropRequest.FilePath).ConfigureAwait(false);
                break;

            case ViewFilePullRequest filePullRequest:
                if (TryBlockReadOnly("file_pull"))
                {
                    return;
                }

                await HandleFilePullAsync(filePullRequest).ConfigureAwait(false);
                break;

            case ViewSwitchDeviceRequest switchDeviceRequest:
                await HandleDeviceSwitchAsync(switchDeviceRequest).ConfigureAwait(false);
                break;

            case ViewInteractionFailedRequest failedRequest:
                WriteEvent(new
                {
                    type = "view_interaction_failed",
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    request_type = failedRequest.FailedRequestType,
                    exception_type = failedRequest.ExceptionType,
                    message = failedRequest.Message
                });
                break;

            default:
                throw new InvalidOperationException($"Unsupported view interaction request '{request.GetType().Name}'.");
        }
    }

    private async Task HandleDeviceSwitchAsync(ViewSwitchDeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceSelector))
        {
            throw new UsageException("device switch requires a non-empty device selector.");
        }

        if (string.Equals(request.DeviceSelector, ActiveDeviceSelector, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        WriteEvent(new
        {
            type = "view_device_switch_requested",
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            from_device = ActiveDeviceSelector,
            to_device = request.DeviceSelector
        });
        ActiveDeviceSelector = request.DeviceSelector;
        UpdateActiveDeviceFlags();
        await PublishChromeAsync().ConfigureAwait(false);
        _reconnectRequested.TrySetResult();
        _iterationCancellation?.Cancel();
    }

    private async Task HandleCommandAsync(ViewWindowCommand command)
    {
        switch (command)
        {
            case ViewWindowCommand.TakeScreenshot:
                if (TryBlockUnsupported("screenshot", "observer_session", !string.IsNullOrWhiteSpace(_options.JoinShareEndpoint)))
                {
                    break;
                }

            {
                var label = $"view-window-{Interlocked.Increment(ref _screenshotSequence):000}";
                var result = await _deviceHost.TakeScreenshotAsync(label).ConfigureAwait(false);
                WriteEvent(new
                {
                    type = "view_screenshot_captured",
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    label = result.Label,
                    file = result.File
                });
                break;
            }

            case ViewWindowCommand.ToggleRecording:
                if (TryBlockUnsupported("recording", "observer_session", !string.IsNullOrWhiteSpace(_options.JoinShareEndpoint)))
                {
                    break;
                }

                await ToggleRecordingAsync().ConfigureAwait(false);
                break;

            case ViewWindowCommand.Reconnect:
                WriteEvent(new
                {
                    type = "view_reconnect_requested",
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    device = ActiveDeviceSelector
                });
                _reconnectRequested.TrySetResult();
                _iterationCancellation?.Cancel();
                break;

            case ViewWindowCommand.Back:
                await SendDeviceKeyAsync("KEYCODE_BACK", "back").ConfigureAwait(false);
                break;

            case ViewWindowCommand.Home:
                await SendDeviceKeyAsync("KEYCODE_HOME", "home").ConfigureAwait(false);
                break;

            case ViewWindowCommand.Recents:
                await SendDeviceKeyAsync("KEYCODE_APP_SWITCH", "recents").ConfigureAwait(false);
                break;

            case ViewWindowCommand.OpenArtifacts:
                WriteEvent(new
                {
                    type = "view_artifacts_requested",
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    artifact_root = _artifacts.Root
                });
                break;

            default:
                break;
        }
    }

    private async Task ToggleRecordingAsync()
    {
        if (_recorder.IsRecording)
        {
            var recordPath = _recorder.ActiveRecordPath;
            await _recorder.StopAsync().ConfigureAwait(false);
            await PublishChromeAsync().ConfigureAwait(false);
            WriteEvent(new
            {
                type = "view_recording_stopped",
                session_id = _sessionId,
                occurred_at = _timeProvider.GetUtcNow(),
                record_path = recordPath,
                reason = "operator"
            });
            return;
        }

        var nextPath = BuildNextRecordingPath();
        await _recorder.StartAsync(nextPath).ConfigureAwait(false);
        await PublishChromeAsync().ConfigureAwait(false);
        WriteEvent(new
        {
            type = "view_recording_started",
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
        return Path.Combine(directory, $"{fileBaseName}-{sequence:000}{extension}");
    }

    private async Task HandleFileDropAsync(string filePath)
    {
        if (string.Equals(Path.GetExtension(filePath), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            var installResult = await _deviceHost.InstallPackageAsync(filePath).ConfigureAwait(false);
            WriteEvent(new
            {
                type = "view_package_installed",
                session_id = _sessionId,
                occurred_at = _timeProvider.GetUtcNow(),
                package_path = installResult.PackagePath
            });
            return;
        }

        var pushResult = await _deviceHost.PushFileAsync(filePath).ConfigureAwait(false);
        WriteEvent(new
        {
            type = "view_file_pushed",
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
            type = "view_file_pulled",
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
            type = "view_key_command_sent",
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            command,
            code = keyCode
        });
    }

    public async Task EmitDeviceShelfSnapshotIfNeededAsync()
    {
        if (!string.IsNullOrWhiteSpace(_options.JoinShareEndpoint))
        {
            _devices = [];
            await PublishChromeAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            var devices = await _deviceHost.GetDevicesAsync().ConfigureAwait(false);
            _devices = devices.Devices
                .Select((device, index) => new ViewChromeDevice(
                    index + 1,
                    device.Serial ?? $"device-{index + 1}",
                    device.Status,
                    device.Details,
                    string.Equals(device.Serial, ActiveDeviceSelector, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            UpdateActiveDeviceFlags();
            await PublishChromeAsync().ConfigureAwait(false);
            if (devices.Devices.Count <= 1)
            {
                return;
            }

            WriteEvent(new
            {
                type = "view_device_shelf",
                session_id = _sessionId,
                observed_at = _timeProvider.GetUtcNow(),
                active_device = ActiveDeviceSelector,
                devices = devices.Devices
            });
        }
        catch
        {
        }
    }

    public Task PublishChromeAsync()
    {
        var chromeUpdater = _chromeUpdater;
        return chromeUpdater is null ? Task.CompletedTask : chromeUpdater(BuildChromeState());
    }

    public async Task UpdateShareStateAsync(string? shareEndpoint, int observerCount)
    {
        _shareEndpoint = shareEndpoint;
        _observerCount = observerCount;
        await PublishChromeAsync().ConfigureAwait(false);
    }

    private ViewChromeState BuildChromeState() => new(
        ActiveDeviceSelector,
        _devices,
        _options.ReadOnly,
        !string.IsNullOrWhiteSpace(_options.JoinShareEndpoint),
        _recorder.IsRecording,
        string.IsNullOrWhiteSpace(_options.JoinShareEndpoint),
        string.IsNullOrWhiteSpace(_options.JoinShareEndpoint),
        true,
        _devices.Count > 1 && string.IsNullOrWhiteSpace(_options.JoinShareEndpoint),
        _shareEndpoint,
        _observerCount);

    private void UpdateActiveDeviceFlags()
    {
        if (_devices.Count == 0)
        {
            return;
        }

        _devices = _devices
            .Select(device => device with { IsActive = string.Equals(device.DeviceSelector, ActiveDeviceSelector, StringComparison.OrdinalIgnoreCase) })
            .ToArray();
    }

    private bool TryBlockReadOnly(string requestType)
    {
        if (!_options.ReadOnly)
        {
            return false;
        }

        WriteEvent(new
        {
            type = "view_input_blocked",
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
            type = "view_input_blocked",
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            request_type = requestType,
            reason
        });
        return true;
    }

    private void WriteEvent(object value) => _writeJsonLine(value);
}
