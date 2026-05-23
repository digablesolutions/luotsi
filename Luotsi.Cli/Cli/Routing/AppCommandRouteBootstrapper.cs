using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Composition;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class AppCommandRouteBootstrapper(AppCommandRouteBootstrapperDependencies dependencies)
{
    private readonly AppCommandRouteBootstrapperDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public async Task<AppCommandRouteSetup> PrepareAsync(AppExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options;
        await _dependencies.ProfileCoordinator.ApplyDefaultsAsync(options).ConfigureAwait(false);

        var adbExecutable = options.Get("adb")
            ?? _dependencies.Environment.GetEnvironmentVariable(CliDefaults.AdbExecutableEnvironmentVariable)
            ?? CliDefaults.DefaultAdbExecutable;
        var artifacts = CreateArtifacts(options);
        context.Artifacts = artifacts;

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
            _dependencies.DeviceHostLauncher,
            new LabLeaseStore(_dependencies.FileSystem, _dependencies.TimeProvider),
            new LabQuarantineStore(_dependencies.FileSystem, _dependencies.TimeProvider)).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(deviceSelector) && string.IsNullOrWhiteSpace(options.Get("device")))
        {
            options.ApplyDefaults(new Dictionary<string, string?> { ["device"] = deviceSelector });
        }

        return _dependencies.DeviceHostLauncher.Create(options, setup.AdbExecutable, setup.Artifacts, deviceSelector);
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

        if (!_dependencies.FileSystem.FileExists(file))
        {
            throw new UsageException($"Scenario file '{file}' does not exist.");
        }
    }

    private ArtifactSession CreateArtifacts(CliOptions options)
    {
        if (string.Equals(options.Command, "replay", StringComparison.OrdinalIgnoreCase))
        {
            var artifactRoot = options.Get("artifacts") ?? throw new UsageException("replay requires --artifacts <directory> pointing to an existing artifact root.");
            return ArtifactSession.AttachExisting(artifactRoot, _dependencies.FileSystem, options.Get("poll-artifacts"));
        }

        return ArtifactSession.Create(options, _dependencies.FileSystem, _dependencies.TimeProvider);
    }
}

internal sealed class AppCommandRouteBootstrapperDependencies
{
    public required TimeProvider TimeProvider { get; init; }

    public required IFileSystem FileSystem { get; init; }

    public required IEnvironmentVariables Environment { get; init; }

    public required ViewProfileCoordinator ProfileCoordinator { get; init; }

    public required DeviceHostLauncher DeviceHostLauncher { get; init; }
}

internal sealed record AppCommandRouteSetup(string AdbExecutable, ArtifactSession Artifacts);
