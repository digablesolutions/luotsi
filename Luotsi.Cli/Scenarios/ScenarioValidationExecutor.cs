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
        var scenarioId = ScenarioIdentity.Create(file, scenario.Name);
        await _eventSink.EmitAsync(new ScenarioEvent("scenario_started", started, File: file, ScenarioId: scenarioId, Scenario: scenario.Name, Status: "validating")).ConfigureAwait(false);

        var steps = CreateStepResults(scenario).ToArray();
        var endedAt = _timeProvider.GetUtcNow();
        var durationMs = Math.Max(0, (endedAt - started).TotalMilliseconds);
        var timing = new ScenarioRunTiming(durationMs, 0, 0, durationMs);
        var metrics = _metricsCollector.CollectScenario(new ScenarioScenarioMetricContext("validated", timing, steps));

        await _eventSink.EmitAsync(new ScenarioEvent(
            "scenario_ended",
            endedAt,
            "validated",
            File: file,
            ScenarioId: scenarioId,
            Scenario: scenario.Name,
            DurationMs: durationMs,
            Metrics: metrics)).ConfigureAwait(false);

        return new ScenarioRunResult(
            scenario.Name,
            "validated",
            timing,
            metrics,
            steps,
            scenarioId,
            file);
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
        return result with { Metrics = _metricsCollector.CollectBatch(new ScenarioBatchMetricContext(result)) };
    }

    private IEnumerable<ScenarioStepResult> CreateStepResults(ScenarioFile scenario)
    {
        foreach (var (step, phase) in EnumerateSteps(scenario))
        {
            var timing = new ScenarioStepTiming(0, 0, GetConfiguredDelayMs(step), 0);
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

    private static int? GetConfiguredDelayMs(ScenarioStep step) => step.Action switch
    {
        "sleep" => Math.Max(0, step.Milliseconds ?? 1000),
        "tapPoint" => Math.Max(0, step.PostTapDelayMs ?? 300),
        "typePin" when !string.IsNullOrWhiteSpace(step.Text) => Math.Max(0, step.IntervalMs ?? 120) * step.Text.Count(char.IsDigit),
        _ => null
    };
}

internal sealed record ScenarioValidationStepResult(string Status);
