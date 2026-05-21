using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioExecutorFactory(
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    IDelay delay,
    IScenarioTemplateResolver templateResolver,
    IScenarioMetricsCollector metricsCollector)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    private readonly IScenarioTemplateResolver _templateResolver = templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));
    private readonly IScenarioMetricsCollector _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));

    public ScenarioExecutor Create(
        IScenarioActionHost actionHost,
        IScenarioEventSink? eventSink = null,
        ScenarioFailureArtifactCapturePolicy failureArtifactCapturePolicy = ScenarioFailureArtifactCapturePolicy.Failure)
    {
        ArgumentNullException.ThrowIfNull(actionHost);
        return new ScenarioExecutor(
            actionHost,
            actionHost as IScenarioScreenshotAssertionHost
                ?? throw new ArgumentException($"{nameof(actionHost)} must implement {nameof(IScenarioScreenshotAssertionHost)}.", nameof(actionHost)),
            _fileSystem,
            _timeProvider,
            _delay,
            _templateResolver,
            eventSink,
            failureArtifactCapturePolicy,
            _metricsCollector);
    }
}
