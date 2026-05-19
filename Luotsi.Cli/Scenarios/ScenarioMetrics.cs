using System.Collections.ObjectModel;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal interface IScenarioMetricsCollector
{
    IReadOnlyDictionary<string, double> CollectStep(ScenarioStepMetricContext context);

    IReadOnlyDictionary<string, double> CollectScenario(ScenarioScenarioMetricContext context);

    IReadOnlyDictionary<string, double> CollectBatch(ScenarioBatchMetricContext context);
}

internal sealed record ScenarioStepMetricContext(
    ScenarioStep Step,
    string Phase,
    string Status,
    ScenarioStepTiming Timing);

internal sealed record ScenarioScenarioMetricContext(
    string Status,
    ScenarioRunTiming Timing,
    IReadOnlyList<ScenarioStepResult> Steps);

internal sealed record ScenarioBatchMetricContext(
    ScenarioRunBatchResult Result);

internal static class ScenarioMetrics
{
    public static readonly IReadOnlyDictionary<string, double> Empty =
        new ReadOnlyDictionary<string, double>(new Dictionary<string, double>());
}

internal sealed class CompositeScenarioMetricsCollector(IReadOnlyList<IScenarioMetricsCollector> collectors) : IScenarioMetricsCollector
{
    private readonly IReadOnlyList<IScenarioMetricsCollector> _collectors = collectors ?? throw new ArgumentNullException(nameof(collectors));

    public static CompositeScenarioMetricsCollector CreateDefault() =>
        new([
            new ScenarioTimingMetricsCollector(),
            new ScenarioActionMetricsCollector()
        ]);

    public IReadOnlyDictionary<string, double> CollectStep(ScenarioStepMetricContext context) =>
        Merge(_collectors.Select(collector => collector.CollectStep(context)));

    public IReadOnlyDictionary<string, double> CollectScenario(ScenarioScenarioMetricContext context) =>
        Merge(_collectors.Select(collector => collector.CollectScenario(context)));

    public IReadOnlyDictionary<string, double> CollectBatch(ScenarioBatchMetricContext context) =>
        Merge(_collectors.Select(collector => collector.CollectBatch(context)));

    private static IReadOnlyDictionary<string, double> Merge(IEnumerable<IReadOnlyDictionary<string, double>> metricSets)
    {
        var metrics = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var metricSet in metricSets)
        {
            foreach (var metric in metricSet)
            {
                metrics[metric.Key] = metric.Value;
            }
        }

        return metrics;
    }
}

internal sealed class ScenarioTimingMetricsCollector : IScenarioMetricsCollector
{
    public IReadOnlyDictionary<string, double> CollectStep(ScenarioStepMetricContext context) =>
        new SortedDictionary<string, double>(StringComparer.Ordinal)
        {
            ["configured_delay_ms"] = context.Timing.ConfiguredDelayMs ?? 0,
            ["harness_delay_ms"] = context.Timing.HarnessDelayMs,
            ["non_delay_ms"] = context.Timing.NonDelayMs,
            ["total_ms"] = context.Timing.TotalMs
        };

    public IReadOnlyDictionary<string, double> CollectScenario(ScenarioScenarioMetricContext context)
    {
        var stepCount = context.Steps.Count;
        var failedStepCount = context.Steps.Count(static step => step.Status == "failed");
        var continuedOnErrorStepCount = context.Steps.Count(static step => step.Status == "continued_on_error");
        var passedStepCount = stepCount - failedStepCount - continuedOnErrorStepCount;
        return new SortedDictionary<string, double>(StringComparer.Ordinal)
        {
            ["continued_on_error_step_count"] = continuedOnErrorStepCount,
            ["failed_step_count"] = failedStepCount,
            ["harness_delay_ms"] = context.Steps.Sum(static step => step.Timing.HarnessDelayMs),
            ["main_step_count"] = context.Steps.Count(static step => step.Phase == ScenarioStepPhases.Main),
            ["non_delay_ms"] = context.Steps.Sum(static step => step.Timing.NonDelayMs),
            ["non_step_ms"] = context.Timing.NonStepMs,
            ["passed_step_count"] = passedStepCount,
            ["prologue_ms"] = context.Timing.PrologueMs,
            ["setup_step_count"] = context.Steps.Count(static step => step.Phase == ScenarioStepPhases.Setup),
            ["slowest_step_ms"] = context.Steps.Count == 0 ? 0 : context.Steps.Max(static step => step.DurationMs),
            ["step_count"] = stepCount,
            ["steps_ms"] = context.Timing.StepsMs,
            ["teardown_step_count"] = context.Steps.Count(static step => step.Phase == ScenarioStepPhases.Teardown),
            ["total_ms"] = context.Timing.TotalMs
        };
    }

    public IReadOnlyDictionary<string, double> CollectBatch(ScenarioBatchMetricContext context)
    {
        var scenarios = context.Result.Scenarios;
        return new SortedDictionary<string, double>(StringComparer.Ordinal)
        {
            ["continued_on_error_step_count"] = scenarios.Sum(static scenario => GetMetric(GetMetrics(scenario), "continued_on_error_step_count")),
            ["failed_step_count"] = scenarios.Sum(static scenario => GetMetric(GetMetrics(scenario), "failed_step_count")),
            ["harness_delay_ms"] = scenarios.Sum(static scenario => GetMetric(GetMetrics(scenario), "harness_delay_ms")),
            ["non_delay_ms"] = scenarios.Sum(static scenario => GetMetric(GetMetrics(scenario), "non_delay_ms")),
            ["passed_scenario_count"] = context.Result.PassedCount,
            ["passed_step_count"] = scenarios.Sum(static scenario => GetMetric(GetMetrics(scenario), "passed_step_count")),
            ["scenario_count"] = context.Result.SelectedCount,
            ["slowest_scenario_ms"] = scenarios.Count == 0 ? 0 : scenarios.Max(static scenario => scenario.Timing?.TotalMs ?? 0),
            ["step_count"] = scenarios.Sum(static scenario => GetMetric(GetMetrics(scenario), "step_count"))
        };
    }

    private static IReadOnlyDictionary<string, double> GetMetrics(ScenarioBatchItemResult scenario) =>
        scenario.Metrics ?? ScenarioMetrics.Empty;

    private static double GetMetric(IReadOnlyDictionary<string, double> metrics, string key) =>
        metrics.TryGetValue(key, out var value) ? value : 0;
}

internal sealed class ScenarioActionMetricsCollector : IScenarioMetricsCollector
{
    public IReadOnlyDictionary<string, double> CollectStep(ScenarioStepMetricContext context) =>
        ScenarioMetrics.Empty;

    public IReadOnlyDictionary<string, double> CollectScenario(ScenarioScenarioMetricContext context)
    {
        var metrics = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var group in context.Steps.GroupBy(static step => step.Action, StringComparer.OrdinalIgnoreCase))
        {
            metrics[$"action.{NormalizeMetricSegment(group.Key)}.count"] = group.Count();
            metrics[$"action.{NormalizeMetricSegment(group.Key)}.ms"] = group.Sum(static step => step.DurationMs);
        }

        return metrics;
    }

    public IReadOnlyDictionary<string, double> CollectBatch(ScenarioBatchMetricContext context)
    {
        var metrics = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var scenario in context.Result.Scenarios)
        {
            foreach (var metric in (scenario.Metrics ?? ScenarioMetrics.Empty).Where(static metric => metric.Key.StartsWith("action.", StringComparison.Ordinal)))
            {
                metrics[metric.Key] = metrics.GetValueOrDefault(metric.Key) + metric.Value;
            }
        }

        return metrics;
    }

    private static string NormalizeMetricSegment(string value)
    {
        var chars = value.Select(static character =>
            char.IsAsciiLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : '_').ToArray();
        return new string(chars);
    }
}
