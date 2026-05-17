using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli;

internal sealed class ScenarioBatchExecutor(ScenarioExecutor scenarios)
{
    private readonly ScenarioExecutor _scenarios = scenarios ?? throw new ArgumentNullException(nameof(scenarios));

    public async Task<ScenarioRunBatchResult> RunAsync(ScenarioBatchExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var results = new List<object>(request.SelectedScenarios.Count);
        var passedCount = 0;
        var failedCount = 0;

        foreach (var scenario in request.SelectedScenarios)
        {
            try
            {
                results.Add(await _scenarios.RunAsync(scenario.File).ConfigureAwait(false));
                passedCount++;
            }
            catch (Exception ex) when (ex is not UsageException)
            {
                failedCount++;
                results.Add(CreateFailureResult(scenario, ex));
            }
        }

        return new ScenarioRunBatchResult(
            request.Query.Path,
            failedCount == 0 ? "passed" : "failed",
            request.TotalCount,
            request.MatchedCount,
            request.SelectedScenarios.Count,
            passedCount,
            failedCount,
            request.MatchedCount - request.SelectedScenarios.Count,
            request.Query.ShardCount,
            request.Query.ShardIndex,
            results);
    }

    private static object CreateFailureResult(ScenarioCatalogEntry scenario, Exception exception)
    {
        var failure = exception as ICommandFailureDetails;
        return new
        {
            scenario = scenario.Name,
            file = scenario.File,
            status = "failed",
            data = failure?.DataPayload,
            error = ErrorInfo.From(exception, failure?.CategoryOverride ?? ErrorInfo.Classify(exception.Message))
        };
    }
}

internal sealed record ScenarioBatchExecutionRequest(
    ScenarioQuery Query,
    int TotalCount,
    int MatchedCount,
    IReadOnlyList<ScenarioCatalogEntry> SelectedScenarios);