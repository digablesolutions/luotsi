using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli;

internal sealed class ViewDiagnosticsLauncher(
    DeviceHostLauncher deviceHostLauncher,
    ViewDiagnosticCommandHost commandHost)
{
    private readonly DeviceHostLauncher _deviceHostLauncher = deviceHostLauncher ?? throw new ArgumentNullException(nameof(deviceHostLauncher));
    private readonly ViewDiagnosticCommandHost _commandHost = commandHost ?? throw new ArgumentNullException(nameof(commandHost));

    public PreparedHostedCommand Prepare(ViewDiagnosticInvocation command, CliOptions options, DateTimeOffset started, string adbExecutable, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var runner = _deviceHostLauncher.Create(options, adbExecutable, artifacts);
        return new PreparedHostedCommand(
            runner,
            () => _commandHost.RunAsync(command, options, started, adbExecutable, runner, artifacts));
    }
}

internal sealed record PreparedHostedCommand(IDeviceHost Runner, Func<Task<int>> ExecuteAsync);