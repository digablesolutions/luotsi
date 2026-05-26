using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioRunOrchestrator(
    ScenarioRunPlanner runPlanner,
    ScenarioExecutorFactory scenarioExecutorFactory,
    ScenarioBatchExecutorFactory scenarioBatchExecutorFactory,
    ScenarioValidationExecutorFactory scenarioValidationExecutorFactory,
    ScenarioRunEventCoordinatorFactory scenarioRunEventCoordinatorFactory,
    ScenarioRunReportCoordinatorFactory scenarioRunReportCoordinatorFactory,
    IScenarioDeviceAllocator deviceAllocator)
{
    private readonly ScenarioRunPlanner _runPlanner = runPlanner ?? throw new ArgumentNullException(nameof(runPlanner));
    private readonly ScenarioExecutorFactory _scenarioExecutorFactory = scenarioExecutorFactory ?? throw new ArgumentNullException(nameof(scenarioExecutorFactory));
    private readonly ScenarioBatchExecutorFactory _scenarioBatchExecutorFactory = scenarioBatchExecutorFactory ?? throw new ArgumentNullException(nameof(scenarioBatchExecutorFactory));
    private readonly ScenarioValidationExecutorFactory _scenarioValidationExecutorFactory = scenarioValidationExecutorFactory ?? throw new ArgumentNullException(nameof(scenarioValidationExecutorFactory));
    private readonly ScenarioRunEventCoordinatorFactory _scenarioRunEventCoordinatorFactory = scenarioRunEventCoordinatorFactory ?? throw new ArgumentNullException(nameof(scenarioRunEventCoordinatorFactory));
    private readonly ScenarioRunReportCoordinatorFactory _scenarioRunReportCoordinatorFactory = scenarioRunReportCoordinatorFactory ?? throw new ArgumentNullException(nameof(scenarioRunReportCoordinatorFactory));
    private readonly IScenarioDeviceAllocator _deviceAllocator = deviceAllocator ?? throw new ArgumentNullException(nameof(deviceAllocator));

    public async Task<ScenarioRunResult> RunFileAsync(string file, IDeviceHost runner, ScenarioRunConfiguration configuration, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(configuration);

        await using var runEvents = _scenarioRunEventCoordinatorFactory.Create(configuration.EventsJsonlPath, artifacts, file, configuration.ProgressMode);
        var runReports = _scenarioRunReportCoordinatorFactory.Create(configuration, artifacts);
        return await RunFileCoreAsync(
            file,
            runner,
            configuration,
            runEvents,
            runReports).ConfigureAwait(false);
    }

    public async Task<ScenarioRunBatchResult> RunPathAsync(ScenarioQuery query, IDeviceHost runner, ScenarioRunConfiguration configuration, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(configuration);

        await using var runEvents = _scenarioRunEventCoordinatorFactory.Create(configuration.EventsJsonlPath, artifacts, query.Path, configuration.ProgressMode);
        var runReports = _scenarioRunReportCoordinatorFactory.Create(configuration, artifacts);
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

        await using var runEvents = _scenarioRunEventCoordinatorFactory.Create(configuration.EventsJsonlPath, progressMode: configuration.ProgressMode);
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

        await using var runEvents = _scenarioRunEventCoordinatorFactory.Create(configuration.EventsJsonlPath, progressMode: configuration.ProgressMode);
        var runReports = _scenarioRunReportCoordinatorFactory.Create(configuration);
        var preparedPlan = await PlanPathAsync(query, runEvents, runReports, () => _runPlanner.CreateAsync(query)).ConfigureAwait(false);
        return await ExecuteBatchAsync(
            preparedPlan,
            runEvents,
            runReports,
            sink => CreateValidationExecutor(sink).ValidatePlanAsync(preparedPlan)).ConfigureAwait(false);
    }

    private async Task<ScenarioRunResult> RunFileCoreAsync(
        string file,
        IDeviceHost runner,
        ScenarioRunConfiguration configuration,
        ScenarioRunEventCoordinator runEvents,
        ScenarioRunReportCoordinator runReports)
    {
        return await ExecuteFileAsync(
            file,
            runEvents,
            runReports,
            async sink =>
            {
                var allocation = await _deviceAllocator.AllocateAsync(runner, configuration).ConfigureAwait(false);
                try
                {
                    var result = await _scenarioExecutorFactory.Create(runner, sink, configuration.FailureArtifactCapturePolicy).RunAsync(file).ConfigureAwait(false);
                    return ScenarioMetadataCompatibility.Attach(result, allocation);
                }
                catch (Exception ex) when (!IsFatalException(ex) && ex is not UsageException)
                {
                    throw ScenarioFailureDetails.AttachDeviceAllocation(ex, allocation);
                }
            }).ConfigureAwait(false);
    }

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
            async sink =>
            {
                var allocation = await _deviceAllocator.AllocateAsync(runner, configuration).ConfigureAwait(false);
                try
                {
                    var result = await _scenarioBatchExecutorFactory.Create(runner, sink, configuration.FailureArtifactCapturePolicy).RunAsync(preparedPlan).ConfigureAwait(false);
                    return ScenarioMetadataCompatibility.Attach(result, allocation);
                }
                catch (Exception ex) when (!IsFatalException(ex) && ex is not UsageException)
                {
                    throw ScenarioFailureDetails.AttachDeviceAllocation(ex, allocation);
                }
            }).ConfigureAwait(false);
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
        _scenarioValidationExecutorFactory.Create(sink);

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}
