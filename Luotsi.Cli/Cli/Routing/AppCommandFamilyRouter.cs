using Luotsi.Cli.Cli.Composition;
using Luotsi.Cli.Cli.Doctor;
using Luotsi.Cli.Cli.Inspect;
using Luotsi.Cli.Cli.Replay;
using Luotsi.Cli.Cli.View;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class AppCommandFamilyRouter(
    AppCommandRouteBootstrapper routeBootstrapper,
    AppCommandHost commandHost,
    ReplayCommandHost replayCommandHost,
    ViewSessionCommandPreparer viewSessionCommandPreparer,
    InspectSessionLauncher inspectSessionLauncher,
    ViewDiagnosticsLauncher viewDiagnosticsLauncher,
    DoctorCommandLauncher doctorCommandLauncher,
    ArtifactCommandHost artifactCommandHost)
{
    private readonly AppCommandRouteBootstrapper _routeBootstrapper = routeBootstrapper ?? throw new ArgumentNullException(nameof(routeBootstrapper));
    private readonly AppCommandHost _commandHost = commandHost ?? throw new ArgumentNullException(nameof(commandHost));
    private readonly ReplayCommandHost _replayCommandHost = replayCommandHost ?? throw new ArgumentNullException(nameof(replayCommandHost));
    private readonly ViewSessionCommandPreparer _viewSessionCommandPreparer = viewSessionCommandPreparer ?? throw new ArgumentNullException(nameof(viewSessionCommandPreparer));
    private readonly InspectSessionLauncher _inspectSessionLauncher = inspectSessionLauncher ?? throw new ArgumentNullException(nameof(inspectSessionLauncher));
    private readonly ViewDiagnosticsLauncher _viewDiagnosticsLauncher = viewDiagnosticsLauncher ?? throw new ArgumentNullException(nameof(viewDiagnosticsLauncher));
    private readonly DoctorCommandLauncher _doctorCommandLauncher = doctorCommandLauncher ?? throw new ArgumentNullException(nameof(doctorCommandLauncher));
    private readonly ArtifactCommandHost _artifactCommandHost = artifactCommandHost ?? throw new ArgumentNullException(nameof(artifactCommandHost));

    public async Task<int> DispatchAsync(AppExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options;
        var started = context.Started;
        var routeSetup = await _routeBootstrapper.PrepareAsync(context).ConfigureAwait(false);

        var classification = AppCommandFamilyClassifier.Classify(options);
        switch (classification.Family)
        {
            case AppCommandFamily.ProfileList:
                return await _commandHost.RunProfileListAsync(options, started, routeSetup.Artifacts).ConfigureAwait(false);

            case AppCommandFamily.ProfileDelete:
                return await _commandHost.RunProfileDeleteAsync(options, started, routeSetup.Artifacts).ConfigureAwait(false);

            case AppCommandFamily.Artifacts:
                return await _artifactCommandHost.RunAsync(options, started, routeSetup.Artifacts).ConfigureAwait(false);

            case AppCommandFamily.Inspect:
                return await _inspectSessionLauncher.RunAsync(options, routeSetup.AdbExecutable, routeSetup.Artifacts).ConfigureAwait(false);

            case AppCommandFamily.Doctor:
            {
                var preparedDoctor = _doctorCommandLauncher.Prepare(options, started, routeSetup.AdbExecutable, routeSetup.Artifacts);
                context.Runner = preparedDoctor.Runner;
                return await preparedDoctor.ExecuteAsync().ConfigureAwait(false);
            }

            case AppCommandFamily.Replay:
                return await _replayCommandHost.RunAsync(options, started, routeSetup.Artifacts).ConfigureAwait(false);

            case AppCommandFamily.ViewDiagnostics:
            {
                var preparedViewDiagnostic = _viewDiagnosticsLauncher.Prepare(
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
                var preparedViewSession = await _viewSessionCommandPreparer.PrepareAsync(options, routeSetup.AdbExecutable, routeSetup.Artifacts).ConfigureAwait(false);
                context.Runner = preparedViewSession.Runner;
                var exitCode = await preparedViewSession.Session.RunAsync(preparedViewSession.Options).ConfigureAwait(false);
                if (exitCode == 0)
                {
                    await _viewSessionCommandPreparer.SaveLastAsync(options, preparedViewSession.Options).ConfigureAwait(false);
                }

                return exitCode;
            }

            default:
                _routeBootstrapper.ValidateHostedCommandPrerequisites(options);
                if (_commandHost.RequiresRunner(options))
                {
                    context.Runner = await _routeBootstrapper.PrepareHostedCommandRunnerAsync(options, routeSetup).ConfigureAwait(false);
                }

                return await _commandHost.RunCommandAsync(options, started, routeSetup.AdbExecutable, context.Runner, routeSetup.Artifacts).ConfigureAwait(false);
        }
    }
}
