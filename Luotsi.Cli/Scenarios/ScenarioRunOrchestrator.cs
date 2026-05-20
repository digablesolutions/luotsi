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

    public async Task<ScenarioRunResult> ValidateFileAsync(string file, ScenarioRunConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(configuration);

        await using var runEvents = _scenarioRunEventCoordinatorFactory.Create(configuration.EventsJsonlPath);
        var runReports = _scenarioRunReportCoordinatorFactory.Create(configuration);
        return await ExecuteFileAsync(
            file,
            runEvents,
            runReports,
            sink => CreateValidationExecutor(sink).ValidateFileAsync(file)).ConfigureAwait(false);
    }

    public async Task<ScenarioRunBatchResult> ValidatePathAsync(ScenarioQuery query, ScenarioRunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(configuration);

        await using var runEvents = _scenarioRunEventCoordinatorFactory.Create(configuration.EventsJsonlPath);
        var runReports = _scenarioRunReportCoordinatorFactory.Create(configuration);
        var preparedPlan = await PlanPathAsync(query, runEvents, runReports, () => _runPlanner.CreateAsync(query)).ConfigureAwait(false);
        return await ExecuteBatchAsync(
            preparedPlan,
            runEvents,
            runReports,
            sink => CreateValidationExecutor(sink).ValidatePlanAsync(preparedPlan)).ConfigureAwait(false);
    }

    private Task<ScenarioRunResult> RunFileCoreAsync(
        string file,
        IDeviceHost runner,
        ScenarioRunConfiguration configuration,
        ScenarioRunEventCoordinator runEvents,
        ScenarioRunReportCoordinator runReports) =>
        ExecuteFileAsync(
            file,
            runEvents,
            runReports,
            sink => _scenarioExecutorFactory.Create(runner, sink, configuration.FailureArtifactCapturePolicy).RunAsync(file));

    private async Task<ScenarioRunBatchResult> RunPathCoreAsync(
        ScenarioQuery query,
        IDeviceHost runner,
        ScenarioRunConfiguration configuration,
        ScenarioRunEventCoordinator runEvents,
        ScenarioRunReportCoordinator runReports)
    {
        var preparedPlan = await PlanPathAsync(query, runEvents, runReports, () => _runPlanner.CreateAsync(query)).ConfigureAwait(false);
        return await ExecuteBatchAsync(
            preparedPlan,
            runEvents,
            runReports,
            sink => _scenarioBatchExecutorFactory.Create(runner, sink, configuration.FailureArtifactCapturePolicy).RunAsync(preparedPlan)).ConfigureAwait(false);
    }

    private Task<ScenarioRunResult> ExecuteFileAsync(
        string file,
        ScenarioRunEventCoordinator runEvents,
        ScenarioRunReportCoordinator runReports,
        Func<IScenarioEventSink, Task<ScenarioRunResult>> runAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(runEvents);
        ArgumentNullException.ThrowIfNull(runReports);
        ArgumentNullException.ThrowIfNull(runAsync);

        return ExecuteFileCoreAsync(file, runEvents, runReports, runAsync);
    }

    private async Task<ScenarioRunResult> ExecuteFileCoreAsync(
        string file,
        ScenarioRunEventCoordinator runEvents,
        ScenarioRunReportCoordinator runReports,
        Func<IScenarioEventSink, Task<ScenarioRunResult>> runAsync)
    {
        var reportScope = runReports.BeginScope();
        try
        {
            var result = await runEvents.RunFileAsync(file, runAsync).ConfigureAwait(false);
            await runReports.WriteFileAsync(file, result, reportScope).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await runReports.WriteFileFailureAsync(file, ex, reportScope).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<ScenarioRunBatchResult> ExecuteBatchAsync(
        ScenarioRunPlan plan,
        ScenarioRunEventCoordinator runEvents,
        ScenarioRunReportCoordinator runReports,
        Func<IScenarioEventSink, Task<ScenarioRunBatchResult>> runAsync)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runEvents);
        ArgumentNullException.ThrowIfNull(runReports);
        ArgumentNullException.ThrowIfNull(runAsync);

        var reportScope = runReports.BeginScope();
        try
        {
            var result = await runEvents.RunBatchAsync(plan, runAsync).ConfigureAwait(false);
            await runReports.WriteBatchAsync(result, reportScope).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await runReports.WriteBatchFailureAsync(plan, ex, reportScope).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<ScenarioRunPlan> PlanPathAsync(
        ScenarioQuery query,
        ScenarioRunEventCoordinator runEvents,
        ScenarioRunReportCoordinator runReports,
        Func<Task<ScenarioRunPlan>> planAsync)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(runEvents);
        ArgumentNullException.ThrowIfNull(runReports);
        ArgumentNullException.ThrowIfNull(planAsync);

        var reportScope = runReports.BeginScope();
        try
        {
            return await runEvents.PlanPathAsync(query, planAsync).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await runReports.WriteQueryFailureAsync(query, ex, reportScope).ConfigureAwait(false);
            throw;
        }
    }

    private ScenarioValidationExecutor CreateValidationExecutor(IScenarioEventSink sink) =>
        new(
            new ScenarioCatalog(
                _scenarioExecutorFactory.FileSystem,
                _scenarioExecutorFactory.TemplateResolver),
            _scenarioExecutorFactory.TimeProvider,
            sink,
            _scenarioExecutorFactory.MetricsCollector);
}
