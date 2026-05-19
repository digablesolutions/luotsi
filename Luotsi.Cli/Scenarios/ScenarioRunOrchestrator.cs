using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioRunOrchestrator(
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

    public async Task<ScenarioRunResult> RunFileAsync(string file, IDeviceHost runner, ScenarioRunConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(configuration);

        await using var runEvents = _scenarioRunEventCoordinatorFactory.Create(configuration.EventsJsonlPath);
        var runReports = _scenarioRunReportCoordinatorFactory.Create(configuration);
        return await RunFileCoreAsync(
            file,
            runner,
            configuration,
            runEvents,
            runReports).ConfigureAwait(false);
    }

    public async Task<ScenarioRunBatchResult> RunPathAsync(ScenarioQuery query, IDeviceHost runner, ScenarioRunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(configuration);

        await using var runEvents = _scenarioRunEventCoordinatorFactory.Create(configuration.EventsJsonlPath);
        var runReports = _scenarioRunReportCoordinatorFactory.Create(configuration);
        return await RunPathCoreAsync(
            query,
            runner,
            configuration,
            runEvents,
            runReports).ConfigureAwait(false);
    }

    private Task<ScenarioRunResult> RunFileCoreAsync(
        string file,
        IDeviceHost runner,
        ScenarioRunConfiguration configuration,
        ScenarioRunEventCoordinator runEvents,
        ScenarioRunReportCoordinator runReports) =>
        runReports.RunFileAsync(
            file,
            () => runEvents.RunFileAsync(
                file,
                sink => _scenarioExecutorFactory.Create(runner, sink, configuration.FailureArtifactCapturePolicy).RunAsync(file)));

    private Task<ScenarioRunBatchResult> RunPathCoreAsync(
        ScenarioQuery query,
        IDeviceHost runner,
        ScenarioRunConfiguration configuration,
        ScenarioRunEventCoordinator runEvents,
        ScenarioRunReportCoordinator runReports) =>
        runEvents.RunPathAsync(
            query,
            _ => runReports.PlanPathAsync(query, () => _runPlanner.CreateAsync(query)),
            (preparedPlan, sink) => runReports.RunBatchAsync(
                preparedPlan,
                () => _scenarioBatchExecutorFactory.Create(runner, sink, configuration.FailureArtifactCapturePolicy).RunAsync(preparedPlan)));
}