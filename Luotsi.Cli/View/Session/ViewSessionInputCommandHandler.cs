using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewSessionInputCommandHandler
{
    private readonly ViewSessionDeviceInputHandler _deviceInputs;
    private readonly ViewSessionFileTransferHandler _fileTransfers;
    private readonly ViewSessionWindowCommandHandler _windowCommands;

    public ViewSessionInputCommandHandler(
        ViewSessionDeviceInputHandler deviceInputs,
        ViewSessionFileTransferHandler fileTransfers,
        ViewSessionWindowCommandHandler windowCommands)
    {
        _deviceInputs = deviceInputs ?? throw new ArgumentNullException(nameof(deviceInputs));
        _fileTransfers = fileTransfers ?? throw new ArgumentNullException(nameof(fileTransfers));
        _windowCommands = windowCommands ?? throw new ArgumentNullException(nameof(windowCommands));
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