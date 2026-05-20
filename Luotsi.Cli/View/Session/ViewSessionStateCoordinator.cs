using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewSessionStateCoordinator
{
    private readonly IDeviceHost _deviceHost;
    private readonly ViewOptions _options;
    private readonly SessionControlledViewRecorder _recorder;
    private readonly TimeProvider _timeProvider;
    private readonly string _sessionId;
    private readonly Action<object> _writeEvent;

    private CancellationTokenSource? _iterationCancellation;
    private TaskCompletionSource _reconnectRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Func<ViewChromeState, Task>? _chromeUpdater;
    private IReadOnlyList<ViewChromeDevice> _devices = [];
    private string? _shareEndpoint;
    private int _observerCount;

    public ViewSessionStateCoordinator(ViewSessionInteractionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _deviceHost = context.DeviceHost ?? throw new ArgumentNullException(nameof(context.DeviceHost));
        _options = context.Options ?? throw new ArgumentNullException(nameof(context.Options));
        _recorder = context.Recorder ?? throw new ArgumentNullException(nameof(context.Recorder));
        _timeProvider = context.TimeProvider ?? throw new ArgumentNullException(nameof(context.TimeProvider));
        _sessionId = string.IsNullOrWhiteSpace(context.SessionId)
            ? throw new ArgumentException("Session id is required.", nameof(context.SessionId))
            : context.SessionId;
        _writeEvent = context.WriteEvent ?? throw new ArgumentNullException(nameof(context.WriteEvent));
        _shareEndpoint = context.Options.JoinShareEndpoint;
        ActiveDeviceSelector = context.Options.DeviceSelector;
    }

    public string ActiveDeviceSelector { get; private set; }

    public void BeginIteration(string deviceSelector, CancellationTokenSource iterationCancellation)
    {
        ArgumentNullException.ThrowIfNull(iterationCancellation);

        ActiveDeviceSelector = string.IsNullOrWhiteSpace(deviceSelector) ? _options.DeviceSelector : deviceSelector;
        _iterationCancellation = iterationCancellation;
        UpdateActiveDeviceFlags();
    }

    public void AttachChromeUpdater(Func<ViewChromeState, Task> chromeUpdater) =>
        _chromeUpdater = chromeUpdater ?? throw new ArgumentNullException(nameof(chromeUpdater));

    public Task WaitForReconnectAsync() => _reconnectRequested.Task;

    public void ResetReconnectSignal() =>
        _reconnectRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool RequestReconnect(string source, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

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
        SignalReconnect();
        return true;
    }

    public async Task<bool> SwitchDeviceAsync(string deviceSelector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSelector);

        if (string.Equals(deviceSelector, ActiveDeviceSelector, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        WriteEvent(new
        {
            type = SessionEventTypes.View.DeviceSwitchRequested,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            from_device = ActiveDeviceSelector,
            to_device = deviceSelector
        });
        ActiveDeviceSelector = deviceSelector;
        UpdateActiveDeviceFlags();
        await PublishChromeAsync().ConfigureAwait(false);
        await SignalReconnectAsync().ConfigureAwait(false);
        return true;
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
        catch (Exception ex) when (!IsFatalException(ex))
        {
            WriteEvent(new
            {
                type = SessionEventTypes.View.Error,
                session_id = _sessionId,
                occurred_at = _timeProvider.GetUtcNow(),
                source = "device_shelf_probe",
                error = ErrorInfo.From(ex, ErrorInfo.Classify(ex.Message))
            });
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

    private void SignalReconnect()
    {
        _reconnectRequested.TrySetResult();
        _iterationCancellation?.Cancel();
    }

    private async Task SignalReconnectAsync()
    {
        _reconnectRequested.TrySetResult();
        var iterationCancellation = _iterationCancellation;
        if (iterationCancellation is not null)
        {
            await iterationCancellation.CancelAsync().ConfigureAwait(false);
        }
    }

    private void WriteEvent(object value) => _writeEvent(value);

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}