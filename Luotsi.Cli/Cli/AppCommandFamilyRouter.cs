using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli;

internal sealed class AppCommandFamilyRouter(AppCommandFamilyRouterDependencies dependencies)
{
    private readonly AppCommandFamilyRouterDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public async Task<int> DispatchAsync(AppExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options;
        var started = context.Started;

        await _dependencies.ProfileCoordinator.ApplyDefaultsAsync(options).ConfigureAwait(false);
        var adbExecutable = options.Get("adb") ?? _dependencies.Environment.GetEnvironmentVariable(CliDefaults.AdbExecutableEnvironmentVariable) ?? CliDefaults.DefaultAdbExecutable;
        var artifacts = ArtifactSession.Create(options, _dependencies.FileSystem, _dependencies.TimeProvider);
        context.Artifacts = artifacts;

        if (string.Equals(options.Command, "profile-list", StringComparison.OrdinalIgnoreCase))
        {
            return await _dependencies.CommandHost.RunProfileListAsync(options, started, artifacts).ConfigureAwait(false);
        }

        if (string.Equals(options.Command, "profile-delete", StringComparison.OrdinalIgnoreCase))
        {
            return await _dependencies.CommandHost.RunProfileDeleteAsync(options, started, artifacts).ConfigureAwait(false);
        }

        if (string.Equals(options.Command, "inspect", StringComparison.OrdinalIgnoreCase))
        {
            return await _dependencies.InspectSessionLauncher.RunAsync(options, adbExecutable, artifacts).ConfigureAwait(false);
        }

        if (IsViewSetupCommand(options))
        {
            var preparedViewSetup = _dependencies.ViewDiagnosticsLauncher.PrepareSetup(options, started, adbExecutable, artifacts);
            context.Runner = preparedViewSetup.Runner;
            return await preparedViewSetup.ExecuteAsync().ConfigureAwait(false);
        }

        if (IsViewCommand(options.Command))
        {
            var preparedViewSession = await _dependencies.ViewSessionCommandPreparer.PrepareAsync(options, adbExecutable, artifacts).ConfigureAwait(false);
            context.Runner = preparedViewSession.Runner;
            var exitCode = await preparedViewSession.Session.RunAsync(preparedViewSession.Options).ConfigureAwait(false);
            if (exitCode == 0)
            {
                await _dependencies.ViewSessionCommandPreparer.SaveLastAsync(options, preparedViewSession.Options).ConfigureAwait(false);
            }

            return exitCode;
        }

        if (string.Equals(options.Command, "view-doctor", StringComparison.OrdinalIgnoreCase))
        {
            var preparedViewDoctor = _dependencies.ViewDiagnosticsLauncher.PrepareDoctor(options, started, adbExecutable, artifacts);
            context.Runner = preparedViewDoctor.Runner;
            return await preparedViewDoctor.ExecuteAsync().ConfigureAwait(false);
        }

        context.Runner = _dependencies.DeviceHostLauncher.Create(options, adbExecutable, artifacts);
        return await _dependencies.CommandHost.RunCommandAsync(options, started, adbExecutable, context.Runner, artifacts).ConfigureAwait(false);
    }

    private static bool IsViewCommand(string? command) =>
        string.Equals(command, "view", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "reconnect", StringComparison.OrdinalIgnoreCase);

    private static bool IsViewSetupCommand(CliOptions options) =>
        string.Equals(options.Command, "view-setup", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(options.Command, "view", StringComparison.OrdinalIgnoreCase) &&
        options.Arguments.Count > 0 &&
        string.Equals(options.Arguments[0], "setup", StringComparison.OrdinalIgnoreCase);
}

internal sealed class AppCommandFamilyRouterDependencies
{
    public required TimeProvider TimeProvider { get; init; }

    public required IFileSystem FileSystem { get; init; }

    public required IEnvironmentVariables Environment { get; init; }

    public required ViewProfileCoordinator ProfileCoordinator { get; init; }

    public required AppCommandHost CommandHost { get; init; }

    public required ViewSessionCommandPreparer ViewSessionCommandPreparer { get; init; }

    public required InspectSessionLauncher InspectSessionLauncher { get; init; }

    public required ViewDiagnosticsLauncher ViewDiagnosticsLauncher { get; init; }

    public required DeviceHostLauncher DeviceHostLauncher { get; init; }
}
