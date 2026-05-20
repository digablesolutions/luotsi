namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioValidationExecutorFactory(
    ScenarioCatalog scenarioCatalog,
    TimeProvider timeProvider,
    IScenarioMetricsCollector metricsCollector)
{
    private readonly ScenarioCatalog _scenarioCatalog = scenarioCatalog ?? throw new ArgumentNullException(nameof(scenarioCatalog));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IScenarioMetricsCollector _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));

    public ScenarioValidationExecutor Create(IScenarioEventSink? eventSink = null) =>
        new(_scenarioCatalog, _timeProvider, eventSink, _metricsCollector);
}