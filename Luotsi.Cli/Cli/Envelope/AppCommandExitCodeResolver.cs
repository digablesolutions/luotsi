using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli.Envelope;

internal sealed class AppCommandExitCodeResolver
{
    public int Resolve(object result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result is ScenarioRunBatchResult { FailedCount: > 0 } ? 1 : 0;
    }
}