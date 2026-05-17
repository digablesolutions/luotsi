using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure;

namespace Luotsi.Cli.Cli;

internal sealed class AppCommandFamilyRouter(
    TimeProvider timeProvider,
    IFileSystem fileSystem,
    IEnvironmentVariables environment,
    ViewProfileCoordinator profileCoordinator,
    AppCommandHost commandHost,
    ViewSessionCommandPreparer viewSessionCommandPreparer,
    InspectSessionLauncher inspectSessionLauncher,
    ViewDoctorLauncher viewDoctorLauncher,
    DeviceHostLauncher deviceHostLauncher)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly ViewProfileCoordinator _profileCoordinator = profileCoordinator ?? throw new ArgumentNullException(nameof(profileCoordinator));
    private readonly AppCommandHost _commandHost = commandHost ?? throw new ArgumentNullException(nameof(commandHost));
    private readonly ViewSessionCommandPreparer _viewSessionCommandPreparer = viewSessionCommandPreparer ?? throw new ArgumentNullException(nameof(viewSessionCommandPreparer));
    private readonly InspectSessionLauncher _inspectSessionLauncher = inspectSessionLauncher ?? throw new ArgumentNullException(nameof(inspectSessionLauncher));
    private readonly ViewDoctorLauncher _viewDoctorLauncher = viewDoctorLauncher ?? throw new ArgumentNullException(nameof(viewDoctorLauncher));
    private readonly DeviceHostLauncher _deviceHostLauncher = deviceHostLauncher ?? throw new ArgumentNullException(nameof(deviceHostLauncher));

    public async Task<int> DispatchAsync(AppExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options;
        var started = context.Started;

        await _profileCoordinator.ApplyDefaultsAsync(options).ConfigureAwait(false);
        var adbExecutable = options.Get("adb") ?? _environment.GetEnvironmentVariable(CliDefaults.AdbExecutableEnvironmentVariable) ?? CliDefaults.DefaultAdbExecutable;
        var artifacts = ArtifactSession.Create(options, _fileSystem, _timeProvider);
        context.Artifacts = artifacts;

        if (string.Equals(options.Command, "profile-list", StringComparison.OrdinalIgnoreCase))
        {
            return await _commandHost.RunProfileListAsync(options, started, artifacts).ConfigureAwait(false);
        }

        if (string.Equals(options.Command, "profile-delete", StringComparison.OrdinalIgnoreCase))
        {
            return await _commandHost.RunProfileDeleteAsync(options, started, artifacts).ConfigureAwait(false);
        }

        if (string.Equals(options.Command, "inspect", StringComparison.OrdinalIgnoreCase))
        {
            return await _inspectSessionLauncher.RunAsync(options, adbExecutable, artifacts).ConfigureAwait(false);
        }

        if (IsViewCommand(options.Command))
        {
            var preparedViewSession = await _viewSessionCommandPreparer.PrepareAsync(options, adbExecutable, artifacts).ConfigureAwait(false);
            context.Runner = preparedViewSession.Runner;
            var exitCode = await preparedViewSession.Session.RunAsync(preparedViewSession.Options).ConfigureAwait(false);
            if (exitCode == 0)
            {
                await _viewSessionCommandPreparer.SaveLastAsync(options, preparedViewSession.Options).ConfigureAwait(false);
            }

            return exitCode;
        }

        if (string.Equals(options.Command, "view-doctor", StringComparison.OrdinalIgnoreCase))
        {
            var preparedViewDoctor = _viewDoctorLauncher.Prepare(options, started, adbExecutable, artifacts);
            context.Runner = preparedViewDoctor.Runner;
            return await preparedViewDoctor.ExecuteAsync().ConfigureAwait(false);
        }

        context.Runner = _deviceHostLauncher.Create(options, adbExecutable, artifacts);
        return await _commandHost.RunCommandAsync(options, started, context.Runner, artifacts).ConfigureAwait(false);
    }

    private static bool IsViewCommand(string? command) =>
        string.Equals(command, "view", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "reconnect", StringComparison.OrdinalIgnoreCase);
}