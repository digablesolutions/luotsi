using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli;

internal sealed class AppCommandDispatcher(
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    IDelay delay,
    IEnvironmentVariables environment,
    ViewProfileCoordinator profileCoordinator)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly ViewProfileCoordinator _profileCoordinator = profileCoordinator ?? throw new ArgumentNullException(nameof(profileCoordinator));

    public async Task<object> ExecuteAsync(string command, CliOptions options, string adbExecutable, IDeviceHost runner)
    {
        var scenarios = new ScenarioExecutor(runner, _fileSystem, _timeProvider, _delay, _environment);

        return command switch
        {
            "adb" => await ExecuteAdbCommandAsync(options, RequireAdbCommandHost(runner, command)).ConfigureAwait(false),
            "devices" => await runner.GetDevicesAsync().ConfigureAwait(false),
            "device-wait" or "wait-for-device" => await RequireAdbCommandHost(runner, command).WaitForDeviceAsync(options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "preflight" => await RequireAdbCommandHost(runner, command).PreflightAsync(options.Get("package")).ConfigureAwait(false),
            "wireless" => await GetWirelessHost(runner).EnableWirelessAsync(options.Get("host"), options.Int("port", 5555)).ConfigureAwait(false),
            "wireless-scan" => await GetWirelessHost(runner).ScanWirelessServicesAsync().ConfigureAwait(false),
            "wireless-pair" => await GetWirelessHost(runner).PairWirelessAsync(GetWirelessEndpoint(options, "wireless-pair"), options.Get("service"), options.Get("code") ?? options.Get("pairing-code")).ConfigureAwait(false),
            "wireless-connect" => await ConnectWirelessAsync(options, adbExecutable, GetWirelessHost(runner)).ConfigureAwait(false),
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
            "run" => await scenarios.RunAsync(options.Require("file")).ConfigureAwait(false),
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

    private static async Task<object> ExecuteAdbCommandAsync(CliOptions options, IAdbCommandHost runner)
    {
        var args = options.Arguments;
        if (args.Count == 0)
        {
            throw new UsageException("Missing adb subcommand. Supported forms: adb server-status, adb version, adb features, adb mdns check, adb wait-for-device, adb reconnect [offline|device].");
        }

        return args[0] switch
        {
            "server-status" when args.Count == 1 => await runner.GetAdbServerStatusAsync().ConfigureAwait(false),
            "version" when args.Count == 1 => await runner.GetAdbVersionAsync().ConfigureAwait(false),
            "features" when args.Count == 1 => await runner.GetAdbFeaturesAsync().ConfigureAwait(false),
            "mdns" when args.Count == 2 && string.Equals(args[1], "check", StringComparison.OrdinalIgnoreCase) => await runner.CheckAdbMdnsAsync().ConfigureAwait(false),
            "wait-for-device" when args.Count == 1 => await runner.WaitForDeviceAsync(options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "device-wait" when args.Count == 1 => await runner.WaitForDeviceAsync(options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
            "reconnect" when args.Count <= 2 => await runner.ReconnectAdbAsync(args.Count > 1 ? args[1] : options.Get("target") ?? "offline").ConfigureAwait(false),
            _ => throw new UsageException($"Unknown adb subcommand '{string.Join(" ", args)}'.")
        };
    }
}
