using Luotsi.Cli.Hosts.Android;

namespace Luotsi.Cli.Infrastructure;

public sealed class DefaultAdbClientFactory : IAdbClientFactory
{
    public IAdbClient Create(string executable, string? serial, IProcessRunner processRunner) =>
        new AdbClient(executable, serial, processRunner);
}