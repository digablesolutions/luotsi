using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.View.Diagnostics;

namespace Luotsi.Cli.Cli.View;

internal sealed class ViewDiagnosticCommandHost(ViewDiagnosticCommandHostDependencies dependencies)
{
    private readonly ViewDiagnosticCommandHostDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public async Task<int> RunAsync(ViewDiagnosticInvocation command, CliOptions options, DateTimeOffset started, string adbExecutable, IDeviceHost runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(artifacts);

        var viewOptions = BuildViewOptions(options, adbExecutable);
        if (command.Action == ViewDiagnosticAction.Setup)
        {
            var setup = await _dependencies.ViewSetupFactory.Create(runner).SetupAsync(viewOptions, command.Fix).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(command.EnvelopeCommand, started, setup, artifacts.ToData());
            return setup.Ready ? 0 : 1;
        }

        var report = await _dependencies.ViewDoctorFactory.Create(runner).DiagnoseAsync(viewOptions).ConfigureAwait(false);
        _dependencies.EnvelopeWriter.WriteSuccess(command.EnvelopeCommand, started, report, artifacts.ToData());
        return 0;
    }

    private Luotsi.Cli.View.Contracts.ViewOptions BuildViewOptions(CliOptions options, string adbExecutable)
    {
        var commandTimeout = AdbCommandTimeoutResolver.Resolve(options, _dependencies.Environment);
        return ViewCommandOptionsFactory.Build(options, adbExecutable, allowJoinShare: false, commandTimeout, options.Command ?? "view-doctor");
    }
}

internal sealed record ViewDiagnosticCommandHostDependencies(
    IEnvironmentVariables Environment,
    AppCommandEnvelopeWriter EnvelopeWriter,
    IViewDoctorFactory ViewDoctorFactory,
    IViewSetupFactory ViewSetupFactory);
