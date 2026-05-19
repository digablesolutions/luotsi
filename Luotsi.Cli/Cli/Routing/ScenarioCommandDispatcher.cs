using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class ScenarioCommandDispatcher(
    ScenarioRunPlanner runPlanner,
    ScenarioRunOrchestrator scenarioRunOrchestrator)
{
    private readonly ScenarioRunPlanner _runPlanner = runPlanner ?? throw new ArgumentNullException(nameof(runPlanner));
    private readonly ScenarioRunOrchestrator _scenarioRunOrchestrator = scenarioRunOrchestrator ?? throw new ArgumentNullException(nameof(scenarioRunOrchestrator));

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

        var configuration = ScenarioRunConfiguration.Create(options);

        if (!ScenarioQueryFactory.UsesCatalogExecution(options))
        {
            if (options.HasFlag("dry-run"))
            {
                throw new UsageException("run --dry-run requires --path. Use run --file <scenario.json> without --dry-run for single-scenario execution.");
            }

            var file = options.Require("file");
            if (configuration.ValidateOnly)
            {
                return await _scenarioRunOrchestrator.ValidateFileAsync(file, configuration).ConfigureAwait(false);
            }

            return await _scenarioRunOrchestrator.RunFileAsync(file, runner, configuration).ConfigureAwait(false);
        }

        var query = ScenarioQueryFactory.CreateCatalogRunQuery(options);
        if (configuration.ValidateOnly && query.DryRun)
        {
            throw new UsageException("Use either --validate-only or --dry-run, not both.");
        }

        if (query.DryRun)
        {
            var plan = await _runPlanner.CreateAsync(query).ConfigureAwait(false);
            return new ScenarioRunPlanResult(
                query.Path,
                true,
                plan.TotalCount,
                plan.MatchedCount,
                plan.SelectedCount,
                plan.ShardedOutCount,
                query.ShardCount,
                query.ShardIndex,
                query.ShardStrategy,
                plan.SelectedScenarios);
        }

        if (configuration.ValidateOnly)
        {
            return await _scenarioRunOrchestrator.ValidatePathAsync(query, configuration).ConfigureAwait(false);
        }

        return await _scenarioRunOrchestrator.RunPathAsync(query, runner, configuration).ConfigureAwait(false);
    }
}
