using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli.Inspect;

internal sealed class InspectSession(IDeviceHost deviceHost, IConsoleIo console, TimeProvider timeProvider)
{
    private readonly IDeviceHost _deviceHost = deviceHost ?? throw new ArgumentNullException(nameof(deviceHost));
    private readonly InspectSessionProtocol _protocol = new(console ?? throw new ArgumentNullException(nameof(console)));
    private readonly InspectSessionCommandDispatcher _commandDispatcher = new(deviceHost);
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<int> RunAsync()
    {
        var sessionId = Guid.NewGuid().ToString("N");

        try
        {
            _protocol.WriteSessionStarted(sessionId, _timeProvider.GetUtcNow());

            ScreenState? currentState = null;
            try
            {
                currentState = await _deviceHost.GetScreenStateAsync().ConfigureAwait(false);
                _protocol.WriteStateSnapshot(sessionId, null, currentState);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _protocol.WriteSessionError(_timeProvider.GetUtcNow(), ex, sessionId);
            }

            while (true)
            {
                var line = _protocol.ReadLine();
                if (line is null)
                {
                    _protocol.WriteSessionEnded(sessionId, null, _timeProvider.GetUtcNow(), "stdin_closed");
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parseResult = _protocol.ParseCommand(line);
                if (!parseResult.IsSuccess)
                {
                    _protocol.WriteProtocolError(sessionId, _timeProvider.GetUtcNow(), parseResult.ErrorMessage!, parseResult.RawLine!);
                    continue;
                }

                var request = parseResult.Request!;
                var normalizedCommand = _commandDispatcher.Normalize(request.Command!);
                if (_commandDispatcher.IsExit(normalizedCommand))
                {
                    _protocol.WriteSessionEnded(sessionId, request.Id, _timeProvider.GetUtcNow(), "client_exit");
                    return 0;
                }

                var startedAt = _timeProvider.GetUtcNow();

                try
                {
                    var data = await _commandDispatcher.ExecuteAsync(request, normalizedCommand).ConfigureAwait(false);
                    _protocol.WriteCommandResult(sessionId, request.Id, normalizedCommand, true, startedAt, _timeProvider.GetUtcNow(), data);

                    if (_commandDispatcher.ShouldCaptureScreenState(normalizedCommand))
                    {
                        try
                        {
                            var nextState = await _deviceHost.GetScreenStateAsync().ConfigureAwait(false);
                            _protocol.WriteStateSnapshot(
                                sessionId,
                                request.Id,
                                nextState,
                                currentState is null ? null : InspectScreenStateDelta.Create(currentState, nextState));
                            currentState = nextState;
                        }
                        catch (Exception ex)
                        {
                            _protocol.WriteSessionError(_timeProvider.GetUtcNow(), ex, sessionId, request.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    var category = ex is UsageException
                        ? "usage_error"
                        : ex is ICommandFailureDetails failure
                            ? failure.CategoryOverride
                            : ErrorInfo.Classify(ex.Message);
                    _protocol.WriteCommandResult(sessionId, request.Id, normalizedCommand, false, startedAt, _timeProvider.GetUtcNow(), error: ErrorInfo.From(ex, category));
                }
            }
        }
        catch (Exception ex)
        {
            _protocol.WriteSessionError(_timeProvider.GetUtcNow(), ex, sessionId);
            return 1;
        }
    }
}
