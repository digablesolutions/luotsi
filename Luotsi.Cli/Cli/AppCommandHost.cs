using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Diagnostics;

namespace Luotsi.Cli.Cli;

internal sealed class AppCommandHost(AppCommandHostDependencies dependencies)
{
    private readonly AppCommandHostDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public async Task<int> RunProfileListAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var profiles = await _dependencies.ProfileCoordinator.ListAsync().ConfigureAwait(false);
        _dependencies.EnvelopeWriter.WriteSuccess(options.Command!, started, new ViewProfileListResult(profiles), artifacts.ToData());
        return 0;
    }

    public async Task<int> RunProfileDeleteAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var profileName = options.Require("name");
        var deleted = await _dependencies.ProfileCoordinator.DeleteAsync(profileName).ConfigureAwait(false);
        _dependencies.EnvelopeWriter.WriteSuccess(options.Command!, started, new ViewProfileDeleteResult(profileName, deleted), artifacts.ToData());
        return 0;
    }

    public async Task<int> RunViewDoctorAsync(CliOptions options, DateTimeOffset started, string adbExecutable, IDeviceHost runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(artifacts);

        var commandTimeout = AdbCommandTimeoutResolver.Resolve(options, _dependencies.Environment);
        var viewOptions = ViewCommandOptionsFactory.Build(options, adbExecutable, allowJoinShare: false, commandTimeout);
        if (options.HasFlag("fix"))
        {
            var setup = await _dependencies.ViewSetupFactory.Create(runner).SetupAsync(viewOptions, fix: true).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command!, started, setup, artifacts.ToData());
            return setup.Ready ? 0 : 1;
        }

        var viewDoctor = _dependencies.ViewDoctorFactory.Create(runner);
        var report = await viewDoctor.DiagnoseAsync(viewOptions).ConfigureAwait(false);
        _dependencies.EnvelopeWriter.WriteSuccess(options.Command!, started, report, artifacts.ToData());
        return 0;
    }

    public async Task<int> RunViewSetupAsync(CliOptions options, DateTimeOffset started, string adbExecutable, IDeviceHost runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(artifacts);

        var commandTimeout = AdbCommandTimeoutResolver.Resolve(options, _dependencies.Environment);
        var viewOptions = ViewCommandOptionsFactory.Build(options, adbExecutable, allowJoinShare: false, commandTimeout);
        var setup = await _dependencies.ViewSetupFactory.Create(runner).SetupAsync(viewOptions, fix: !options.HasFlag("dry-run")).ConfigureAwait(false);
        _dependencies.EnvelopeWriter.WriteSuccess(options.Command!, started, setup, artifacts.ToData());
        return setup.Ready ? 0 : 1;
    }

    public async Task<int> RunCommandAsync(CliOptions options, DateTimeOffset started, string adbExecutable, IDeviceHost runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(artifacts);

        var data = await _dependencies.CommandDispatcher.ExecuteAsync(options.Command!, options, adbExecutable, runner).ConfigureAwait(false);
        _dependencies.EnvelopeWriter.WriteSuccess(options.Command!, started, data, artifacts.ToData());
        return _dependencies.ExitCodeResolver.Resolve(data);
    }
}

internal sealed class AppCommandHostDependencies
{
    public required IEnvironmentVariables Environment { get; init; }

    public required AppCommandEnvelopeWriter EnvelopeWriter { get; init; }

    public required AppCommandExitCodeResolver ExitCodeResolver { get; init; }

    public required ViewProfileCoordinator ProfileCoordinator { get; init; }

    public required AppCommandDispatcher CommandDispatcher { get; init; }

    public required IViewDoctorFactory ViewDoctorFactory { get; init; }

    public required IViewSetupFactory ViewSetupFactory { get; init; }
}
