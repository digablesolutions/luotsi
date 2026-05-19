using Luotsi.Cli.Errors;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioBatchExecutor(
    ScenarioExecutor scenarios,
    IScenarioMetricsCollector metricsCollector)
{
    private readonly ScenarioExecutor _scenarios = scenarios ?? throw new ArgumentNullException(nameof(scenarios));
    private readonly IScenarioMetricsCollector _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));

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
                results.Add(ScenarioBatchItemResult.FromSuccess(await _scenarios.RunAsync(scenario.File).ConfigureAwait(false), scenario));
                passedCount++;
            }
            catch (Exception ex) when (ex is not UsageException)
            {
                failedCount++;
                results.Add(CreateFailureResult(scenario, ex));
            }
        }

        var result = new ScenarioRunBatchResult(
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
            results,
            plan.Query.ShardStrategy);
        return result with { Metrics = _metricsCollector.CollectBatch(new ScenarioBatchMetricContext(result)) };
    }

    private static ScenarioBatchItemResult CreateFailureResult(ScenarioCatalogEntry scenario, Exception exception)
    {
        return ScenarioBatchItemResult.FromFailure(
            scenario.Name,
            scenario.File,
            ScenarioFailureDetails.TryGetData(exception),
            ScenarioErrorInfo.From(exception),
            scenario.Id);
    }
}
