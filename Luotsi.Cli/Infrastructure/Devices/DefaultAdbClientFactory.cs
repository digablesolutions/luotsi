using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Infrastructure.Contracts;
using Polly;
using Polly.Registry;

namespace Luotsi.Cli.Infrastructure.Devices;

public sealed class DefaultAdbClientFactory(ResiliencePipelineProvider<string>? resiliencePipelines = null) : IAdbClientFactory
{
    public IAdbClient Create(string executable, string? serial, IProcessRunner processRunner, TimeSpan? commandTimeout = null) =>
        new AdbClient(
            executable,
            serial,
            processRunner,
            commandTimeout,
            resiliencePipelines?.GetPipeline(AdbResilience.CommandRetryPipelineName));
}
