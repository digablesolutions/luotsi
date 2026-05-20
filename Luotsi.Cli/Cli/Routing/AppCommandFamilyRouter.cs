using Luotsi.Cli.Cli.Composition;
using Luotsi.Cli.Cli.Inspect;
using Luotsi.Cli.Cli.View;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class AppCommandFamilyRouter(AppCommandFamilyRouterDependencies dependencies)
{
    private readonly AppCommandFamilyRouterDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public async Task<int> DispatchAsync(AppExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options;
        var started = context.Started;
        var routeSetup = await _dependencies.RouteBootstrapper.PrepareAsync(context).ConfigureAwait(false);

        var classification = AppCommandFamilyClassifier.Classify(options);
        switch (classification.Family)
        {
            case AppCommandFamily.ProfileList:
                return await _dependencies.CommandHost.RunProfileListAsync(options, started, routeSetup.Artifacts).ConfigureAwait(false);

            case AppCommandFamily.ProfileDelete:
                return await _dependencies.CommandHost.RunProfileDeleteAsync(options, started, routeSetup.Artifacts).ConfigureAwait(false);

            case AppCommandFamily.Inspect:
                return await _dependencies.InspectSessionLauncher.RunAsync(options, routeSetup.AdbExecutable, routeSetup.Artifacts).ConfigureAwait(false);

            case AppCommandFamily.ViewDiagnostics:
            {
                var preparedViewDiagnostic = _dependencies.ViewDiagnosticsLauncher.Prepare(
                    classification.ViewDiagnostic ?? throw new InvalidOperationException("View diagnostics classification requires an invocation."),
                    options,
                    started,
                    routeSetup.AdbExecutable,
                    routeSetup.Artifacts);
                context.Runner = preparedViewDiagnostic.Runner;
                return await preparedViewDiagnostic.ExecuteAsync().ConfigureAwait(false);
            }

            case AppCommandFamily.ViewSession:
            {
                var preparedViewSession = await _dependencies.ViewSessionCommandPreparer.PrepareAsync(options, routeSetup.AdbExecutable, routeSetup.Artifacts).ConfigureAwait(false);
                context.Runner = preparedViewSession.Runner;
                var exitCode = await preparedViewSession.Session.RunAsync(preparedViewSession.Options).ConfigureAwait(false);
                if (exitCode == 0)
                {
                    await _dependencies.ViewSessionCommandPreparer.SaveLastAsync(options, preparedViewSession.Options).ConfigureAwait(false);
                }

                return exitCode;
            }

            default:
                context.Runner = await _dependencies.RouteBootstrapper.PrepareHostedCommandRunnerAsync(options, routeSetup).ConfigureAwait(false);
                return await _dependencies.CommandHost.RunCommandAsync(options, started, routeSetup.AdbExecutable, context.Runner, routeSetup.Artifacts).ConfigureAwait(false);
        }
    }
}

internal sealed class AppCommandFamilyRouterDependencies
{
    public required AppCommandRouteBootstrapper RouteBootstrapper { get; init; }

    public required AppCommandHost CommandHost { get; init; }

    public required ViewSessionCommandPreparer ViewSessionCommandPreparer { get; init; }

    public required InspectSessionLauncher InspectSessionLauncher { get; init; }

    public required ViewDiagnosticsLauncher ViewDiagnosticsLauncher { get; init; }
}
