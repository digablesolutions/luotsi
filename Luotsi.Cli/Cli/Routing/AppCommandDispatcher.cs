using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Discovery;
using Luotsi.Cli.Cli.JourneyIntake;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Cli.Update;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Infrastructure.Resilience;
using Luotsi.Cli.Models;
using Polly.Registry;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class AppCommandDispatcher(
    AdbSubcommandDispatcher adbSubcommandDispatcher,
    ScenarioCommandDispatcher scenarioCommandDispatcher,
    DiscoveryCommandService discoveryCommandService,
    ISelfUpdateService selfUpdateService,
    JourneyIntakeValidationService journeyIntakeValidationService,
    ViewProfileCoordinator profileCoordinator,
    LabLeaseStore labLeaseStore,
    LabLeaseClaimCoordinator labLeaseClaimCoordinator,
    LabQuarantineStore labQuarantineStore,
    LabDeviceInventoryStore labDeviceInventoryStore,
    ResiliencePipelineProvider<string> resiliencePipelines)
{
    private readonly AdbSubcommandDispatcher _adbSubcommandDispatcher = adbSubcommandDispatcher ?? throw new ArgumentNullException(nameof(adbSubcommandDispatcher));
    private readonly ScenarioCommandDispatcher _scenarioCommandDispatcher = scenarioCommandDispatcher ?? throw new ArgumentNullException(nameof(scenarioCommandDispatcher));
    private readonly DiscoveryCommandService _discoveryCommandService = discoveryCommandService ?? throw new ArgumentNullException(nameof(discoveryCommandService));
    private readonly ISelfUpdateService _selfUpdateService = selfUpdateService ?? throw new ArgumentNullException(nameof(selfUpdateService));
    private readonly JourneyIntakeValidationService _journeyIntakeValidationService = journeyIntakeValidationService ?? throw new ArgumentNullException(nameof(journeyIntakeValidationService));
    private readonly ViewProfileCoordinator _profileCoordinator = profileCoordinator ?? throw new ArgumentNullException(nameof(profileCoordinator));
    private readonly LabLeaseStore _labLeaseStore = labLeaseStore ?? throw new ArgumentNullException(nameof(labLeaseStore));
    private readonly LabLeaseClaimCoordinator _labLeaseClaimCoordinator = labLeaseClaimCoordinator ?? throw new ArgumentNullException(nameof(labLeaseClaimCoordinator));
    private readonly LabQuarantineStore _labQuarantineStore = labQuarantineStore ?? throw new ArgumentNullException(nameof(labQuarantineStore));
    private readonly LabDeviceInventoryStore _labDeviceInventoryStore = labDeviceInventoryStore ?? throw new ArgumentNullException(nameof(labDeviceInventoryStore));
    private readonly ResiliencePipelineProvider<string> _resiliencePipelines = resiliencePipelines ?? throw new ArgumentNullException(nameof(resiliencePipelines));

    public bool RequiresRunner(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Command switch
        {
            "scenario-list" => false,
            "scenario-init" => false,
            "scenario-validate" => false,
            "scenario-explain" => false,
            "quickstart" => false,
            "journey-intake" => false,
            "version" => false,
            "update" => false,
            "run" => ScenarioCommandDispatcher.RequiresRunner(options),
            _ => true
        };
    }

    public async Task<object> ExecuteAsync(string command, CliOptions options, string adbExecutable, IDeviceHost? runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        return command switch
        {
            "adb" => await AdbSubcommandDispatcher.ExecuteAsync(options, RequireAdbCommandHost(runner, command)).ConfigureAwait(false),
            "quickstart" => await QuickstartCommand.RunAsync(options, artifacts).ConfigureAwait(false),
            "version" => await _selfUpdateService.GetVersionInfoAsync().ConfigureAwait(false),
            "update" => await _selfUpdateService.UpdateAsync(options, artifacts.Root).ConfigureAwait(false),
            "devices" => DeviceInventory.FromDeviceList(await RequireRunner(runner, command).GetDevicesAsync().ConfigureAwait(false)),
            "device-status" => await DeviceStatusResolver.ReadAsync(RequireRunner(runner, command), RequireAdbCommandHost(runner, command), options.Get("device")).ConfigureAwait(false),
            "device-wait" or "wait-for-device" => await RequireAdbCommandHost(runner, command).WaitForDeviceAsync(options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "preflight" => await RequireAdbCommandHost(runner, command).PreflightAsync(options.Get("package")).ConfigureAwait(false),
            "wireless" => await GetWirelessHost(runner, command).EnableWirelessAsync(options.Get("host"), options.Int("port", 5555)).ConfigureAwait(false),
            "wireless-scan" => await GetWirelessHost(runner, command).ScanWirelessServicesAsync().ConfigureAwait(false),
            "wireless-pair" => await GetWirelessHost(runner, command).PairWirelessAsync(GetWirelessEndpoint(options, "wireless-pair"), options.Get("service"), options.Get("code") ?? options.Get("pairing-code")).ConfigureAwait(false),
            "wireless-connect" => await ConnectWirelessAsync(options, adbExecutable, GetWirelessHost(runner, command)).ConfigureAwait(false),
            "forward-list" => await RequireRunner(runner, command).ListForwardsAsync().ConfigureAwait(false),
            "forward" => await RequireRunner(runner, command).ForwardAsync(options.Require("local"), options.Require("remote"), options.HasFlag("no-rebind")).ConfigureAwait(false),
            "forward-remove" => await RequireRunner(runner, command).RemoveForwardAsync(options.Require("local")).ConfigureAwait(false),
            "reverse-list" => await RequireRunner(runner, command).ListReversesAsync().ConfigureAwait(false),
            "reverse" => await RequireRunner(runner, command).ReverseAsync(options.Require("remote"), options.Require("local"), options.HasFlag("no-rebind")).ConfigureAwait(false),
            "reverse-remove" => await RequireRunner(runner, command).RemoveReverseAsync(options.Require("remote")).ConfigureAwait(false),
            "start-app" => await RequireRunner(runner, command).StartAppAsync(options.Require("package"), options.Get("activity"), options.HasFlag("wait")).ConfigureAwait(false),
            "start-uri" => await RequireRunner(runner, command).StartUriAsync(options.Require("uri"), options.Get("package"), options.Get("activity"), options.Get("action"), options.HasFlag("wait")).ConfigureAwait(false),
            "force-stop" => await RequireRunner(runner, command).ForceStopAsync(options.Require("package")).ConfigureAwait(false),
            "clear" or "clear-app" => await RequireRunner(runner, command).ClearAppAsync(options.Require("package")).ConfigureAwait(false),
            "wait-for-activity" => await RequireRunner(runner, command).WaitForActivityAsync(options.Require("activity"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "wait-for-not-activity" => await RequireRunner(runner, command).WaitForNotActivityAsync(options.Require("activity"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "is-app-installed" => await RequireRunner(runner, command).IsAppInstalledAsync(options.Require("package")).ConfigureAwait(false),
            "lab" => await ExecuteLabAsync(options, RequireRunner(runner, command)).ConfigureAwait(false),
            "list-installed-packages" => await RequireRunner(runner, command).ListInstalledPackagesAsync(options.HasFlag("third-party")).ConfigureAwait(false),
            "grant-permission" => await RequireRunner(runner, command).GrantPermissionAsync(options.Require("package"), options.Require("permission")).ConfigureAwait(false),
            "revoke-permission" => await RequireRunner(runner, command).RevokePermissionAsync(options.Require("package"), options.Require("permission")).ConfigureAwait(false),
            "scenario-list" => await _scenarioCommandDispatcher.ListAsync(options).ConfigureAwait(false),
            "scenario-init" => await _scenarioCommandDispatcher.InitAsync(options).ConfigureAwait(false),
            "scenario-validate" => await _scenarioCommandDispatcher.ValidateAsync(options, artifacts).ConfigureAwait(false),
            "scenario-explain" => await _scenarioCommandDispatcher.ExplainAsync(options).ConfigureAwait(false),
            "journey-intake" => await _journeyIntakeValidationService.ValidateAsync(options).ConfigureAwait(false),
            "discover" => await _discoveryCommandService.RunAsync(options, RequireRunner(runner, command), artifacts).ConfigureAwait(false),
            "screen-state" => await RequireRunner(runner, command).GetScreenStateAsync().ConfigureAwait(false),
            "telemetry-tail" => await RequireRunner(runner, command).TelemetryTailAsync(options.Int("tail", CliDefaults.DefaultLogTail)).ConfigureAwait(false),
            "telemetry-watch" => await RequireRunner(runner, command).TelemetryWatchAsync(options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "wait-step" => await RequireRunner(runner, command).WaitForStepAsync(options.Require("step"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "wait-action-ready" => await RequireRunner(runner, command).WaitForActionReadyAsync(options.Require("action"), options.Get("step"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "tap" => await RequireRunner(runner, command).TapAsync(options.Require("x"), options.Require("y")).ConfigureAwait(false),
            "tap-text" => await RequireRunner(runner, command).TapTextAsync(options.Require("text"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "wait-visible" => await RequireRunner(runner, command).WaitVisibleAsync(options.Require("text"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "type-text" => await RequireRunner(runner, command).TypeTextAsync(options.Require("text")).ConfigureAwait(false),
            "keyevent" => await RequireRunner(runner, command).KeyEventAsync(options.Require("code")).ConfigureAwait(false),
            "logcat" => await RequireRunner(runner, command).LogcatAsync(options.Int("tail", CliDefaults.DefaultLogTail)).ConfigureAwait(false),
            "wait-log" => await RequireRunner(runner, command).WaitForLogAsync(options.Require("contains"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "record" => await RequireRunner(runner, command).RecordAsync(options.Require("output"), options.Int("time-limit-sec", CliDefaults.DefaultRecordTimeLimitSeconds)).ConfigureAwait(false),
            "run" => await _scenarioCommandDispatcher.RunAsync(options, runner, artifacts).ConfigureAwait(false),
            _ => throw new UsageException($"Unknown command '{command}'.")
        };
    }

    private async Task<WirelessMdnsConnectResult> ConnectWirelessAsync(CliOptions options, string adbExecutable, IWirelessDebugHost runner)
    {
        var result = await runner.ConnectWirelessAsync(GetWirelessEndpoint(options, "wireless-connect"), options.Get("service")).ConfigureAwait(false);
        var profileName = options.Get("save-profile");
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            await _profileCoordinator.SaveConnectedDeviceAsync(
                profileName,
                result.DeviceSelector,
                adbExecutable,
                options.Get("poll-artifacts")).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<object> ExecuteLabAsync(CliOptions options, IDeviceHost runner)
    {
        var action = options.Arguments.FirstOrDefault() ?? "status";
        var requirements = DeviceAdmissionRequirementsParser.Parse(
            options.Get("device-pool"),
            options.Get("require-capabilities"),
            "--device-pool",
            "--require-capabilities");
        return action.ToLowerInvariant() switch
        {
            "status" => await LabCommandResolver.ReadStatusAsync(
                runner,
                options.Get("device-query"),
                _labLeaseStore,
                _labQuarantineStore,
                _labDeviceInventoryStore,
                requirements,
                _resiliencePipelines.GetPipeline(LuotsiResiliencePipelines.LabProbePipelineName),
                includeProbes: true).ConfigureAwait(false),
            "doctor" => await LabCommandResolver.DiagnoseAsync(
                runner,
                options.Get("device-query"),
                options.HasFlag("fix"),
                _labLeaseStore,
                _labQuarantineStore,
                _labDeviceInventoryStore,
                requirements,
                _resiliencePipelines.GetPipeline(LuotsiResiliencePipelines.LabProbePipelineName)).ConfigureAwait(false),
            "plan" => await LabCommandResolver.PlanAsync(runner, options.Get("device-query"), _labLeaseStore, _labQuarantineStore, _labDeviceInventoryStore, requirements).ConfigureAwait(false),
            "claim" => await LabCommandResolver.ClaimAsync(
                runner,
                options.Get("device-query"),
                options.Get("owner"),
                options.Int("ttl-sec", 3600),
                ParseClaimWaitSeconds(options),
                _labLeaseClaimCoordinator,
                _labLeaseStore,
                _labQuarantineStore,
                _labDeviceInventoryStore,
                requirements).ConfigureAwait(false),
            "release" => await ReleaseLabLeaseAsync(options).ConfigureAwait(false),
            "extend" => await ExtendLabLeaseAsync(options).ConfigureAwait(false),
            "leases" => await _labLeaseStore.ListAsync().ConfigureAwait(false),
            "queue" => await _labLeaseStore.ListQueueAsync().ConfigureAwait(false),
            "quarantine" => await LabCommandResolver.QuarantineAsync(runner, options.Get("device-query"), options.Require("reason"), options.Get("owner"), _labQuarantineStore).ConfigureAwait(false),
            "unquarantine" => await _labQuarantineStore.ReleaseAsync(options.Require("serial")).ConfigureAwait(false),
            "quarantines" => await _labQuarantineStore.ListAsync().ConfigureAwait(false),
            "inventory" => await ExecuteLabInventoryAsync(options, runner).ConfigureAwait(false),
            _ => throw new UsageException("lab requires subcommand status, doctor, plan, claim, release, extend, leases, queue, quarantine, unquarantine, quarantines, or inventory.")
        };
    }

    private async Task<object> ExecuteLabInventoryAsync(CliOptions options, IDeviceHost runner)
    {
        var inventoryAction = options.Arguments.Skip(1).FirstOrDefault() ?? "list";
        return inventoryAction.ToLowerInvariant() switch
        {
            "list" => await _labDeviceInventoryStore.ListAsync(DeviceInventory.FromDeviceList(await runner.GetDevicesAsync().ConfigureAwait(false))).ConfigureAwait(false),
            "set" => await _labDeviceInventoryStore.SetAsync(
                await ResolveInventorySerialAsync(options, runner).ConfigureAwait(false),
                options.Get("pool"),
                options.Get("capabilities"),
                options.Get("owner")).ConfigureAwait(false),
            "clear" => await _labDeviceInventoryStore.ClearAsync(options.Require("serial")).ConfigureAwait(false),
            _ => throw new UsageException("lab inventory requires subcommand list, set, or clear.")
        };
    }

    private static async Task<string> ResolveInventorySerialAsync(CliOptions options, IDeviceHost runner)
    {
        var serial = options.Get("serial");
        var query = options.Get("device-query");
        if (!string.IsNullOrWhiteSpace(serial) && !string.IsNullOrWhiteSpace(query))
        {
            throw new UsageException("lab inventory set accepts either --serial or --device-query, not both.");
        }

        if (!string.IsNullOrWhiteSpace(serial))
        {
            return serial;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new UsageException("lab inventory set requires --serial or --device-query.");
        }

        var inventory = DeviceInventory.FromDeviceList(await runner.GetDevicesAsync().ConfigureAwait(false));
        var selected = DeviceQuerySelector.Select(inventory, query);
        if (string.IsNullOrWhiteSpace(selected.Serial))
        {
            throw new UsageException($"--device-query '{query}' selected a device without a serial.");
        }

        return selected.Serial;
    }

    private async Task<LabLeaseReleaseResult> ReleaseLabLeaseAsync(CliOptions options)
    {
        var leaseId = options.Get("lease");
        var serial = options.Get("serial");
        if (!string.IsNullOrWhiteSpace(leaseId) && !string.IsNullOrWhiteSpace(serial))
        {
            throw new UsageException("lab release accepts either --lease or --serial, not both.");
        }

        if (!string.IsNullOrWhiteSpace(serial))
        {
            return await _labLeaseStore.ReleaseSerialAsync(serial).ConfigureAwait(false);
        }

        return await _labLeaseStore.ReleaseAsync(options.Require("lease")).ConfigureAwait(false);
    }

    private async Task<LabLeaseExtendResult> ExtendLabLeaseAsync(CliOptions options)
    {
        var leaseId = options.Get("lease");
        var serial = options.Get("serial");
        if (!string.IsNullOrWhiteSpace(leaseId) && !string.IsNullOrWhiteSpace(serial))
        {
            throw new UsageException("lab extend accepts either --lease or --serial, not both.");
        }

        if (!string.IsNullOrWhiteSpace(serial))
        {
            return await _labLeaseStore.ExtendSerialAsync(serial, options.Int("ttl-sec", 3600)).ConfigureAwait(false);
        }

        return await _labLeaseStore.ExtendAsync(options.Require("lease"), options.Int("ttl-sec", 3600)).ConfigureAwait(false);
    }

    private static IWirelessDebugHost GetWirelessHost(IDeviceHost? runner, string command) =>
        RequireRunner(runner, command) as IWirelessDebugHost
        ?? throw new InvalidOperationException("The selected device host does not support wireless ADB commands.");

    private static int ParseClaimWaitSeconds(CliOptions options)
    {
        var waitSeconds = options.Int("claim-wait-sec", 0);
        if (waitSeconds < 0)
        {
            throw new UsageException("--claim-wait-sec must be zero or greater.");
        }

        return waitSeconds;
    }

    private static string? GetWirelessEndpoint(CliOptions options, string commandName)
    {
        var endpoint = options.Get("endpoint");
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint;
        }

        var host = options.Get("host");
        var port = options.Get("port");
        if (string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(port))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(port))
        {
            throw new UsageException($"{commandName} requires --endpoint <host:port>, or both --host <host> and --port <port>.");
        }

        return $"{host}:{port}";
    }

    private static IDeviceHost RequireRunner(IDeviceHost? runner, string command) =>
        runner ?? throw new InvalidOperationException($"Command '{command}' requires a device host.");

    private static IAdbCommandHost RequireAdbCommandHost(IDeviceHost? runner, string command) =>
        RequireRunner(runner, command) as IAdbCommandHost ?? throw new InvalidOperationException($"Command '{command}' requires a direct adb-backed device host.");
}
