using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

internal sealed class ViewSessionReadOnlyBlockPolicy
{
    private readonly ViewOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _sessionId;
    private readonly Action<object> _writeEvent;

    public ViewSessionReadOnlyBlockPolicy(ViewSessionReadOnlyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _options = context.Options ?? throw new ArgumentNullException(nameof(context.Options));
        _timeProvider = context.Events.TimeProvider ?? throw new ArgumentNullException(nameof(context.Events.TimeProvider));
        _sessionId = string.IsNullOrWhiteSpace(context.Events.SessionId)
            ? throw new ArgumentException("Session id is required.", nameof(context.Events.SessionId))
            : context.Events.SessionId;
        _writeEvent = context.Events.WriteEvent ?? throw new ArgumentNullException(nameof(context.Events.WriteEvent));
    }

    public bool TryBlock(string requestType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestType);

        if (!_options.ReadOnly)
        {
            return false;
        }

        _writeEvent(new
        {
            type = SessionEventTypes.View.InputBlocked,
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            request_type = requestType,
            reason = "read_only"
        });
        return true;
    }
}