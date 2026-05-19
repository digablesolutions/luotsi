using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli;

internal sealed class InspectSessionLauncher(
    DeviceHostLauncher deviceHostLauncher,
    IConsoleIo console,
    TimeProvider timeProvider)
{
    private readonly DeviceHostLauncher _deviceHostLauncher = deviceHostLauncher ?? throw new ArgumentNullException(nameof(deviceHostLauncher));
    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<int> RunAsync(CliOptions options, string adbExecutable, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var runner = _deviceHostLauncher.Create(options, adbExecutable, artifacts);
        var inspectSession = new InspectSession(runner, _console, _timeProvider);
        return await inspectSession.RunAsync().ConfigureAwait(false);
    }
}