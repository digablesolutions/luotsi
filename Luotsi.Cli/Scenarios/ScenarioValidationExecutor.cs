using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioValidationExecutor(
    ScenarioCatalog scenarioCatalog,
    TimeProvider timeProvider,
    IScenarioEventSink? eventSink = null,
    IScenarioMetricsCollector? metricsCollector = null)
{
    private readonly ScenarioCatalog _scenarioCatalog = scenarioCatalog ?? throw new ArgumentNullException(nameof(scenarioCatalog));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IScenarioEventSink _eventSink = eventSink ?? NullScenarioEventSink.Instance;
    private readonly IScenarioMetricsCollector _metricsCollector = metricsCollector ?? CompositeScenarioMetricsCollector.CreateDefault();

    public async Task<ScenarioRunResult> ValidateFileAsync(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        var started = _timeProvider.GetUtcNow();
        var scenario = await _scenarioCatalog.LoadValidatedAsync(file, ScenarioExecutor.SupportedScenarioActions).ConfigureAwait(false);
        var lifecycle = new ScenarioLifecycleCoordinator(_timeProvider, _eventSink);
        var context = new ScenarioLifecycleContext(
            file,
            ScenarioIdentity.Create(file, scenario.Name),
            scenario.Name,
            started);

        return await lifecycle.RunAsync(
            context,
            startedStatus: "validating",
            lifecycleContext =>
            {
                var endedAt = _timeProvider.GetUtcNow();
                return Task.FromResult(new ScenarioLifecycleCompletion(
                    CreateValidatedResult(scenario, lifecycleContext, endedAt),
                    endedAt));
            }).ConfigureAwait(false);
    }

    public async Task<ScenarioRunBatchResult> ValidatePlanAsync(ScenarioRunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var results = new List<ScenarioBatchItemResult>(plan.SelectedCount);
        var failedCount = 0;
        foreach (var scenario in plan.SelectedScenarios)
        {
            try
            {
                results.Add(ScenarioBatchItemResult.FromSuccess(await ValidateFileAsync(scenario.File).ConfigureAwait(false), scenario));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedCount++;
                results.Add(ScenarioBatchItemResult.FromFailure(
                    scenario.Name,
                    scenario.File,
                    ScenarioFailureDetails.TryGetData(ex),
                    ScenarioErrorInfo.From(ex),
                    scenario.Id));
            }
        }

        var result = new ScenarioRunBatchResult(
            plan.Query.Path,
            failedCount == 0 ? "validated" : "failed",
            plan.TotalCount,
            plan.MatchedCount,
            plan.SelectedCount,
            0,
            failedCount,
            plan.ShardedOutCount,
            plan.Query.ShardCount,
            plan.Query.ShardIndex,
            results,
            plan.Query.ShardStrategy);
        return result with
        {
            Metrics = _metricsCollector.CollectBatch(new ScenarioBatchMetricContext(result)),
            Governance = ScenarioGovernanceClassifier.FromBatch(result)
        };
    }

    private IEnumerable<ScenarioStepResult> CreateStepResults(ScenarioFile scenario)
    {
        foreach (var (step, phase) in EnumerateSteps(scenario))
        {
            var timing = ScenarioTimingSupport.CreateStepTiming(step, 0, 0);
            var metrics = _metricsCollector.CollectStep(new ScenarioStepMetricContext(step, phase, "validated", timing));
            yield return new ScenarioStepResult(
                step.Name ?? step.Action,
                step.Action,
                0,
                timing,
                metrics,
                Result: new ScenarioValidationStepResult("validated"),
                Status: "validated",
                Phase: phase);
        }
    }

    private ScenarioRunResult CreateValidatedResult(ScenarioFile scenario, ScenarioLifecycleContext context, DateTimeOffset endedAt)
    {
        var steps = CreateStepResults(scenario).ToArray();
        var durationMs = Math.Max(0, (endedAt - context.StartedAt).TotalMilliseconds);
        var timing = new ScenarioRunTiming(durationMs, 0, 0, durationMs);
        var metrics = _metricsCollector.CollectScenario(new ScenarioScenarioMetricContext("validated", timing, steps));

        return new ScenarioRunResult(
            scenario.Name,
            "validated",
            timing,
            metrics,
            steps,
            null,
            context.ScenarioId,
            context.File,
            scenario.Metadata,
            Governance: ScenarioGovernanceClassifier.FromStatus("validated", null));
    }

    private static IEnumerable<(ScenarioStep Step, string Phase)> EnumerateSteps(ScenarioFile scenario)
    {
        foreach (var step in scenario.Setup ?? [])
        {
            yield return (step, ScenarioStepPhases.Setup);
        }

        foreach (var step in scenario.Steps)
        {
            yield return (step, ScenarioStepPhases.Main);
        }

        foreach (var step in scenario.Teardown ?? [])
        {
            yield return (step, ScenarioStepPhases.Teardown);
        }
    }

}

internal sealed record ScenarioValidationStepResult(string Status);
