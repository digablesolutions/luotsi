using Polly;
using Polly.Retry;

namespace Luotsi.Cli.Infrastructure.Resilience;

internal static class LuotsiResiliencePipelines
{
    public const string SetupDownloadPipelineName = "setup-download-retry";
    public const string LabProbePipelineName = "lab-probe-retry";

    public static ResiliencePipeline CreateSetupDownloadPipeline() =>
        new ResiliencePipelineBuilder()
            .AddRetry(CreateSetupDownloadRetryOptions())
            .Build();

    public static ResiliencePipeline CreateLabProbePipeline() =>
        new ResiliencePipelineBuilder()
            .AddRetry(CreateLabProbeRetryOptions())
            .Build();

    public static RetryStrategyOptions CreateSetupDownloadRetryOptions() =>
        new()
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = new PredicateBuilder().Handle<SetupDownloadTransientException>()
        };

    public static RetryStrategyOptions CreateLabProbeRetryOptions() =>
        new()
        {
            MaxRetryAttempts = 1,
            Delay = TimeSpan.FromMilliseconds(250),
            ShouldHandle = new PredicateBuilder().Handle<LabProbeTransientException>()
        };
}

internal sealed class SetupDownloadTransientException(string message) : Exception(message);

internal sealed class LabProbeTransientException(string message, int exitCode, string invocation) : Exception(message)
{
    public int ExitCode { get; } = exitCode;

    public string Invocation { get; } = invocation;
}
