using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

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

    public bool RequiresRunner(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return _dependencies.CommandDispatcher.RequiresRunner(options);
    }

    public async Task<int> RunCommandAsync(CliOptions options, DateTimeOffset started, string adbExecutable, IDeviceHost? runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var data = await _dependencies.CommandDispatcher.ExecuteAsync(options.Command!, options, adbExecutable, runner, artifacts).ConfigureAwait(false);
        _dependencies.EnvelopeWriter.WriteSuccess(options.Command!, started, data, artifacts.ToData());
        return _dependencies.ExitCodeResolver.Resolve(data);
    }
}

internal sealed record AppCommandHostDependencies(
    AppCommandEnvelopeWriter EnvelopeWriter,
    AppCommandExitCodeResolver ExitCodeResolver,
    ViewProfileCoordinator ProfileCoordinator,
    AppCommandDispatcher CommandDispatcher);
