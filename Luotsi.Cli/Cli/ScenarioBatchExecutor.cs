using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli;

internal sealed class ScenarioBatchExecutor(ScenarioExecutor scenarios)
{
    private readonly ScenarioExecutor _scenarios = scenarios ?? throw new ArgumentNullException(nameof(scenarios));

    public async Task<ScenarioRunBatchResult> RunAsync(ScenarioRunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var results = new List<object>(plan.SelectedCount);
        var passedCount = 0;
        var failedCount = 0;

        foreach (var scenario in plan.SelectedScenarios)
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
