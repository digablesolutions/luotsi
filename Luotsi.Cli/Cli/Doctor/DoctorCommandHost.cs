using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Diagnostics;

namespace Luotsi.Cli.Cli.Doctor;

internal sealed class DoctorCommandHost(
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

    public async Task<int> RunAsync(CliOptions options, DateTimeOffset started, string adbExecutable, IDeviceHost runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(artifacts);

        if (string.IsNullOrWhiteSpace(options.Get("device")) && string.IsNullOrWhiteSpace(options.Get("device-query")))
        {
            var guidance = await BuildDeviceGuidanceAsync(runner, options.Get("package")).ConfigureAwait(false);
            _envelopeWriter.WriteSuccess(options.Command ?? "doctor", started, guidance, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
            return string.Equals(guidance.Status, "ready_to_select", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        }

        var adbHost = runner as IAdbCommandHost
            ?? throw new InvalidOperationException("Command 'doctor' requires a direct adb-backed device host.");
        var fix = options.HasFlag("fix");
        var package = options.Get("package");
        var viewOptions = BuildViewOptions(options, adbExecutable);
        var adbStartServerCommand = BuildAdbStartServerCommand(viewOptions.AdbExecutable);

        var checks = new List<DoctorCheck>();
        var repairSteps = new List<ViewSetupStep>();

        await AddAdbCheckAsync(
            checks,
            "adb_server_status",
            "ADB server is ready.",
            "ADB server is not ready.",
            $"Run `{adbStartServerCommand}` or point Luotsi at a working adb binary with --adb or LUOTSI_ADB.",
            adbHost.GetAdbServerStatusAsync).ConfigureAwait(false);
        await AddAdbCheckAsync(
            checks,
            "adb_version",
            "ADB executable is ready.",
            "ADB executable is not ready.",
            "Install Android platform-tools or point Luotsi at a working adb binary with --adb or LUOTSI_ADB.",
            adbHost.GetAdbVersionAsync).ConfigureAwait(false);

        ViewDoctorResult viewReport;
        if (fix)
        {
            if (IsFfmpegDecoder(viewOptions))
            {
                await _ffmpegSetupProvisioner.StageAsync(repairSteps.Add).ConfigureAwait(false);
            }

            var setup = await _viewSetupFactory.Create(runner).SetupAsync(viewOptions, fix: true).ConfigureAwait(false);
            repairSteps.AddRange(setup.Steps);
            viewReport = setup.Doctor;
        }
        else
        {
            viewReport = await _viewDoctorFactory.Create(runner).DiagnoseAsync(viewOptions).ConfigureAwait(false);
        }

        PreflightResult? packagePreflight = null;
        if (!string.IsNullOrWhiteSpace(package))
        {
            packagePreflight = await AddPackagePreflightCheckAsync(checks, adbHost, package).ConfigureAwait(false);
        }

        var ready = checks.All(static check => check.Ok) && viewReport.Ready;
        var readinessPlan = BuildReadinessPlan(options, viewOptions, adbStartServerCommand, package, fix, checks, viewReport, repairSteps, ready);
        var result = new DoctorResult(
            ready,
            fix,
            adbExecutable,
            package,
            checks,
            packagePreflight,
            viewReport,
            repairSteps,
            readinessPlan,
            readinessPlan.RecommendedCommands);
        _envelopeWriter.WriteSuccess(options.Command ?? "doctor", started, result, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
        return result.Ready ? 0 : 1;
    }

    private static async Task<DoctorDeviceGuidanceResult> BuildDeviceGuidanceAsync(IDeviceHost runner, string? package)
    {
        try
        {
            var inventory = await runner.GetDevicesAsync().ConfigureAwait(false);
            var devices = inventory.Devices;
            var onlineDevices = devices
                .Where(device => string.Equals(device.Status, "device", StringComparison.OrdinalIgnoreCase))
                .Where(static device => !string.IsNullOrWhiteSpace(device.Serial))
                .ToArray();

            if (onlineDevices.Length == 1)
            {
                var serial = onlineDevices[0].Serial!;
                var doctorCommand = BuildDoctorCommand(serial, package, string.Empty, includeFix: false);
                var doctorFixCommand = BuildDoctorCommand(serial, package, string.Empty, includeFix: true);
                var recommendedCommands = new List<DoctorRecommendedCommandResult>
                {
                    new("doctor_selected", "Run the full first-run readiness report for the only online device.", doctorCommand),
                    new("doctor_fix_selected", "Apply Luotsi-owned setup fixes if the readiness report reports blockers.", doctorFixCommand),
                    new("inspect_selected", "Open an interactive inspect session after doctor is ready.", $"luotsi inspect --device {Quote(serial)}")
                };

                if (!string.IsNullOrWhiteSpace(package))
                {
                    recommendedCommands.Add(new DoctorRecommendedCommandResult("preflight_package", "Check target app readiness on the selected device.", BuildPreflightCommand(serial, package)));
                }

                return new DoctorDeviceGuidanceResult(
                    "ready_to_select",
                    $"Found one online device '{serial}'. Run the recommended doctor command to validate first-run readiness.",
                    doctorCommand,
                    devices,
                    [],
                    recommendedCommands);
            }

            if (onlineDevices.Length > 1)
            {
                var recommendedCommands = new List<DoctorRecommendedCommandResult>
                {
                    new("devices", "List attached devices and choose the intended serial.", "luotsi devices"),
                    new("doctor_with_device", "Run first-run readiness for one explicit device.", BuildDoctorCommand("<adb serial>", package, string.Empty, includeFix: false)),
                    new("lab_status", "Compare attached devices and refine a query when several are available.", "luotsi lab status")
                };

                return new DoctorDeviceGuidanceResult(
                    "needs_device",
                    $"Found {onlineDevices.Length} online devices. Choose one with --device or --device-query before running doctor.",
                    "luotsi doctor --device <adb serial>",
                    devices,
                    [
                        new DoctorReadinessBlocker(
                            "doctor",
                            "device_selection",
                            "Doctor needs one selected Android device.",
                            "Pass --device <adb serial> or --device-query <query>.",
                            "luotsi doctor --device <adb serial>")
                    ],
                    recommendedCommands);
            }

            return new DoctorDeviceGuidanceResult(
                "no_devices",
                devices.Count == 0
                    ? "No adb-visible devices were found. Connect a device or start an emulator before running doctor."
                    : "No online adb devices were found. Resolve offline/unauthorized devices before running doctor.",
                "luotsi devices",
                devices,
                [
                    new DoctorReadinessBlocker(
                        "doctor",
                        "device_visibility",
                        "No online adb device is available for first-run readiness.",
                        "Connect a device, authorize USB debugging, start an emulator, or run adb start-server.",
                        "luotsi devices")
                ],
                [
                    new DoctorRecommendedCommandResult("devices", "Refresh the adb-visible device list.", "luotsi devices"),
                    new DoctorRecommendedCommandResult("adb_start_server", "Start the adb server if no devices are visible.", "adb start-server"),
                    new DoctorRecommendedCommandResult("doctor_retry", "Retry doctor after an online device appears.", "luotsi doctor --device <adb serial>")
                ]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DoctorDeviceGuidanceResult(
                "device_inventory_failed",
                "Doctor could not read the adb device list.",
                "luotsi devices",
                [],
                [
                    new DoctorReadinessBlocker(
                        "doctor",
                        "device_inventory",
                        "Reading adb devices failed.",
                        ex.Message,
                        "luotsi devices")
                ],
                [
                    new DoctorRecommendedCommandResult("devices", "Run the device inventory command to see the adb error.", "luotsi devices"),
                    new DoctorRecommendedCommandResult("adb_start_server", "Start the adb server if adb is not responding.", "adb start-server")
                ]);
        }
    }

    private static DoctorReadinessPlan BuildReadinessPlan(
        CliOptions options,
        ViewOptions viewOptions,
        string adbStartServerCommand,
        string? package,
        bool fix,
        IReadOnlyList<DoctorCheck> checks,
        ViewDoctorResult viewReport,
        IReadOnlyList<ViewSetupStep> repairSteps,
        bool ready)
    {
        var deviceSelector = string.IsNullOrWhiteSpace(viewOptions.DeviceSelector)
            ? options.Get("device") ?? "<adb serial>"
            : viewOptions.DeviceSelector;
        var viewOptionSuffix = BuildViewOptionSuffix(options);
        var doctorCommand = BuildDoctorCommand(deviceSelector, package, viewOptionSuffix, includeFix: false);
        var doctorFixCommand = BuildDoctorCommand(deviceSelector, package, viewOptionSuffix, includeFix: true);
        var viewDoctorCommand = $"luotsi view-doctor --device {Quote(deviceSelector)}{viewOptionSuffix}";
        var viewSetupCommand = $"luotsi view setup --device {Quote(deviceSelector)}{viewOptionSuffix}";
        var preflightCommand = BuildPreflightCommand(deviceSelector, package);
        var runCommand = BuildRunCommand(deviceSelector, package);
        var inspectCommand = $"luotsi inspect --device {Quote(deviceSelector)}";
        var viewCommand = $"luotsi view --device {Quote(deviceSelector)}{viewOptionSuffix}";

        var blockers = new List<DoctorReadinessBlocker>();
        blockers.AddRange(checks
            .Where(static check => !check.Ok)
            .Select(check => new DoctorReadinessBlocker(
                "doctor",
                check.Name,
                check.Summary,
                check.Recommendation,
                ResolveDoctorCheckCommand(check.Name, doctorFixCommand, preflightCommand, adbStartServerCommand))));
        blockers.AddRange(viewReport.Checks
            .Where(static check => !check.Ok)
            .Select(check => new DoctorReadinessBlocker(
                "view",
                check.Name,
                check.Summary,
                check.Recommendation,
                ResolveViewCheckCommand(check.Name, doctorFixCommand, viewDoctorCommand, viewSetupCommand, viewCommand))));
        blockers.AddRange(repairSteps
            .Where(static step => string.Equals(step.Status, ViewStartupPhaseStatus.Failed, StringComparison.OrdinalIgnoreCase))
            .Select(step => new DoctorReadinessBlocker(
                "repair",
                step.Name,
                step.Summary,
                step.Recommendation,
                doctorFixCommand)));

        if (!ready && blockers.Count == 0)
        {
            blockers.Add(new DoctorReadinessBlocker(
                "doctor",
                "readiness",
                "Doctor did not report the selected device as ready.",
                "Review the nested doctor checks and rerun doctor after correcting the reported setup issue.",
                doctorCommand));
        }

        var recommendedCommands = new List<DoctorRecommendedCommandResult>();
        if (ready)
        {
            AddRecommendedCommand(recommendedCommands, "run_scenarios", "Run a reviewed scenario path on this device.", runCommand);
            AddRecommendedCommand(recommendedCommands, "inspect_device", "Open an interactive inspect session on this device.", inspectCommand);
            AddRecommendedCommand(recommendedCommands, "view_device", "Open live view for this device.", viewCommand);
            if (!string.IsNullOrWhiteSpace(package))
            {
                AddRecommendedCommand(recommendedCommands, "preflight_package", "Recheck target app readiness before a run.", preflightCommand);
            }
        }
        else
        {
            if (!fix)
            {
                AddRecommendedCommand(recommendedCommands, "doctor_fix", "Apply Luotsi-owned setup fixes and rerun readiness checks.", doctorFixCommand);
            }

            foreach (var blocker in blockers.Where(static blocker => !string.IsNullOrWhiteSpace(blocker.Command)))
            {
                AddRecommendedCommand(recommendedCommands, $"resolve_{blocker.Name}", $"Resolve blocker: {blocker.Summary}", blocker.Command!);
            }

            AddRecommendedCommand(recommendedCommands, "doctor_rerun", "Rerun the first-run readiness report after applying fixes.", doctorCommand);
        }

        var nextCommand = recommendedCommands.FirstOrDefault()?.Command;
        var status = ready ? "ready" : "blocked";
        var summary = ready
            ? fix && repairSteps.Count > 0
                ? "Doctor repairs completed and the selected device is ready for Luotsi workflows."
                : "The selected device is ready for Luotsi workflows."
            : $"Doctor found {blockers.Count} readiness blocker{(blockers.Count == 1 ? string.Empty : "s")}.";

        return new DoctorReadinessPlan(
            status,
            summary,
            nextCommand,
            blockers,
            recommendedCommands);
    }

    private static string? ResolveDoctorCheckCommand(string checkName, string doctorFixCommand, string preflightCommand, string adbStartServerCommand) =>
        checkName switch
        {
            "adb_server_status" => adbStartServerCommand,
            "package_preflight" => preflightCommand,
            _ => doctorFixCommand
        };

    private static string BuildAdbStartServerCommand(string adbExecutable) =>
        $"{Quote(string.IsNullOrWhiteSpace(adbExecutable) ? "adb" : adbExecutable)} start-server";

    private static string? ResolveViewCheckCommand(string checkName, string doctorFixCommand, string viewDoctorCommand, string viewSetupCommand, string viewCommand) =>
        checkName switch
        {
            "device_visibility" => "adb devices",
            "capture_backend" => viewDoctorCommand,
            "mediaprojection_api" => viewDoctorCommand,
            "mediaprojection_encoder" => viewDoctorCommand,
            "mediaprojection_consent" => viewCommand,
            "preflight" => viewDoctorCommand,
            "recording" => viewDoctorCommand,
            "decoder" => doctorFixCommand,
            "helper_package" => viewSetupCommand,
            _ => doctorFixCommand
        };

    private static void AddRecommendedCommand(List<DoctorRecommendedCommandResult> commands, string kind, string summary, string command)
    {
        if (commands.Any(existing => string.Equals(existing.Command, command, StringComparison.Ordinal)))
        {
            return;
        }

        commands.Add(new DoctorRecommendedCommandResult(kind, summary, command));
    }

    private static string BuildDoctorCommand(string deviceSelector, string? package, string viewOptionSuffix, bool includeFix)
    {
        var command = $"luotsi doctor --device {Quote(deviceSelector)}";
        if (!string.IsNullOrWhiteSpace(package))
        {
            command += $" --package {Quote(package)}";
        }

        command += viewOptionSuffix;
        return includeFix ? command + " --fix" : command;
    }

    private static string BuildPreflightCommand(string deviceSelector, string? package)
    {
        var command = $"luotsi preflight --device {Quote(deviceSelector)}";
        if (!string.IsNullOrWhiteSpace(package))
        {
            command += $" --package {Quote(package)}";
        }

        return command;
    }

    private static string BuildRunCommand(string deviceSelector, string? package)
    {
        var command = $"luotsi run --path <scenarios> --device {Quote(deviceSelector)}";
        if (!string.IsNullOrWhiteSpace(package))
        {
            command += $" --package {Quote(package)}";
        }

        return command;
    }

    private static string BuildViewOptionSuffix(CliOptions options)
    {
        var parts = new List<string>();
        AddOption(parts, options, "adb");
        AddOption(parts, options, "adb-timeout-sec");
        AddOption(parts, options, "profile");
        AddOption(parts, options, "preset");
        AddFlag(parts, options, "defaults");
        AddFlag(parts, options, "read-only");
        AddFlag(parts, options, "headless");
        AddFlag(parts, options, "overlay-screen-state");
        AddFlag(parts, options, "overlay-telemetry");
        AddFlag(parts, options, "always-on-top");
        AddOption(parts, options, "decoder");
        AddOption(parts, options, "capture-backend");
        AddOption(parts, options, "codec");
        AddOption(parts, options, "record");
        AddOption(parts, options, "max-size");
        AddOption(parts, options, "max-fps");
        AddOption(parts, options, "video-bit-rate");
        AddOption(parts, options, "stats-interval-ms");
        AddOption(parts, options, "renderer-stats-interval-ms");
        AddOption(parts, options, "scale-mode");

        return parts.Count == 0 ? string.Empty : " " + string.Join(" ", parts);
    }

    private static void AddOption(List<string> parts, CliOptions options, string name)
    {
        var value = options.Get(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"--{name} {Quote(value)}");
        }
    }

    private static void AddFlag(List<string> parts, CliOptions options, string name)
    {
        if (options.HasFlag(name))
        {
            parts.Add($"--{name}");
        }
    }

    private static string Quote(string value) =>
        value.Any(static ch => char.IsWhiteSpace(ch) || ch == '"')
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

    private static async Task AddAdbCheckAsync(
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

    private static async Task<PreflightResult?> AddPackagePreflightCheckAsync(List<DoctorCheck> checks, IAdbCommandHost adbHost, string package)
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
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException)
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
        var commandTimeout = AdbCommandTimeoutResolver.Resolve(options, _environment);
        return ViewCommandOptionsFactory.Build(options, adbExecutable, allowJoinShare: false, commandTimeout, options.Command ?? "doctor");
    }

    private static bool IsFfmpegDecoder(Luotsi.Cli.View.Contracts.ViewOptions options) =>
        string.Equals(options.Decoder, "ffmpeg", StringComparison.OrdinalIgnoreCase);
}
