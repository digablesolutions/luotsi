using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal static class DeviceSelectorResolver
{
    public static async Task<string?> ResolveAsync(
        CliOptions options,
        string adbExecutable,
        ArtifactSession artifacts,
        string? command,
        DeviceHostLauncher deviceHostLauncher,
        LabLeaseStore? leaseStore = null,
        LabQuarantineStore? quarantineStore = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(deviceHostLauncher);

        var query = options.Get("device-query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        if (string.Equals(command, "lab", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(command, "devices", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("--device-query selects one target device and is not supported with `devices`. Use `device-status --device-query <query>` for a single-device status.");
        }

        if (!string.IsNullOrWhiteSpace(options.Get("device")))
        {
            throw new UsageException("Use either --device or --device-query, not both.");
        }

        var inventoryHost = deviceHostLauncher.Create(options, adbExecutable, artifacts, deviceSelector: null);
        var inventory = DeviceInventory.FromDeviceList(await inventoryHost.GetDevicesAsync().ConfigureAwait(false));
        var leases = leaseStore?.ReadActiveLeasesBySerial() ?? new Dictionary<string, LabLeaseResult>(StringComparer.OrdinalIgnoreCase);
        var quarantines = quarantineStore?.ReadBySerial() ?? new Dictionary<string, LabQuarantineResult>(StringComparer.OrdinalIgnoreCase);
        if (leases.Count > 0 || quarantines.Count > 0)
        {
            ThrowIfOnlyLeasedDevicesMatch(inventory, query, leases);
            ThrowIfOnlyQuarantinedDevicesMatch(inventory, query, quarantines);
            inventory = ApplyLabExclusions(inventory, leases, quarantines);
        }

        var selected = DeviceQuerySelector.Select(inventory, query);
        if (string.IsNullOrWhiteSpace(selected.Serial))
        {
            throw new UsageException($"--device-query '{query}' selected a device without a serial.");
        }

        return selected.Serial;
    }

    private static DeviceInventoryResult ApplyLabExclusions(
        DeviceInventoryResult inventory,
        IReadOnlyDictionary<string, LabLeaseResult> leases,
        IReadOnlyDictionary<string, LabQuarantineResult> quarantines)
    {
        if (leases.Count == 0 && quarantines.Count == 0)
        {
            return inventory;
        }

        return new DeviceInventoryResult(inventory.Devices
            .Where(device => device.Serial is null || !leases.ContainsKey(device.Serial) && !quarantines.ContainsKey(device.Serial))
            .ToArray());
    }

    private static void ThrowIfOnlyLeasedDevicesMatch(DeviceInventoryResult inventory, string query, IReadOnlyDictionary<string, LabLeaseResult> leases)
    {
        var parsed = new DeviceQuery(query);
        var matches = inventory.Devices.Where(parsed.Matches).ToArray();
        if (matches.Length == 0)
        {
            return;
        }

        var leasedMatches = matches
            .Where(device => device.Serial is not null && leases.ContainsKey(device.Serial))
            .ToArray();
        if (leasedMatches.Length != matches.Length)
        {
            return;
        }

        var details = leasedMatches
            .Select(device =>
            {
                var lease = leases[device.Serial!];
                return $"{device.Serial} leased by {lease.Owner} until {lease.ExpiresAt:O}";
            });
        throw new UsageException($"--device-query '{query}' matched only leased devices: {string.Join(", ", details)}. Run `luotsi lab leases` or release a lease before selecting this device.");
    }

    private static void ThrowIfOnlyQuarantinedDevicesMatch(DeviceInventoryResult inventory, string query, IReadOnlyDictionary<string, LabQuarantineResult> quarantines)
    {
        if (quarantines.Count == 0)
        {
            return;
        }

        var parsed = new DeviceQuery(query);
        var matches = inventory.Devices.Where(parsed.Matches).ToArray();
        if (matches.Length == 0)
        {
            return;
        }

        var quarantinedMatches = matches
            .Where(device => device.Serial is not null && quarantines.ContainsKey(device.Serial))
            .ToArray();
        if (quarantinedMatches.Length != matches.Length)
        {
            return;
        }

        var details = quarantinedMatches
            .Select(device =>
            {
                var quarantine = quarantines[device.Serial!];
                return $"{device.Serial} quarantined by {quarantine.Owner} at {quarantine.QuarantinedAt:O}: {quarantine.Reason}";
            });
        throw new UsageException($"--device-query '{query}' matched only quarantined devices: {string.Join(", ", details)}. Run `luotsi lab quarantines` or unquarantine a healthy device before selecting it.");
    }
}
