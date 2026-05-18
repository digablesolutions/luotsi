using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioExecutorFactory(
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    IDelay delay,
    IScenarioTemplateResolver templateResolver)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    private readonly IScenarioTemplateResolver _templateResolver = templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));

    public ScenarioExecutor Create(IScenarioActionHost actionHost)
    {
        ArgumentNullException.ThrowIfNull(actionHost);
        return new ScenarioExecutor(actionHost, _fileSystem, _timeProvider, _delay, _templateResolver);
    }
}