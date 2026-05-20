using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewSessionInteractionRouter(
    ViewSessionInteractionContext context)
{
    private readonly ViewSessionInteractionContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly TimeProvider _timeProvider = context.TimeProvider ?? throw new ArgumentNullException(nameof(context.TimeProvider));
    private readonly string _sessionId = string.IsNullOrWhiteSpace(context.SessionId) ? throw new ArgumentException("Session id is required.", nameof(context.SessionId)) : context.SessionId;
    private readonly Action<object> _writeJsonLine = context.WriteEvent ?? throw new ArgumentNullException(nameof(context.WriteEvent));

    public string ActiveDeviceSelector => State.ActiveDeviceSelector;

    public void BeginIteration(string deviceSelector, CancellationTokenSource iterationCancellation)
        => State.BeginIteration(deviceSelector, iterationCancellation);

    public void AttachConnection(ViewConnectionInfo connectionInfo) => Recording.AttachConnection(connectionInfo);

    public void AttachChromeUpdater(Func<ViewChromeState, Task> chromeUpdater) => State.AttachChromeUpdater(chromeUpdater);

    public void AttachStreamPauseUpdater(Action<bool> streamPauseUpdater) => InputCommands.AttachStreamPauseUpdater(streamPauseUpdater);

    public Task WaitForReconnectAsync() => State.WaitForReconnectAsync();

    public void ResetReconnectSignal() => State.ResetReconnectSignal();

    public bool RequestReconnect(string source, string? reason = null) => State.RequestReconnect(source, reason);

    public async Task StartInitialRecordingIfNeededAsync()
        => await Recording.StartInitialRecordingIfNeededAsync().ConfigureAwait(false);

    public async Task StopRecordingForReconnectAsync()
        => await Recording.StopRecordingForReconnectAsync().ConfigureAwait(false);

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

        await State.SwitchDeviceAsync(request.DeviceSelector).ConfigureAwait(false);
    }

    public async Task EmitDeviceShelfSnapshotIfNeededAsync()
        => await State.EmitDeviceShelfSnapshotIfNeededAsync().ConfigureAwait(false);

    public Task PublishChromeAsync() => State.PublishChromeAsync();

    public async Task UpdateShareStateAsync(string? shareEndpoint, int observerCount)
        => await State.UpdateShareStateAsync(shareEndpoint, observerCount).ConfigureAwait(false);

    private ViewSessionInputCommandHandler InputCommands =>
        field ??= new ViewSessionInputCommandHandler(
            _context,
            Recording,
            new ViewSessionInteractionCallbacks(
                () => ActiveDeviceSelector,
                RequestReconnect));

    private ViewSessionRecordingCoordinator Recording =>
        field ??= new ViewSessionRecordingCoordinator(_context, PublishChromeAsync);

    private ViewSessionStateCoordinator State =>
        field ??= new ViewSessionStateCoordinator(_context);

    private void WriteEvent(object value) => _writeJsonLine(value);
}
