namespace Luotsi.Cli.Scenarios;

internal static class ScenarioFailureDetails
{
    public static ScenarioRunFailureData? TryGetData(Exception exception) =>
        (exception as ICommandFailureDetails)?.DataPayload as ScenarioRunFailureData;

    public static IReadOnlyDictionary<string, double>? TryGetMetrics(Exception exception) =>
        TryGetData(exception)?.Metrics;
}