using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Infrastructure.Devices;

public sealed class DefaultAdbClientFactory : IAdbClientFactory
{
    public IAdbClient Create(string executable, string? serial, IProcessRunner processRunner, TimeSpan? commandTimeout = null) =>
        new AdbClient(executable, serial, processRunner, commandTimeout);
}
