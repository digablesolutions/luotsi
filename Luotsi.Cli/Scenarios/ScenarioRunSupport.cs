using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal enum ScenarioFailureArtifactCapturePolicy
{
    Failure,
    Never
}

public static class ScenarioIdentity
{
    public static string Create(string file, string scenarioName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioName);
        return $"{file}::{scenarioName}";
    }
}

internal static class ScenarioErrorInfo
{
    public static ErrorInfo From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return ErrorInfo.From(exception, GetCategory(exception));
    }

    public static string GetCategory(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is ICommandFailureDetails failure
            ? failure.CategoryOverride
            : ErrorInfo.Classify(exception.Message);
    }
}