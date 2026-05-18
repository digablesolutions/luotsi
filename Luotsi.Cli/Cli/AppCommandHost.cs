using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

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
    public required AppCommandEnvelopeWriter EnvelopeWriter { get; init; }

    public required AppCommandExitCodeResolver ExitCodeResolver { get; init; }

    public required ViewProfileCoordinator ProfileCoordinator { get; init; }

    public required AppCommandDispatcher CommandDispatcher { get; init; }
}
