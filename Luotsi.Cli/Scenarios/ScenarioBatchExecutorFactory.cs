using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioBatchExecutorFactory(
    ScenarioExecutorFactory scenarioExecutorFactory,
    IScenarioMetricsCollector metricsCollector)
{
    private readonly ScenarioExecutorFactory _scenarioExecutorFactory = scenarioExecutorFactory ?? throw new ArgumentNullException(nameof(scenarioExecutorFactory));
    private readonly IScenarioMetricsCollector _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));

    public ScenarioBatchExecutor Create(
        IDeviceHost runner,
        IScenarioEventSink? eventSink = null,
        ScenarioFailureArtifactCapturePolicy failureArtifactCapturePolicy = ScenarioFailureArtifactCapturePolicy.Failure)
    {
        ArgumentNullException.ThrowIfNull(runner);
        return new ScenarioBatchExecutor(
            _scenarioExecutorFactory.Create(runner, eventSink, failureArtifactCapturePolicy),
            _metricsCollector);
    }
}
