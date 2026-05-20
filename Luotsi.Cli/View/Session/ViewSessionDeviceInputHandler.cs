using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewSessionDeviceInputHandler
{
    private readonly IDeviceHost _deviceHost;
    private readonly TimeProvider _timeProvider;
    private readonly string _sessionId;
    private readonly Action<object> _writeEvent;
    private readonly Func<string, bool> _tryBlockReadOnly;

    public ViewSessionDeviceInputHandler(
        ViewSessionDeviceInputContext context,
        Func<string, bool> tryBlockReadOnly)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tryBlockReadOnly);

        _deviceHost = context.DeviceHost ?? throw new ArgumentNullException(nameof(context.DeviceHost));
        _timeProvider = context.Events.TimeProvider ?? throw new ArgumentNullException(nameof(context.Events.TimeProvider));
        _sessionId = string.IsNullOrWhiteSpace(context.Events.SessionId)
            ? throw new ArgumentException("Session id is required.", nameof(context.Events.SessionId))
            : context.Events.SessionId;
        _writeEvent = context.Events.WriteEvent ?? throw new ArgumentNullException(nameof(context.Events.WriteEvent));
        _tryBlockReadOnly = tryBlockReadOnly;
    }

    public async Task<bool> TryHandleAsync(ViewInteractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request)
        {
            case ViewTapRequest tapRequest:
                if (_tryBlockReadOnly("tap"))
                {
                    return true;
                }

                await _deviceHost.TapPointAsync("view-window", null, null, tapRequest.XRatio, tapRequest.YRatio, 0).ConfigureAwait(false);
                return true;

            case ViewTextInputRequest textInputRequest:
                if (_tryBlockReadOnly("text_input"))
                {
                    return true;
                }

                await _deviceHost.TypeTextAsync(textInputRequest.Text).ConfigureAwait(false);
                return true;

            case ViewKeyInputRequest keyInputRequest:
                if (_tryBlockReadOnly("key_input"))
                {
                    return true;
                }

                await _deviceHost.KeyEventAsync(keyInputRequest.Code).ConfigureAwait(false);
                return true;

            case ViewScrollRequest scrollRequest:
                if (_tryBlockReadOnly("scroll"))
                {
                    return true;
                }

                await _deviceHost.ScrollAsync(scrollRequest.HorizontalTicks, scrollRequest.VerticalTicks).ConfigureAwait(false);
                return true;

            case ViewClipboardPasteRequest clipboardPasteRequest:
                if (_tryBlockReadOnly("clipboard"))
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

            default:
                return false;
        }
    }

    private void WriteEvent(object value) => _writeEvent(value);
}