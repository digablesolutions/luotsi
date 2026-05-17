using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli;

internal sealed class AppCommandDispatcher(
    AdbSubcommandDispatcher adbSubcommandDispatcher,
    ScenarioCommandDispatcher scenarioCommandDispatcher,
    ViewProfileCoordinator profileCoordinator)
{
    private readonly AdbSubcommandDispatcher _adbSubcommandDispatcher = adbSubcommandDispatcher ?? throw new ArgumentNullException(nameof(adbSubcommandDispatcher));
    private readonly ScenarioCommandDispatcher _scenarioCommandDispatcher = scenarioCommandDispatcher ?? throw new ArgumentNullException(nameof(scenarioCommandDispatcher));
    private readonly ViewProfileCoordinator _profileCoordinator = profileCoordinator ?? throw new ArgumentNullException(nameof(profileCoordinator));

    public async Task<object> ExecuteAsync(string command, CliOptions options, string adbExecutable, IDeviceHost runner)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);

        return command switch
        {
            "adb" => await AdbSubcommandDispatcher.ExecuteAsync(options, RequireAdbCommandHost(runner, command)).ConfigureAwait(false),
            "devices" => await runner.GetDevicesAsync().ConfigureAwait(false),
            "device-wait" or "wait-for-device" => await RequireAdbCommandHost(runner, command).WaitForDeviceAsync(options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "preflight" => await RequireAdbCommandHost(runner, command).PreflightAsync(options.Get("package")).ConfigureAwait(false),
            "wireless" => await GetWirelessHost(runner).EnableWirelessAsync(options.Get("host"), options.Int("port", 5555)).ConfigureAwait(false),
            "wireless-scan" => await GetWirelessHost(runner).ScanWirelessServicesAsync().ConfigureAwait(false),
            "wireless-pair" => await GetWirelessHost(runner).PairWirelessAsync(GetWirelessEndpoint(options, "wireless-pair"), options.Get("service"), options.Get("code") ?? options.Get("pairing-code")).ConfigureAwait(false),
            "wireless-connect" => await ConnectWirelessAsync(options, adbExecutable, GetWirelessHost(runner)).ConfigureAwait(false),
            "forward-list" => await runner.ListForwardsAsync().ConfigureAwait(false),
            "forward" => await runner.ForwardAsync(options.Require("local"), options.Require("remote"), options.HasFlag("no-rebind")).ConfigureAwait(false),
            "forward-remove" => await runner.RemoveForwardAsync(options.Require("local")).ConfigureAwait(false),
            "reverse-list" => await runner.ListReversesAsync().ConfigureAwait(false),
            "reverse" => await runner.ReverseAsync(options.Require("remote"), options.Require("local"), options.HasFlag("no-rebind")).ConfigureAwait(false),
            "reverse-remove" => await runner.RemoveReverseAsync(options.Require("remote")).ConfigureAwait(false),
            "start-app" => await runner.StartAppAsync(options.Require("package"), options.Get("activity"), options.HasFlag("wait")).ConfigureAwait(false),
            "start-uri" => await runner.StartUriAsync(options.Require("uri"), options.Get("package"), options.Get("activity"), options.Get("action"), options.HasFlag("wait")).ConfigureAwait(false),
            "force-stop" => await runner.ForceStopAsync(options.Require("package")).ConfigureAwait(false),
            "clear" or "clear-app" => await runner.ClearAppAsync(options.Require("package")).ConfigureAwait(false),
            "wait-for-activity" => await runner.WaitForActivityAsync(options.Require("activity"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "wait-for-not-activity" => await runner.WaitForNotActivityAsync(options.Require("activity"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "is-app-installed" => await runner.IsAppInstalledAsync(options.Require("package")).ConfigureAwait(false),
            "list-installed-packages" => await runner.ListInstalledPackagesAsync(options.HasFlag("third-party")).ConfigureAwait(false),
            "grant-permission" => await runner.GrantPermissionAsync(options.Require("package"), options.Require("permission")).ConfigureAwait(false),
            "revoke-permission" => await runner.RevokePermissionAsync(options.Require("package"), options.Require("permission")).ConfigureAwait(false),
            "scenario-list" => await _scenarioCommandDispatcher.ListAsync(options).ConfigureAwait(false),
            "screen-state" => await runner.GetScreenStateAsync().ConfigureAwait(false),
            "telemetry-tail" => await runner.TelemetryTailAsync(options.Int("tail", CliDefaults.DefaultLogTail)).ConfigureAwait(false),
            "telemetry-watch" => await runner.TelemetryWatchAsync(options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "wait-step" => await runner.WaitForStepAsync(options.Require("step"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "wait-action-ready" => await runner.WaitForActionReadyAsync(options.Require("action"), options.Get("step"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "tap" => await runner.TapAsync(options.Require("x"), options.Require("y")).ConfigureAwait(false),
            "tap-text" => await runner.TapTextAsync(options.Require("text"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "wait-visible" => await runner.WaitVisibleAsync(options.Require("text"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "type-text" => await runner.TypeTextAsync(options.Require("text")).ConfigureAwait(false),
            "keyevent" => await runner.KeyEventAsync(options.Require("code")).ConfigureAwait(false),
            "logcat" => await runner.LogcatAsync(options.Int("tail", CliDefaults.DefaultLogTail)).ConfigureAwait(false),
            "wait-log" => await runner.WaitForLogAsync(options.Require("contains"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "record" => await runner.RecordAsync(options.Require("output"), options.Int("time-limit-sec", CliDefaults.DefaultRecordTimeLimitSeconds)).ConfigureAwait(false),
            "run" => await _scenarioCommandDispatcher.RunAsync(options, runner).ConfigureAwait(false),
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

    private static IWirelessDebugHost GetWirelessHost(IDeviceHost runner) =>
        runner as IWirelessDebugHost
        ?? throw new InvalidOperationException("The selected device host does not support wireless ADB commands.");

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

    private static IAdbCommandHost RequireAdbCommandHost(IDeviceHost runner, string command) =>
        runner as IAdbCommandHost ?? throw new InvalidOperationException($"Command '{command}' requires a direct adb-backed device host.");
}
