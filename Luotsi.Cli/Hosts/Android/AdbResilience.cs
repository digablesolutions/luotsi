using Polly;
using Polly.Retry;

namespace Luotsi.Cli.Hosts.Android;

internal static class AdbResilience
{
    public const string CommandRetryPipelineName = "adb-command-retry";

    public static ResiliencePipeline CreateCommandRetryPipeline() =>
        new ResiliencePipelineBuilder()
            .AddRetry(CreateCommandRetryOptions())
            .Build();

    public static RetryStrategyOptions CreateCommandRetryOptions() =>
        new()
        {
            MaxRetryAttempts = 1,
            Delay = TimeSpan.Zero,
            ShouldHandle = new PredicateBuilder().Handle<AdbTransientTransportException>()
        };
}

internal sealed class AdbTransientTransportException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

