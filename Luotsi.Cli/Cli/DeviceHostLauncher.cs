using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure;

namespace Luotsi.Cli.Cli;

internal sealed class DeviceHostLauncher(IDeviceHostFactory deviceHostFactory)
{
    private readonly IDeviceHostFactory _deviceHostFactory = deviceHostFactory ?? throw new ArgumentNullException(nameof(deviceHostFactory));

    public IDeviceHost Create(CliOptions options, string adbExecutable, ArtifactSession artifacts, string? deviceSelector = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        return _deviceHostFactory.Create(
            new DeviceHostConfiguration(
                options.Get("platform") ?? CliDefaults.DefaultPlatform,
                adbExecutable,
                deviceSelector ?? options.Get("device")),
            artifacts);
    }
}