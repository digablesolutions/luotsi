using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Devices;

namespace Luotsi.Cli.Cli.Routing;

internal static class DeviceSelectorResolver
{
    public static async Task<string?> ResolveAsync(CliOptions options, string adbExecutable, ArtifactSession artifacts, string? command, DeviceHostLauncher deviceHostLauncher)
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
        var selected = DeviceQuerySelector.Select(inventory, query);
        if (string.IsNullOrWhiteSpace(selected.Serial))
        {
            throw new UsageException($"--device-query '{query}' selected a device without a serial.");
        }

        return selected.Serial;
    }
}
