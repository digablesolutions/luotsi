namespace VisitLab.Cli;

public sealed class SemanticWaitTimeoutException(string target, int timeoutSec) : TimeoutException($"Timed out after {timeoutSec}s waiting for {target}."), ICommandFailureDetails
{
    public string CategoryOverride => "oracle_timeout";

    public object? DataPayload => null;
}