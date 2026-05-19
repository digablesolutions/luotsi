using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Composition;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Cli.Inspect;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli.Routing;

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

        var classification = AppCommandFamilyClassifier.Classify(options);
        switch (classification.Family)
        {
            case AppCommandFamily.ProfileList:
                return await _dependencies.CommandHost.RunProfileListAsync(options, started, artifacts).ConfigureAwait(false);

            case AppCommandFamily.ProfileDelete:
                return await _dependencies.CommandHost.RunProfileDeleteAsync(options, started, artifacts).ConfigureAwait(false);

            case AppCommandFamily.Inspect:
                return await _dependencies.InspectSessionLauncher.RunAsync(options, adbExecutable, artifacts).ConfigureAwait(false);

            case AppCommandFamily.ViewDiagnostics:
            {
                var preparedViewDiagnostic = _dependencies.ViewDiagnosticsLauncher.Prepare(
                    classification.ViewDiagnostic ?? throw new InvalidOperationException("View diagnostics classification requires an invocation."),
                    options,
                    started,
                    adbExecutable,
                    artifacts);
                context.Runner = preparedViewDiagnostic.Runner;
                return await preparedViewDiagnostic.ExecuteAsync().ConfigureAwait(false);
            }

            case AppCommandFamily.ViewSession:
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

            default:
                var deviceSelector = await DeviceSelectorResolver.ResolveAsync(options, adbExecutable, artifacts, options.Command, _dependencies.DeviceHostLauncher).ConfigureAwait(false);
                context.Runner = _dependencies.DeviceHostLauncher.Create(options, adbExecutable, artifacts, deviceSelector);
                return await _dependencies.CommandHost.RunCommandAsync(options, started, adbExecutable, context.Runner, artifacts).ConfigureAwait(false);
        }
    }
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
