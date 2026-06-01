using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli.Inspect;

internal sealed class InspectSession
{
    private readonly IDeviceHost _deviceHost;
    private readonly ArtifactSession _artifacts;
    private readonly InspectSessionProtocol _protocol;
    private readonly InspectSessionCommandDispatcher _commandDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly string? _target;
    private SessionReplayArtifacts? _replayArtifacts;

    public InspectSession(IDeviceHost deviceHost, ArtifactSession artifacts, IConsoleIo console, TimeProvider timeProvider, string? target = null)
    {
        _deviceHost = deviceHost ?? throw new ArgumentNullException(nameof(deviceHost));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _commandDispatcher = new InspectSessionCommandDispatcher(deviceHost);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _target = target;
        _protocol = new InspectSessionProtocol(console ?? throw new ArgumentNullException(nameof(console)), json => _replayArtifacts?.RecordSerializedEvent(json));
    }

    public async Task<int> RunAsync()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var sessionStartedAt = _timeProvider.GetUtcNow();
        _replayArtifacts = new SessionReplayArtifacts(_artifacts, "inspect", sessionId, sessionStartedAt);
        _replayArtifacts.SetTarget(_target);
        var exitCode = 1;
        var endReason = "error";
        DateTimeOffset? endedAt = null;

        try
        {
            _protocol.WriteSessionStarted(sessionId, sessionStartedAt);

            ScreenState? currentState = null;
            try
            {
                currentState = await _deviceHost.GetScreenStateAsync().ConfigureAwait(false);
                _protocol.WriteStateSnapshot(sessionId, null, currentState);
            }
            catch (Exception ex) when (!IsFatalException(ex) && ex is not OperationCanceledException)
            {
                _protocol.WriteSessionError(_timeProvider.GetUtcNow(), ex, sessionId);
            }

            while (true)
            {
                var line = _protocol.ReadLine();
                if (line is null)
                {
                    endedAt = _timeProvider.GetUtcNow();
                    exitCode = 0;
                    endReason = "stdin_closed";
                    _protocol.WriteSessionEnded(sessionId, null, endedAt.Value, endReason);
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parseResult = InspectSessionProtocol.ParseCommand(line);
                if (!parseResult.IsSuccess)
                {
                    _protocol.WriteProtocolError(sessionId, _timeProvider.GetUtcNow(), parseResult.ErrorMessage!, parseResult.RawLine!);
                    continue;
                }

                var request = parseResult.Request!;
                var normalizedCommand = InspectSessionCommandDispatcher.Normalize(request.Command!);
                if (InspectSessionCommandDispatcher.IsExit(normalizedCommand))
                {
                    endedAt = _timeProvider.GetUtcNow();
                    exitCode = 0;
                    endReason = "client_exit";
                    _protocol.WriteSessionEnded(sessionId, request.Id, endedAt.Value, endReason);
                    return 0;
                }

                var startedAt = _timeProvider.GetUtcNow();

                try
                {
                    var data = await _commandDispatcher.ExecuteAsync(request, normalizedCommand).ConfigureAwait(false);
                    _protocol.WriteCommandResult(
                        sessionId,
                        request.Id,
                        normalizedCommand,
                        true,
                        startedAt,
                        _timeProvider.GetUtcNow(),
                        data,
                        selector: InspectSessionCommandDispatcher.TryCreateResultSelector(request, normalizedCommand));

                    if (InspectSessionCommandDispatcher.ShouldCaptureScreenState(normalizedCommand))
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
                        catch (OperationCanceledException ex)
                        {
                            _protocol.WriteSessionError(_timeProvider.GetUtcNow(), ex, sessionId, request.Id);
                        }
                        catch (TimeoutException ex)
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
                    _protocol.WriteCommandResult(
                        sessionId,
                        request.Id,
                        normalizedCommand,
                        false,
                        startedAt,
                        _timeProvider.GetUtcNow(),
                        error: ErrorInfo.From(ex, category),
                        selector: InspectSessionCommandDispatcher.TryCreateResultSelector(request, normalizedCommand));
                }
            }
        }
        catch (Exception ex)
        {
            endedAt = _timeProvider.GetUtcNow();
            _protocol.WriteSessionError(endedAt.Value, ex, sessionId);
            return 1;
        }
        finally
        {
            if (_replayArtifacts is not null)
            {
                await _replayArtifacts.PersistAsync(endedAt ?? _timeProvider.GetUtcNow(), endReason, exitCode).ConfigureAwait(false);
                _replayArtifacts = null;
            }
        }
    }

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}
