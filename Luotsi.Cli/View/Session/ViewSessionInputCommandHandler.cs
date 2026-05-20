using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewSessionInputCommandHandler
{
    private readonly ViewSessionDeviceInputHandler _deviceInputs;
    private readonly ViewSessionFileTransferHandler _fileTransfers;
    private readonly ViewSessionWindowCommandHandler _windowCommands;

    public ViewSessionInputCommandHandler(
        ViewSessionInteractionContext context,
        ViewSessionRecordingCoordinator recording,
        ViewSessionInteractionCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(callbacks);

        var readOnlyBlockPolicy = new ViewSessionReadOnlyBlockPolicy(context);
        _deviceInputs = new ViewSessionDeviceInputHandler(context, readOnlyBlockPolicy.TryBlock);
        _fileTransfers = new ViewSessionFileTransferHandler(context, readOnlyBlockPolicy.TryBlock);
        _windowCommands = new ViewSessionWindowCommandHandler(context, recording, callbacks, readOnlyBlockPolicy.TryBlock);
    }

    public void AttachStreamPauseUpdater(Action<bool> streamPauseUpdater) => _windowCommands.AttachStreamPauseUpdater(streamPauseUpdater);

    public async Task<bool> TryHandleAsync(ViewInteractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request is ViewWindowCommandRequest windowCommandRequest)
        {
            await _windowCommands.HandleAsync(windowCommandRequest.Command).ConfigureAwait(false);
            return true;
        }

        if (await _deviceInputs.TryHandleAsync(request).ConfigureAwait(false))
        {
            return true;
        }

        return await _fileTransfers.TryHandleAsync(request).ConfigureAwait(false);
    }
}