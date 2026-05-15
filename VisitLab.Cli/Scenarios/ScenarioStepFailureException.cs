namespace VisitLab.Cli;

public sealed class ScenarioStepFailureException(string message, string categoryOverride, object dataPayload, Exception innerException)
    : Exception(message, innerException), ICommandFailureDetails
{
    public string CategoryOverride { get; } = categoryOverride;

    public object? DataPayload { get; } = dataPayload;
}