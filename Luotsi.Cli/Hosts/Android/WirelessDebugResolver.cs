using System.Globalization;
using System.Text.RegularExpressions;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal static class WirelessDebugResolver
{
    private const string AdbLegacyServiceType = "_adb._tcp";
    private const string AdbTlsPairingServiceType = "_adb-tls-pairing._tcp";
    private const string AdbTlsConnectServiceType = "_adb-tls-connect._tcp";

    public static IReadOnlyList<WirelessMdnsService> ParseMdnsServices(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var services = new List<WirelessMdnsService>();
        foreach (var rawLine in output.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("List of discovered mdns services", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = Regex.Split(line, @"\s+").Where(static part => part.Length > 0).ToArray();
            if (parts.Length < 3)
            {
                continue;
            }

            var endpoint = parts[^1];
            if (!TryParseEndpoint(endpoint, out var host, out var port))
            {
                continue;
            }

            var serviceType = NormalizeServiceType(parts[^2]);
            var serviceName = string.Join(" ", parts.Take(parts.Length - 2)).Trim();
            if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(serviceType))
            {
                continue;
            }

            var normalizedEndpoint = FormatEndpoint(host, port);
            services.Add(new WirelessMdnsService(
                serviceName,
                serviceType,
                host,
                port,
                normalizedEndpoint,
                BuildServiceSelector(serviceName, serviceType),
                GetServiceKind(serviceType)));
        }

        return services;
    }

    public static WirelessScanResult CreateScanResult(IReadOnlyList<WirelessMdnsService> services) =>
        new(
            services,
            services.Where(static service => string.Equals(service.ServiceType, AdbTlsPairingServiceType, StringComparison.OrdinalIgnoreCase)).ToArray(),
            services.Where(static service => string.Equals(service.ServiceType, AdbTlsConnectServiceType, StringComparison.OrdinalIgnoreCase)).ToArray(),
            services.Where(static service => string.Equals(service.ServiceType, AdbLegacyServiceType, StringComparison.OrdinalIgnoreCase)).ToArray());

    public static ResolvedWirelessService ResolvePairingService(IReadOnlyList<WirelessMdnsService> pairingServices, string? endpoint, string? service)
    {
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(service))
        {
            throw new UsageException("wireless-pair accepts either --endpoint or --service, not both.");
        }

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            return new ResolvedWirelessService(NormalizeEndpoint(endpoint, "wireless-pair"), null, null, null);
        }

        if (!string.IsNullOrWhiteSpace(service))
        {
            return SelectWirelessServiceByName(pairingServices, service, "wireless-pair", AdbTlsPairingServiceType);
        }

        return SelectSingleWirelessService(pairingServices, "wireless-pair", AdbTlsPairingServiceType);
    }

    public static ResolvedWirelessService ResolveConnectService(WirelessScanResult scan, string? endpoint, string? service)
    {
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(service))
        {
            throw new UsageException("wireless-connect accepts either --endpoint or --service, not both.");
        }

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            return new ResolvedWirelessService(NormalizeEndpoint(endpoint, "wireless-connect"), null, null, null);
        }

        if (!string.IsNullOrWhiteSpace(service))
        {
            var connectableServices = scan.Services
                .Where(static item =>
                    string.Equals(item.ServiceType, AdbTlsConnectServiceType, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.ServiceType, AdbLegacyServiceType, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return SelectWirelessServiceByName(connectableServices, service, "wireless-connect", $"{AdbTlsConnectServiceType} or {AdbLegacyServiceType}");
        }

        return SelectSingleWirelessService(scan.ConnectServices, "wireless-connect", AdbTlsConnectServiceType);
    }

    private static ResolvedWirelessService SelectWirelessServiceByName(
        IReadOnlyList<WirelessMdnsService> services,
        string service,
        string commandName,
        string expectedServiceType)
    {
        var matches = services.Where(item => ServiceMatches(item, service)).ToArray();
        if (matches.Length == 0)
        {
            throw new UsageException($"{commandName} could not find service '{service}' among discovered {expectedServiceType} services. Run wireless-scan to inspect current mDNS services, or pass --endpoint <host:port>.");
        }

        if (matches.Length > 1)
        {
            throw new UsageException($"{commandName} found multiple services matching '{service}': {DescribeServices(matches)}. Pass --endpoint <host:port>.");
        }

        return ResolvedWirelessService.From(matches[0]);
    }

    private static ResolvedWirelessService SelectSingleWirelessService(
        IReadOnlyList<WirelessMdnsService> services,
        string commandName,
        string expectedServiceType)
    {
        if (services.Count == 0)
        {
            throw new UsageException($"{commandName} found no discovered {expectedServiceType} services. Run wireless-scan, enable Wireless debugging on the device, or pass --endpoint <host:port>.");
        }

        if (services.Count > 1)
        {
            throw new UsageException($"{commandName} found multiple discovered {expectedServiceType} services: {DescribeServices(services)}. Pass --service <service-name> or --endpoint <host:port>.");
        }

        return ResolvedWirelessService.From(services[0]);
    }

    private static string NormalizeEndpoint(string endpoint, string commandName)
    {
        var trimmed = RequireNonBlank(endpoint, $"{commandName} requires a non-empty endpoint.");
        if (!TryParseEndpoint(trimmed, out var host, out var port))
        {
            throw new UsageException($"{commandName} requires --endpoint in <host>:<port> form.");
        }

        return FormatEndpoint(host, port);
    }

    private static bool TryParseEndpoint(string endpoint, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        var trimmed = endpoint.Trim();
        var separator = trimmed.LastIndexOf(':');
        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            return false;
        }

        var parsedHost = trimmed[..separator].Trim();
        var portText = trimmed[(separator + 1)..].Trim();
        if (parsedHost.Length == 0 ||
            !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort) ||
            parsedPort <= 0 ||
            parsedPort > 65535)
        {
            return false;
        }

        host = parsedHost;
        port = parsedPort;
        return true;
    }

    private static string FormatEndpoint(string host, int port) =>
        $"{host}:{port.ToString(CultureInfo.InvariantCulture)}";

    private static string NormalizeServiceType(string serviceType)
    {
        var normalized = serviceType.Trim();
        while (normalized.EndsWith(".", StringComparison.Ordinal))
        {
            normalized = normalized[..^1];
        }

        if (normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^6].TrimEnd('.');
        }

        return normalized;
    }

    private static string BuildServiceSelector(string serviceName, string serviceType) =>
        $"{serviceName}.{NormalizeServiceType(serviceType)}";

    private static string NormalizeServiceLookup(string service)
    {
        var normalized = service.Trim();
        while (normalized.EndsWith(".", StringComparison.Ordinal))
        {
            normalized = normalized[..^1];
        }

        if (normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^6].TrimEnd('.');
        }

        return normalized;
    }

    private static bool ServiceMatches(WirelessMdnsService service, string lookup)
    {
        var normalizedLookup = NormalizeServiceLookup(lookup);
        return string.Equals(service.ServiceName, normalizedLookup, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(service.Selector, normalizedLookup, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(service.Endpoint, normalizedLookup, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetServiceKind(string serviceType) =>
        NormalizeServiceType(serviceType).ToLowerInvariant() switch
        {
            AdbTlsPairingServiceType => "pairing",
            AdbTlsConnectServiceType => "connect",
            AdbLegacyServiceType => "legacy",
            _ => "other"
        };

    private static string DescribeServices(IEnumerable<WirelessMdnsService> services) =>
        string.Join(", ", services.Select(static service => $"{service.ServiceName} ({service.ServiceType} {service.Endpoint})"));

    private static string RequireNonBlank(string value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new UsageException(message) : value.Trim();
}

internal sealed record ResolvedWirelessService(string Endpoint, string? ServiceName, string? ServiceType, string? Selector)
{
    public static ResolvedWirelessService From(WirelessMdnsService service) =>
        new(service.Endpoint, service.ServiceName, service.ServiceType, service.Selector);
}