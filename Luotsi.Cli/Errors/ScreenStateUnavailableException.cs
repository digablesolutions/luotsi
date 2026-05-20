using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Errors;

public sealed class ScreenStateUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException), ICommandFailureDetails
{
    public string CategoryOverride => "screen_state_unavailable";

    public object? DataPayload => null;
}
