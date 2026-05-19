namespace Luotsi.Cli.Scenarios;

public sealed class ScenarioStepFailureException(string message, string categoryOverride, ScenarioRunFailureData dataPayload, Exception innerException)
    : Exception(message, innerException), ICommandFailureDetails
{
    public string CategoryOverride { get; } = categoryOverride;

    public object? DataPayload { get; private set; } = dataPayload;

    internal void UpdateDataPayload(ScenarioRunFailureData updatedDataPayload)
    {
        ArgumentNullException.ThrowIfNull(updatedDataPayload);
        DataPayload = updatedDataPayload;
    }
}