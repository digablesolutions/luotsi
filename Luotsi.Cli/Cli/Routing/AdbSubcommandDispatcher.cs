using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class AdbSubcommandDispatcher
{
    public static async Task<object> ExecuteAsync(CliOptions options, IAdbCommandHost runner)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);

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
