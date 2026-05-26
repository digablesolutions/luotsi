using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.View.Diagnostics;

namespace Luotsi.Cli.Cli.View;

internal sealed class ViewDiagnosticCommandHost(
    IEnvironmentVariables environment,
    AppCommandEnvelopeWriter envelopeWriter,
    IViewDoctorFactory viewDoctorFactory,
    IViewSetupFactory viewSetupFactory,
    FfmpegSetupProvisioner ffmpegSetupProvisioner)
{
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly AppCommandEnvelopeWriter _envelopeWriter = envelopeWriter ?? throw new ArgumentNullException(nameof(envelopeWriter));
    private readonly IViewDoctorFactory _viewDoctorFactory = viewDoctorFactory ?? throw new ArgumentNullException(nameof(viewDoctorFactory));
    private readonly IViewSetupFactory _viewSetupFactory = viewSetupFactory ?? throw new ArgumentNullException(nameof(viewSetupFactory));
    private readonly FfmpegSetupProvisioner _ffmpegSetupProvisioner = ffmpegSetupProvisioner ?? throw new ArgumentNullException(nameof(ffmpegSetupProvisioner));

    public async Task<int> RunAsync(ViewDiagnosticInvocation command, CliOptions options, DateTimeOffset started, string adbExecutable, IDeviceHost runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(artifacts);

        var viewOptions = BuildViewOptions(options, adbExecutable);
        if (command.Action == ViewDiagnosticAction.Setup)
        {
            var repairSteps = new List<ViewSetupStep>();
            if (command.Fix && IsFfmpegDecoder(viewOptions))
            {
                await _ffmpegSetupProvisioner.StageAsync(repairSteps.Add).ConfigureAwait(false);
            }

            var setup = await _viewSetupFactory.Create(runner).SetupAsync(viewOptions, command.Fix).ConfigureAwait(false);
            var result = repairSteps.Count == 0
                ? setup
                : setup with {Steps = repairSteps.Concat(setup.Steps).ToArray()};
            _envelopeWriter.WriteSuccess(command.EnvelopeCommand, started, result, artifacts.ToData());
            return result.Ready ? 0 : 1;
        }

        var report = await _viewDoctorFactory.Create(runner).DiagnoseAsync(viewOptions).ConfigureAwait(false);
        _envelopeWriter.WriteSuccess(command.EnvelopeCommand, started, report, artifacts.ToData());
        return 0;
    }

    private Luotsi.Cli.View.Contracts.ViewOptions BuildViewOptions(CliOptions options, string adbExecutable)
    {
        var commandTimeout = AdbCommandTimeoutResolver.Resolve(options, _environment);
        return ViewCommandOptionsFactory.Build(options, adbExecutable, allowJoinShare: false, commandTimeout, options.Command ?? "view-doctor");
    }

    private static bool IsFfmpegDecoder(Luotsi.Cli.View.Contracts.ViewOptions options) =>
        string.Equals(options.Decoder, "ffmpeg", StringComparison.OrdinalIgnoreCase);
}
