using System.Globalization;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed class AndroidWirelessDebugOperations(IAdbClient adb)
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));

    public async Task<WirelessConnectResult> EnableWirelessAsync(string? host, int port)
    {
        if (port is <= 0 or > 65535)
        {
            throw new UsageException("wireless requires --port between 1 and 65535.");
        }

        var validatedHost = string.IsNullOrWhiteSpace(host)
            ? await DetectWirelessHostAsync().ConfigureAwait(false)
            : host.Trim();
        var tcpip = await _adb.RunAsync(["tcpip", port.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        tcpip.EnsureSuccess("adb tcpip failed");
        var endpoint = $"{validatedHost}:{port}";
        var connect = await _adb.RunAsync(["connect", endpoint]).ConfigureAwait(false);
        connect.EnsureSuccess("adb connect failed");
        return new WirelessConnectResult(validatedHost, port, endpoint);
    }

    public async Task<WirelessScanResult> ScanWirelessServicesAsync()
    {
        var result = await _adb.RunAsync(["mdns", "services"]).ConfigureAwait(false);
        result.EnsureSuccess("adb mdns services failed");
        return WirelessDebugResolver.CreateScanResult(WirelessDebugResolver.ParseMdnsServices(result.Stdout));
    }

    public async Task<WirelessPairResult> PairWirelessAsync(string? endpoint, string? service, string? pairingCode)
    {
        var target = await ResolvePairingServiceAsync(endpoint, service).ConfigureAwait(false);
        var normalizedCode = string.IsNullOrWhiteSpace(pairingCode) ? null : pairingCode.Trim();
        if (normalizedCode is null)
        {
            return new WirelessPairResult(
                target.Endpoint,
                target.ServiceName,
                target.ServiceType,
                target.Selector,
                Paired: false,
                InteractiveRequired: true,
                $"Luotsi cannot drive adb's interactive pairing prompt while preserving one JSON command envelope. Pass --code <pairing-code>, or run `adb pair {target.Endpoint}` manually.",
                Stdout: null);
        }

        var result = await _adb.RunAsync(["pair", target.Endpoint, normalizedCode]).ConfigureAwait(false);
        result.EnsureSuccess("adb pair failed");
        var stdout = result.Stdout.Trim();
        return new WirelessPairResult(
            target.Endpoint,
            target.ServiceName,
            target.ServiceType,
            target.Selector,
            Paired: true,
            InteractiveRequired: false,
            string.IsNullOrWhiteSpace(stdout) ? $"Paired to {target.Endpoint}." : stdout,
            string.IsNullOrWhiteSpace(stdout) ? null : stdout);
    }

    public async Task<WirelessMdnsConnectResult> ConnectWirelessAsync(string? endpoint, string? service)
    {
        var target = await ResolveConnectServiceAsync(endpoint, service).ConfigureAwait(false);
        var connectTarget = target.Endpoint;
        var result = await _adb.RunAsync(["connect", connectTarget]).ConfigureAwait(false);
        result.EnsureSuccess("adb connect failed");
        var stdout = result.Stdout.Trim();
        return new WirelessMdnsConnectResult(
            target.Endpoint,
            target.ServiceName,
            target.ServiceType,
            target.Selector,
            connectTarget,
            target.Selector ?? target.Endpoint,
            Connected: true,
            string.IsNullOrWhiteSpace(stdout) ? $"Connected to {connectTarget}." : stdout,
            string.IsNullOrWhiteSpace(stdout) ? null : stdout);
    }

    private async Task<ResolvedWirelessService> ResolvePairingServiceAsync(string? endpoint, string? service)
    {
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            return WirelessDebugResolver.ResolvePairingService([], endpoint, service);
        }

        var scan = await ScanWirelessServicesAsync().ConfigureAwait(false);
        return WirelessDebugResolver.ResolvePairingService(scan.PairingServices, endpoint, service);
    }

    private async Task<ResolvedWirelessService> ResolveConnectServiceAsync(string? endpoint, string? service)
    {
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            return WirelessDebugResolver.ResolveConnectService(new WirelessScanResult([], [], [], []), endpoint, service);
        }

        var scan = await ScanWirelessServicesAsync().ConfigureAwait(false);
        return WirelessDebugResolver.ResolveConnectService(scan, endpoint, service);
    }

    private async Task<string> DetectWirelessHostAsync()
    {
        var route = await _adb.ShellAsync("ip route get 8.8.8.8").ConfigureAwait(false);
        route.EnsureSuccess("wireless host auto-detection failed");
        var sourceAddress = ParseRouteSourceAddress(route.Stdout);
        if (string.IsNullOrWhiteSpace(sourceAddress))
        {
            throw new UsageException("wireless could not auto-detect the device Wi-Fi IP address. Pass --host <ip-or-host>.");
        }

        return sourceAddress;
    }

    internal static string? ParseRouteSourceAddress(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var tokens = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            if (string.Equals(tokens[index], "src", StringComparison.OrdinalIgnoreCase))
            {
                return tokens[index + 1];
            }
        }

        return null;
    }
}