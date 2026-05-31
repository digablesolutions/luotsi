using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Composition;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class AppCommandRouteBootstrapper(
    TimeProvider timeProvider,
    IFileSystem fileSystem,
    IEnvironmentVariables environment,
    ViewProfileCoordinator profileCoordinator,
    DeviceHostLauncher deviceHostLauncher,
    LabLeaseStore labLeaseStore,
    LabQuarantineStore labQuarantineStore,
    LabDeviceInventoryStore labDeviceInventoryStore)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly ViewProfileCoordinator _profileCoordinator = profileCoordinator ?? throw new ArgumentNullException(nameof(profileCoordinator));
    private readonly DeviceHostLauncher _deviceHostLauncher = deviceHostLauncher ?? throw new ArgumentNullException(nameof(deviceHostLauncher));
    private readonly LabLeaseStore _labLeaseStore = labLeaseStore ?? throw new ArgumentNullException(nameof(labLeaseStore));
    private readonly LabQuarantineStore _labQuarantineStore = labQuarantineStore ?? throw new ArgumentNullException(nameof(labQuarantineStore));
    private readonly LabDeviceInventoryStore _labDeviceInventoryStore = labDeviceInventoryStore ?? throw new ArgumentNullException(nameof(labDeviceInventoryStore));

    public async Task<AppCommandRouteSetup> PrepareAsync(AppExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options;
        await _profileCoordinator.ApplyDefaultsAsync(options).ConfigureAwait(false);

        var adbExecutable = options.Get("adb")
            ?? _environment.GetEnvironmentVariable(CliDefaults.AdbExecutableEnvironmentVariable)
            ?? CliDefaults.DefaultAdbExecutable;
        var artifacts = CreateArtifacts(options);
        context.Artifacts = artifacts;
        ValidateHostedCommandPrerequisites(options);

        return new AppCommandRouteSetup(adbExecutable, artifacts);
    }

    public async Task<IDeviceHost> PrepareHostedCommandRunnerAsync(CliOptions options, AppCommandRouteSetup setup)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(setup);

        var deviceSelector = await DeviceSelectorResolver.ResolveAsync(
            options,
            setup.AdbExecutable,
            setup.Artifacts,
            options.Command,
            _deviceHostLauncher,
            _labLeaseStore,
            _labQuarantineStore,
            _labDeviceInventoryStore).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(deviceSelector) && string.IsNullOrWhiteSpace(options.Get("device")))
        {
            options.ApplyDefaults(new Dictionary<string, string?> { ["device"] = deviceSelector });
        }

        return _deviceHostLauncher.Create(options, setup.AdbExecutable, setup.Artifacts, deviceSelector);
    }

    public void ValidateHostedCommandPrerequisites(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(options.Command, "run", StringComparison.Ordinal) ||
            options.HasFlag("validate-only") ||
            options.HasFlag("dry-run"))
        {
            return;
        }

        var file = options.Get("file");
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }

        if (!_fileSystem.FileExists(file))
        {
            throw new UsageException($"Scenario file '{file}' does not exist.");
        }
    }

    private ArtifactSession CreateArtifacts(CliOptions options)
    {
        if (string.Equals(options.Command, "replay", StringComparison.OrdinalIgnoreCase))
        {
            var artifactRoot = ResolveReplayArtifactRoot(options);
            return ArtifactSession.AttachExisting(artifactRoot, _fileSystem, options.Get("poll-artifacts"));
        }

        return ArtifactSession.Create(
            options,
            _fileSystem,
            _timeProvider,
            _environment,
            preferWorkspaceHome: ShouldPreferWorkspaceHome(options));
    }

    private string ResolveReplayArtifactRoot(CliOptions options)
    {
        var artifactRoot = options.Get("artifacts");
        if (IsReplayOpenCommand(options) && options.HasFlag("last"))
        {
            return ArtifactRootResolver.ResolveLatestArtifactRoot(_fileSystem, artifactRoot, _environment, preferWorkspaceHome: true);
        }

        if (!string.IsNullOrWhiteSpace(artifactRoot))
        {
            return artifactRoot;
        }

        if (IsReplayOpenCommand(options))
        {
            throw new UsageException("replay open requires --artifacts <directory> pointing to an existing artifact root, or use --last.");
        }

        throw new UsageException("replay requires --artifacts <directory> pointing to an existing artifact root.");
    }

    private static bool IsReplayOpenCommand(CliOptions options) =>
        options.Arguments.Count > 0 &&
        string.Equals(options.Arguments[0], "open", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldPreferWorkspaceHome(CliOptions options) =>
        string.Equals(options.Command, "run", StringComparison.OrdinalIgnoreCase);
}

internal sealed record AppCommandRouteSetup(string AdbExecutable, ArtifactSession Artifacts);
