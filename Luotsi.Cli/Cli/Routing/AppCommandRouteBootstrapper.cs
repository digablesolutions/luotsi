using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Composition;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Cli.View;
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
        var artifacts = ArtifactSession.Create(options, _dependencies.FileSystem, _dependencies.TimeProvider);
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
            _dependencies.DeviceHostLauncher).ConfigureAwait(false);
        return _dependencies.DeviceHostLauncher.Create(options, setup.AdbExecutable, setup.Artifacts, deviceSelector);
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