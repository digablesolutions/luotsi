using System.Linq;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Diagnostics;

namespace Luotsi.Cli.Cli.Doctor;

internal sealed class DoctorCommandHost(DoctorCommandHostDependencies dependencies)
{
    private readonly DoctorCommandHostDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public async Task<int> RunAsync(CliOptions options, DateTimeOffset started, string adbExecutable, IDeviceHost runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(artifacts);

        var adbHost = runner as IAdbCommandHost
            ?? throw new InvalidOperationException("Command 'doctor' requires a direct adb-backed device host.");
        var fix = options.HasFlag("fix");
        var package = options.Get("package");
        var viewOptions = BuildViewOptions(options, adbExecutable);

        var checks = new List<DoctorCheck>();
        var repairSteps = new List<ViewSetupStep>();

        await AddAdbCheckAsync(
            checks,
            "adb_server_status",
            "ADB server is ready.",
            "ADB server is not ready.",
            "Run `adb start-server` or point Luotsi at a working adb binary with --adb or LUOTSI_ADB.",
            adbHost.GetAdbServerStatusAsync).ConfigureAwait(false);
        await AddAdbCheckAsync(
            checks,
            "adb_version",
            "ADB executable is ready.",
            "ADB executable is not ready.",
            "Install Android platform-tools or point Luotsi at a working adb binary with --adb or LUOTSI_ADB.",
            adbHost.GetAdbVersionAsync).ConfigureAwait(false);

        var viewReport = await _dependencies.ViewDoctorFactory.Create(runner).DiagnoseAsync(viewOptions).ConfigureAwait(false);
        if (fix)
        {
            if (ShouldStageFfmpeg(viewOptions, viewReport))
            {
                await _dependencies.FfmpegSetupProvisioner.StageAsync(repairSteps.Add).ConfigureAwait(false);
            }

            var setup = await _dependencies.ViewSetupFactory.Create(runner).SetupAsync(viewOptions, fix: true).ConfigureAwait(false);
            repairSteps.AddRange(setup.Steps);
            viewReport = setup.Doctor;
        }

        PreflightResult? packagePreflight = null;
        if (!string.IsNullOrWhiteSpace(package))
        {
            packagePreflight = await AddPackagePreflightCheckAsync(checks, adbHost, package).ConfigureAwait(false);
        }

        var result = new DoctorResult(
            checks.All(static check => check.Ok) && viewReport.Ready,
            fix,
            adbExecutable,
            package,
            checks,
            packagePreflight,
            viewReport,
            repairSteps);
        _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "doctor", started, result, artifacts.ToData());
        return result.Ready ? 0 : 1;
    }

    private async Task AddAdbCheckAsync(
        List<DoctorCheck> checks,
        string name,
        string successSummary,
        string failureSummary,
        string recommendation,
        Func<Task<AdbDiagnosticResult>> action)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(result.Command.Stdout)
                ? result.Command.Stderr
                : result.Command.Stdout;
            checks.Add(new DoctorCheck(
                name,
                result.Command.Succeeded,
                result.Command.Succeeded ? successSummary : failureSummary,
                string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(),
                result.Command.Succeeded ? null : recommendation));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            checks.Add(new DoctorCheck(name, false, failureSummary, ex.Message, recommendation));
        }
    }

    private async Task<PreflightResult?> AddPackagePreflightCheckAsync(List<DoctorCheck> checks, IAdbCommandHost adbHost, string package)
    {
        try
        {
            var result = await adbHost.ReadPreflightAsync(package).ConfigureAwait(false);
            checks.Add(new DoctorCheck(
                "package_preflight",
                true,
                $"Package preflight passed for '{package}'.",
                result.CurrentFocus));
            return result;
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or System.IO.IOException)
        {
            checks.Add(new DoctorCheck(
                "package_preflight",
                false,
                $"Package preflight failed for '{package}'.",
                ex.Message,
                "Wake/unlock the device, start the target app, and confirm the requested package is foreground before rerunning doctor."));
            return null;
        }
    }

    private Luotsi.Cli.View.Contracts.ViewOptions BuildViewOptions(CliOptions options, string adbExecutable)
    {
        var commandTimeout = AdbCommandTimeoutResolver.Resolve(options, _dependencies.Environment);
        return ViewCommandOptionsFactory.Build(options, adbExecutable, allowJoinShare: false, commandTimeout, options.Command ?? "doctor");
    }

    private static bool ShouldStageFfmpeg(Luotsi.Cli.View.Contracts.ViewOptions options, ViewDoctorResult report) =>
        string.Equals(options.Decoder, "ffmpeg", StringComparison.OrdinalIgnoreCase) &&
        report.Checks.Any(static check => string.Equals(check.Name, "decoder", StringComparison.Ordinal) && !check.Ok);
}

internal sealed record DoctorCommandHostDependencies(
    IEnvironmentVariables Environment,
    AppCommandEnvelopeWriter EnvelopeWriter,
    IViewDoctorFactory ViewDoctorFactory,
    IViewSetupFactory ViewSetupFactory,
    FfmpegSetupProvisioner FfmpegSetupProvisioner);