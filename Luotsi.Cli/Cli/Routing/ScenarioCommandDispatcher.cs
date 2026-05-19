using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class ScenarioCommandDispatcher(
    ScenarioRunPlanner runPlanner,
    ScenarioExecutorFactory scenarioExecutorFactory,
    ScenarioBatchExecutorFactory scenarioBatchExecutorFactory,
    ScenarioRunEventCoordinatorFactory scenarioRunEventCoordinatorFactory,
    ScenarioRunReportCoordinatorFactory scenarioRunReportCoordinatorFactory)
{
    private readonly ScenarioRunPlanner _runPlanner = runPlanner ?? throw new ArgumentNullException(nameof(runPlanner));
    private readonly ScenarioExecutorFactory _scenarioExecutorFactory = scenarioExecutorFactory ?? throw new ArgumentNullException(nameof(scenarioExecutorFactory));
    private readonly ScenarioBatchExecutorFactory _scenarioBatchExecutorFactory = scenarioBatchExecutorFactory ?? throw new ArgumentNullException(nameof(scenarioBatchExecutorFactory));
    private readonly ScenarioRunEventCoordinatorFactory _scenarioRunEventCoordinatorFactory = scenarioRunEventCoordinatorFactory ?? throw new ArgumentNullException(nameof(scenarioRunEventCoordinatorFactory));
    private readonly ScenarioRunReportCoordinatorFactory _scenarioRunReportCoordinatorFactory = scenarioRunReportCoordinatorFactory ?? throw new ArgumentNullException(nameof(scenarioRunReportCoordinatorFactory));

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

        var failureArtifactCapturePolicy = ParseFailureArtifactCapturePolicy(options.Get("capture-on"));

        if (!ScenarioQueryFactory.UsesCatalogExecution(options))
        {
            if (options.HasFlag("dry-run"))
            {
                throw new UsageException("run --dry-run requires --path. Use run --file <scenario.json> without --dry-run for single-scenario execution.");
            }

            var file = options.Require("file");
            await using var singleRunEvents = _scenarioRunEventCoordinatorFactory.Create(options.Get("events-jsonl"));
            var singleRunReports = _scenarioRunReportCoordinatorFactory.Create(options);
            return await singleRunReports.RunFileAsync(
                file,
                () => singleRunEvents.RunFileAsync(
                    file,
                    sink => _scenarioExecutorFactory.Create(runner, sink, failureArtifactCapturePolicy).RunAsync(file))).ConfigureAwait(false);
        }

        var query = ScenarioQueryFactory.CreateCatalogRunQuery(options);
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

        await using var batchRunEvents = _scenarioRunEventCoordinatorFactory.Create(options.Get("events-jsonl"));
        var batchRunReports = _scenarioRunReportCoordinatorFactory.Create(options);
        return await batchRunEvents.RunPathAsync(
            query,
            _ => batchRunReports.PlanPathAsync(query, () => _runPlanner.CreateAsync(query)),
            (preparedPlan, sink) => batchRunReports.RunBatchAsync(
                preparedPlan,
                () => _scenarioBatchExecutorFactory.Create(runner, sink, failureArtifactCapturePolicy).RunAsync(preparedPlan))).ConfigureAwait(false);
    }

    private static ScenarioFailureArtifactCapturePolicy ParseFailureArtifactCapturePolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ScenarioFailureArtifactCapturePolicy.Failure;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "failure" or "on-failure" or "onfailure" => ScenarioFailureArtifactCapturePolicy.Failure,
            "never" => ScenarioFailureArtifactCapturePolicy.Never,
            _ => throw new UsageException("--capture-on must be one of: failure, never.")
        };
    }
}
