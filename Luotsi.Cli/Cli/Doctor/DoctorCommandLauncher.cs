using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Cli.View;

namespace Luotsi.Cli.Cli.Doctor;

internal sealed class DoctorCommandLauncher(
    DeviceHostLauncher deviceHostLauncher,
    DoctorCommandHost commandHost)
{
    private readonly DeviceHostLauncher _deviceHostLauncher = deviceHostLauncher ?? throw new ArgumentNullException(nameof(deviceHostLauncher));
    private readonly DoctorCommandHost _commandHost = commandHost ?? throw new ArgumentNullException(nameof(commandHost));

    public PreparedHostedCommand Prepare(CliOptions options, DateTimeOffset started, string adbExecutable, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var runner = _deviceHostLauncher.Create(options, adbExecutable, artifacts);
        return new PreparedHostedCommand(
            runner,
            () => _commandHost.RunAsync(options, started, adbExecutable, runner, artifacts));
    }
}