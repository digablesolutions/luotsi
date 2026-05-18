using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli;

internal sealed class ScenarioCommandDispatcher(
    ScenarioRunPlanner runPlanner,
    ScenarioExecutorFactory scenarioExecutorFactory,
    ScenarioBatchExecutorFactory scenarioBatchExecutorFactory,
    ScenarioRunEventCoordinatorFactory scenarioRunEventCoordinatorFactory)
{
    private readonly ScenarioRunPlanner _runPlanner = runPlanner ?? throw new ArgumentNullException(nameof(runPlanner));
    private readonly ScenarioExecutorFactory _scenarioExecutorFactory = scenarioExecutorFactory ?? throw new ArgumentNullException(nameof(scenarioExecutorFactory));
    private readonly ScenarioBatchExecutorFactory _scenarioBatchExecutorFactory = scenarioBatchExecutorFactory ?? throw new ArgumentNullException(nameof(scenarioBatchExecutorFactory));
    private readonly ScenarioRunEventCoordinatorFactory _scenarioRunEventCoordinatorFactory = scenarioRunEventCoordinatorFactory ?? throw new ArgumentNullException(nameof(scenarioRunEventCoordinatorFactory));

    public async Task<ScenarioListResult> ListAsync(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var query = ScenarioQueryFactory.CreateListQuery(options);
        var selection = await _runPlanner.CreateListSelectionAsync(query).ConfigureAwait(false);
        return new ScenarioListResult(selection.Query.Path, selection.TotalCount, selection.MatchedCount, selection.MatchedScenarios);
    }

    public async Task<object> RunAsync(CliOptions options, IDeviceHost runner)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);

        if (!ScenarioQueryFactory.UsesCatalogExecution(options))
        {
            if (options.HasFlag("dry-run"))
            {
                throw new UsageException("run --dry-run requires --path. Use run --file <scenario.json> without --dry-run for single-scenario execution.");
            }

            var file = options.Require("file");
            await using var singleRunEvents = _scenarioRunEventCoordinatorFactory.Create(options.Get("events-jsonl"));
            return await singleRunEvents.RunFileAsync(
                file,
                sink => _scenarioExecutorFactory.Create(runner, sink).RunAsync(file)).ConfigureAwait(false);
        }

        var query = ScenarioQueryFactory.CreateCatalogRunQuery(options);
        var plan = await _runPlanner.CreateAsync(query).ConfigureAwait(false);

        if (query.DryRun)
        {
            return new ScenarioRunPlanResult(
                query.Path,
                true,
                plan.TotalCount,
                plan.MatchedCount,
                plan.SelectedCount,
                plan.ShardedOutCount,
                query.ShardCount,
                query.ShardIndex,
                plan.SelectedScenarios);
        }

        await using var batchRunEvents = _scenarioRunEventCoordinatorFactory.Create(options.Get("events-jsonl"));
        return await batchRunEvents.RunBatchAsync(
            plan,
            sink => _scenarioBatchExecutorFactory.Create(runner, sink).RunAsync(plan)).ConfigureAwait(false);
    }
}
