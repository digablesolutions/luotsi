using Luotsi.Cli.Cli.JourneyIntake;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli.Envelope;

internal sealed class AppCommandExitCodeResolver
{
    public static int Resolve(object result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            ScenarioRunBatchResult { CiPolicy: { ExitCodeApplied: true } ciPolicy } => ciPolicy.RecommendedExitCode,
            ScenarioRunResult { CiPolicy: { ExitCodeApplied: true } ciPolicy } => ciPolicy.RecommendedExitCode,
            ScenarioRunBatchResult { FailedCount: > 0 } => 1,
            ScenarioRunResult { Status: var status } when string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) => 1,
            JourneyIntakeValidationResult { Valid: false } => 1,
            _ => 0
        };
    }
}
