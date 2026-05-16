using VisitLab.Cli.Hosts.Android;

namespace VisitLab.Cli.Infrastructure;

public sealed class DefaultAdbClientFactory : IAdbClientFactory
{
    public IAdbClient Create(string executable, string? serial, IProcessRunner processRunner) =>
        new AdbClient(executable, serial, processRunner);
}