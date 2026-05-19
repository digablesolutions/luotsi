using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli;

internal sealed class DeviceHostLauncher(IDeviceHostFactory deviceHostFactory, IEnvironmentVariables environment)
{
    private readonly IDeviceHostFactory _deviceHostFactory = deviceHostFactory ?? throw new ArgumentNullException(nameof(deviceHostFactory));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public IDeviceHost Create(CliOptions options, string adbExecutable, ArtifactSession artifacts, string? deviceSelector = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        return _deviceHostFactory.Create(
            new DeviceHostConfiguration(
                options.Get("platform") ?? CliDefaults.DefaultPlatform,
                adbExecutable,
                deviceSelector ?? options.Get("device"),
                AdbCommandTimeoutResolver.Resolve(options, _environment)),
            artifacts);
    }
}
