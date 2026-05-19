using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal static class DeviceStatusResolver
{
    public static async Task<DeviceStatusResult> ReadAsync(IDeviceHost runner, IAdbCommandHost adbCommandHost)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(adbCommandHost);

        var readiness = await adbCommandHost.ReadPreflightAsync(null).ConfigureAwait(false);
        var inventory = DeviceInventory.FromDeviceList(await runner.GetDevicesAsync().ConfigureAwait(false));
        var matchedDevice = inventory.Devices.FirstOrDefault(device => string.Equals(device.Serial, readiness.Serial, StringComparison.OrdinalIgnoreCase));

        if (matchedDevice is null)
        {
            throw new DeviceInventorySelectionException(readiness.Serial);
        }

        return new DeviceStatusResult(matchedDevice, readiness);
    }
}