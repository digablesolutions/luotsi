using System.Diagnostics;
using System.Runtime.InteropServices;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewSessionInteractionRouter(
    ViewSessionInteractionContext context)
{
    private readonly ViewSessionInteractionContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly IDeviceHost _deviceHost = context.DeviceHost ?? throw new ArgumentNullException(nameof(context.DeviceHost));
    private readonly ArtifactSession _artifacts = context.Artifacts ?? throw new ArgumentNullException(nameof(context.Artifacts));
    private readonly ViewOptions _options = context.Options ?? throw new ArgumentNullException(nameof(context.Options));
    private readonly SessionControlledViewRecorder _recorder = context.Recorder ?? throw new ArgumentNullException(nameof(context.Recorder));
    private readonly TimeProvider _timeProvider = context.TimeProvider ?? throw new ArgumentNullException(nameof(context.TimeProvider));
    private readonly string _sessionId = string.IsNullOrWhiteSpace(context.SessionId) ? throw new ArgumentException("Session id is required.", nameof(context.SessionId)) : context.SessionId;
    private readonly Action<object> _writeJsonLine = context.WriteEvent ?? throw new ArgumentNullException(nameof(context.WriteEvent));
    private readonly IArtifactFolderOpener _artifactFolderOpener = context.ArtifactFolderOpener ?? throw new ArgumentNullException(nameof(context.ArtifactFolderOpener));

    private CancellationTokenSource? _iterationCancellation;
    private TaskCompletionSource _reconnectRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Func<ViewChromeState, Task>? _chromeUpdater;
    private IReadOnlyList<ViewChromeDevice> _devices = [];
    private string? _shareEndpoint = context.Options.JoinShareEndpoint;
    private int _observerCount;

    public string ActiveDeviceSelector { get; private set; } = context.Options.DeviceSelector;

    public void BeginIteration(string deviceSelector, CancellationTokenSource iterationCancellation)
    {
        ActiveDeviceSelector = string.IsNullOrWhiteSpace(deviceSelector) ? _options.DeviceSelector : deviceSelector;
        _iterationCancellation = iterationCancellation;
        UpdateActiveDeviceFlags();
    }

    public void AttachConnection(ViewConnectionInfo connectionInfo) => InputCommands.AttachConnection(connectionInfo);

    public void AttachChromeUpdater(Func<ViewChromeState, Task> chromeUpdater) => _chromeUpdater = chromeUpdater;

    public void AttachStreamPauseUpdater(Action<bool> streamPauseUpdater) => InputCommands.AttachStreamPauseUpdater(streamPauseUpdater);

    public Task WaitForReconnectAsync() => _reconnectRequested.Task;

    public void ResetReconnectSignal() => _reconnectRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool RequestReconnect(string source, string? reason = null)
    {
        if (_reconnectRequested.Task.IsCompleted)
        {
            return false;
        }

        WriteEvent(new
        {
            type = SessionEventTypes.View.ReconnectRequested,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            device = ActiveDeviceSelector,
            source,
            reason
        });
        _reconnectRequested.TrySetResult();
        _iterationCancellation?.Cancel();
        return true;
    }

    public async Task StartInitialRecordingIfNeededAsync()
        => await InputCommands.StartInitialRecordingIfNeededAsync().ConfigureAwait(false);

    public async Task StopRecordingForReconnectAsync()
        => await InputCommands.StopRecordingForReconnectAsync().ConfigureAwait(false);

    public async Task HandleAsync(ViewInteractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request)
        {
            case ViewSwitchDeviceRequest switchDeviceRequest:
                await HandleDeviceSwitchAsync(switchDeviceRequest).ConfigureAwait(false);
                return;

            case ViewInteractionFailedRequest failedRequest:
                WriteEvent(new
                {
                    type = SessionEventTypes.View.InteractionFailed,
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    request_type = failedRequest.FailedRequestType,
                    exception_type = failedRequest.ExceptionType,
                    message = failedRequest.Message
                });
                return;

            default:
                if (await InputCommands.TryHandleAsync(request).ConfigureAwait(false))
                {
                    return;
                }

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
            type = SessionEventTypes.View.DeviceSwitchRequested,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            from_device = ActiveDeviceSelector,
            to_device = request.DeviceSelector
        });
        ActiveDeviceSelector = request.DeviceSelector;
        UpdateActiveDeviceFlags();
        await PublishChromeAsync().ConfigureAwait(false);
        _reconnectRequested.TrySetResult();
        var iterationCancellation = _iterationCancellation;
        if (iterationCancellation is not null)
        {
            await iterationCancellation.CancelAsync().ConfigureAwait(false);
        }
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
                type = SessionEventTypes.View.DeviceShelf,
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

    private ViewSessionInputCommandHandler InputCommands =>
        field ??= new ViewSessionInputCommandHandler(
            _context,
            new ViewSessionInteractionCallbacks(
                PublishChromeAsync,
                () => ActiveDeviceSelector,
                RequestReconnect));

    private void WriteEvent(object value) => _writeJsonLine(value);
}

public interface IArtifactFolderOpener
{
    Task OpenAsync(string path);
}

internal sealed class SystemArtifactFolderOpener : IArtifactFolderOpener
{
    public Task OpenAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var startInfo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProcessStartInfo("explorer.exe", fullPath)
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? new ProcessStartInfo("open", fullPath)
                : new ProcessStartInfo("xdg-open", fullPath);

        startInfo.UseShellExecute = false;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to open artifact folder '{fullPath}'.");
        return Task.CompletedTask;
    }
}
