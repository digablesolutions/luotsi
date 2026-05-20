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

internal static class ScenarioTimingSupport
{
    public static ScenarioStepTiming CreateStepTiming(ScenarioStep step, double durationMs, int harnessDelayMs)
    {
        ArgumentNullException.ThrowIfNull(step);

        return new ScenarioStepTiming(
            durationMs,
            harnessDelayMs,
            GetConfiguredDelayMs(step),
            Math.Max(0, durationMs - harnessDelayMs));
    }

    public static int? GetConfiguredDelayMs(ScenarioStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return step.Action switch
        {
            "sleep" => Math.Max(0, step.Milliseconds ?? 1000),
            "tapPoint" => Math.Max(0, step.PostTapDelayMs ?? 300),
            "typePin" when !string.IsNullOrWhiteSpace(step.Text) => Math.Max(0, step.IntervalMs ?? 120) * step.Text.Count(char.IsDigit),
            _ => null
        };
    }
}