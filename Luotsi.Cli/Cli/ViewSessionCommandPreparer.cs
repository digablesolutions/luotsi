using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.View;

namespace Luotsi.Cli.Cli;

internal sealed class ViewSessionCommandPreparer(
    DeviceHostLauncher deviceHostLauncher,
    IViewSessionFactory viewSessionFactory,
    ViewProfileCoordinator profileCoordinator)
{
    private readonly DeviceHostLauncher _deviceHostLauncher = deviceHostLauncher ?? throw new ArgumentNullException(nameof(deviceHostLauncher));
    private readonly IViewSessionFactory _viewSessionFactory = viewSessionFactory ?? throw new ArgumentNullException(nameof(viewSessionFactory));
    private readonly ViewProfileCoordinator _profileCoordinator = profileCoordinator ?? throw new ArgumentNullException(nameof(profileCoordinator));

    public async Task<PreparedViewSession> PrepareAsync(CliOptions options, string adbExecutable, ArtifactSession artifacts)
    {
        var viewOptions = ViewCommandOptionsFactory.Build(options, adbExecutable, allowJoinShare: true);
        await _profileCoordinator.SaveIfRequestedAsync(options, viewOptions).ConfigureAwait(false);
        var runner = string.IsNullOrWhiteSpace(viewOptions.JoinShareEndpoint)
            ? _deviceHostLauncher.Create(options, adbExecutable, artifacts, viewOptions.DeviceSelector)
            : new UnsupportedDeviceHost();
        var viewSession = _viewSessionFactory.Create(runner, artifacts);
        return new PreparedViewSession(viewOptions, runner, viewSession);
    }

    public Task SaveLastAsync(CliOptions options, ViewOptions viewOptions) =>
        _profileCoordinator.SaveLastAsync(options, viewOptions);
}

internal sealed record PreparedViewSession(ViewOptions Options, IDeviceHost Runner, IViewSession Session);