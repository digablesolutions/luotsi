using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Infrastructure.Devices;

/// <summary>
/// Default factory that currently supports Android hosts backed by ADB.
/// </summary>
public sealed class DefaultDeviceHostFactory(
    IAdbClientFactory adbClientFactory,
    IProcessRunner processRunner,
    IDelay delay,
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    IEnvironmentVariables environment,
    IUniqueIdGenerator idGenerator) : IDeviceHostFactory
{
    private readonly IAdbClientFactory _adbClientFactory = adbClientFactory ?? throw new ArgumentNullException(nameof(adbClientFactory));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IUniqueIdGenerator _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));

    /// <summary>
    /// Creates a concrete device host.
    /// </summary>
    /// <param name="configuration">Host creation parameters.</param>
    /// <param name="artifacts">Artifact session for the command.</param>
    /// <returns>Concrete device host.</returns>
    public IDeviceHost Create(DeviceHostConfiguration configuration, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(artifacts);

        if (!string.Equals(configuration.Platform, "android", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException($"Unsupported platform '{configuration.Platform}'. The current build only supports --platform android.");
        }

        var adb = _adbClientFactory.Create(configuration.Executable, configuration.DeviceSerial, _processRunner, configuration.CommandTimeout);
        return new DeviceRunner(adb, artifacts, _timeProvider, _delay, _fileSystem, _idGenerator, _environment);
    }
}
