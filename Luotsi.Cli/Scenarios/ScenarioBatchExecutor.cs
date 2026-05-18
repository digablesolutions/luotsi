using Luotsi.Cli.Errors;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioBatchExecutor(ScenarioExecutor scenarios)
{
    private readonly ScenarioExecutor _scenarios = scenarios ?? throw new ArgumentNullException(nameof(scenarios));

    public async Task<ScenarioRunBatchResult> RunAsync(ScenarioRunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var results = new List<ScenarioBatchItemResult>(plan.SelectedCount);
        var passedCount = 0;
        var failedCount = 0;

        foreach (var scenario in plan.SelectedScenarios)
        {
            try
            {
                results.Add(ScenarioBatchItemResult.FromSuccess(await _scenarios.RunAsync(scenario.File).ConfigureAwait(false)));
                passedCount++;
            }
            catch (Exception ex) when (ex is not UsageException)
            {
                failedCount++;
                results.Add(CreateFailureResult(scenario, ex));
            }
        }

        return new ScenarioRunBatchResult(
            plan.Query.Path,
            failedCount == 0 ? "passed" : "failed",
            plan.TotalCount,
            plan.MatchedCount,
            plan.SelectedCount,
            passedCount,
            failedCount,
            plan.ShardedOutCount,
            plan.Query.ShardCount,
            plan.Query.ShardIndex,
            results);
    }

    private static ScenarioBatchItemResult CreateFailureResult(ScenarioCatalogEntry scenario, Exception exception)
    {
        var failure = exception as ICommandFailureDetails;
        return ScenarioBatchItemResult.FromFailure(
            scenario.Name,
            scenario.File,
            failure?.DataPayload as ScenarioRunFailureData,
            ScenarioErrorInfo.From(exception));
    }
}