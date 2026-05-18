using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioBatchExecutorFactory(ScenarioExecutorFactory scenarioExecutorFactory)
{
    private readonly ScenarioExecutorFactory _scenarioExecutorFactory = scenarioExecutorFactory ?? throw new ArgumentNullException(nameof(scenarioExecutorFactory));

    public ScenarioBatchExecutor Create(IDeviceHost runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        return new ScenarioBatchExecutor(_scenarioExecutorFactory.Create(runner));
    }
}