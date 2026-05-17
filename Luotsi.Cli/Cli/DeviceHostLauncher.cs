using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure;

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
                ResolveAdbCommandTimeout(options)),
            artifacts);
    }

    private TimeSpan? ResolveAdbCommandTimeout(CliOptions options)
    {
        var rawValue = options.Get("adb-timeout-sec") ??
            _environment.GetEnvironmentVariable(CliDefaults.AdbCommandTimeoutEnvironmentVariable) ??
            CliDefaults.DefaultAdbCommandTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (!int.TryParse(rawValue, out var timeoutSec) || timeoutSec < 0)
        {
            throw new UsageException("Option --adb-timeout-sec must be a non-negative integer.");
        }

        return timeoutSec == 0 ? null : TimeSpan.FromSeconds(timeoutSec);
    }
}
