using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class AppCommandHost(
    AppCommandEnvelopeWriter envelopeWriter,
    AppCommandExitCodeResolver exitCodeResolver,
    ViewProfileCoordinator profileCoordinator,
    AppCommandDispatcher commandDispatcher)
{
    private readonly AppCommandEnvelopeWriter _envelopeWriter = envelopeWriter ?? throw new ArgumentNullException(nameof(envelopeWriter));
    private readonly AppCommandExitCodeResolver _exitCodeResolver = exitCodeResolver ?? throw new ArgumentNullException(nameof(exitCodeResolver));
    private readonly ViewProfileCoordinator _profileCoordinator = profileCoordinator ?? throw new ArgumentNullException(nameof(profileCoordinator));
    private readonly AppCommandDispatcher _commandDispatcher = commandDispatcher ?? throw new ArgumentNullException(nameof(commandDispatcher));

    public async Task<int> RunProfileListAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var profiles = await _profileCoordinator.ListAsync().ConfigureAwait(false);
        _envelopeWriter.WriteSuccess(options.Command!, started, new ViewProfileListResult(profiles), artifacts.ToData());
        return 0;
    }

    public async Task<int> RunProfileDeleteAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var profileName = options.Require("name");
        var deleted = await _profileCoordinator.DeleteAsync(profileName).ConfigureAwait(false);
        _envelopeWriter.WriteSuccess(options.Command!, started, new ViewProfileDeleteResult(profileName, deleted), artifacts.ToData());
        return 0;
    }

    public bool RequiresRunner(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return _commandDispatcher.RequiresRunner(options);
    }

    public async Task<int> RunCommandAsync(CliOptions options, DateTimeOffset started, string adbExecutable, IDeviceHost? runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var data = await _commandDispatcher.ExecuteAsync(options.Command!, options, adbExecutable, runner, artifacts).ConfigureAwait(false);
        _envelopeWriter.WriteSuccess(options.Command!, started, data, artifacts.ToData());
        return _exitCodeResolver.Resolve(data);
    }
}
