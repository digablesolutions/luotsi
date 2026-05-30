using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewSessionInteractionRouter
{
    private readonly TimeProvider _timeProvider;
    private readonly string _sessionId;
    private readonly Action<object> _writeJsonLine;
    private readonly ViewSessionInputCommandHandler _inputCommands;
    private readonly ViewSessionRecordingCoordinator _recording;
    private readonly ViewSessionStateCoordinator _state;

    public ViewSessionInteractionRouter(ViewSessionInteractionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var events = context.CreateEventContext();
        _timeProvider = events.TimeProvider ?? throw new ArgumentNullException(nameof(events.TimeProvider));
        _sessionId = string.IsNullOrWhiteSpace(events.SessionId)
            ? throw new ArgumentException("Session id is required.", nameof(events.SessionId))
            : events.SessionId;
        _writeJsonLine = events.WriteEvent ?? throw new ArgumentNullException(nameof(events.WriteEvent));

        _state = new ViewSessionStateCoordinator(context.CreateStateContext(events));
        _recording = new ViewSessionRecordingCoordinator(context.CreateRecordingContext(events), _state.PublishChromeAsync);

        var readOnlyBlockPolicy = new ViewSessionReadOnlyBlockPolicy(context.CreateReadOnlyContext(events));
        _inputCommands = new ViewSessionInputCommandHandler(
            new ViewSessionDeviceInputHandler(context.CreateDeviceInputContext(events), readOnlyBlockPolicy.TryBlock),
            new ViewSessionFileTransferHandler(context.CreateFileTransferContext(events), readOnlyBlockPolicy.TryBlock),
            new ViewSessionWindowCommandHandler(
                context.CreateWindowCommandContext(events),
                _recording,
                new ViewSessionInteractionCallbacks(
                    () => ActiveDeviceSelector,
                    RequestReconnect),
                readOnlyBlockPolicy.TryBlock));
    }

    public string ActiveDeviceSelector => _state.ActiveDeviceSelector;

    public void BeginIteration(string deviceSelector, CancellationTokenSource iterationCancellation)
        => _state.BeginIteration(deviceSelector, iterationCancellation);

    public void AttachConnection(ViewConnectionInfo connectionInfo) => _recording.AttachConnection(connectionInfo);

    public void AttachChromeUpdater(Func<ViewChromeState, Task> chromeUpdater) => _state.AttachChromeUpdater(chromeUpdater);

    public void AttachStreamPauseUpdater(Action<bool> streamPauseUpdater) => _inputCommands.AttachStreamPauseUpdater(streamPauseUpdater);

    public Task WaitForReconnectAsync() => _state.WaitForReconnectAsync();

    public void ResetReconnectSignal() => _state.ResetReconnectSignal();

    public bool RequestReconnect(string source, string? reason = null) => _state.RequestReconnect(source, reason);

    public async Task StartInitialRecordingIfNeededAsync()
        => await _recording.StartInitialRecordingIfNeededAsync().ConfigureAwait(false);

    public async Task StopRecordingForReconnectAsync()
        => await _recording.StopRecordingForReconnectAsync().ConfigureAwait(false);

    public async Task ResumeRecordingAfterReconnectIfNeededAsync()
        => await _recording.ResumeRecordingAfterReconnectIfNeededAsync().ConfigureAwait(false);

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
                if (await _inputCommands.TryHandleAsync(request).ConfigureAwait(false))
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

        await _state.SwitchDeviceAsync(request.DeviceSelector).ConfigureAwait(false);
    }

    public async Task EmitDeviceShelfSnapshotIfNeededAsync()
        => await _state.EmitDeviceShelfSnapshotIfNeededAsync().ConfigureAwait(false);

    public Task PublishChromeAsync() => _state.PublishChromeAsync();

    public async Task UpdateShareStateAsync(string? shareEndpoint, int observerCount)
        => await _state.UpdateShareStateAsync(shareEndpoint, observerCount).ConfigureAwait(false);

    private void WriteEvent(object value) => _writeJsonLine(value);
}
