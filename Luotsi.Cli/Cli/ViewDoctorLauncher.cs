using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure;

namespace Luotsi.Cli.Cli;

internal sealed class ViewDoctorLauncher(
    DeviceHostLauncher deviceHostLauncher,
    AppCommandHost commandHost)
{
    private readonly DeviceHostLauncher _deviceHostLauncher = deviceHostLauncher ?? throw new ArgumentNullException(nameof(deviceHostLauncher));
    private readonly AppCommandHost _commandHost = commandHost ?? throw new ArgumentNullException(nameof(commandHost));

    public PreparedHostedCommand Prepare(CliOptions options, DateTimeOffset started, string adbExecutable, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var runner = _deviceHostLauncher.Create(options, adbExecutable, artifacts);
        return new PreparedHostedCommand(
            runner,
            () => _commandHost.RunViewDoctorAsync(options, started, adbExecutable, runner, artifacts));
    }
}

internal sealed record PreparedHostedCommand(IDeviceHost Runner, Func<Task<int>> ExecuteAsync);