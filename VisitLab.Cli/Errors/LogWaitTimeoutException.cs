using VisitLab.Cli.Scenarios;

namespace VisitLab.Cli.Errors;

public sealed class LogWaitTimeoutException(string containsText, int timeoutSec) : TimeoutException($"Timed out after {timeoutSec}s waiting for log text '{containsText}'."), ICommandFailureDetails
{
    public string CategoryOverride => "log_wait_timeout";

    public object? DataPayload => null;
}