using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal static class DeviceStatusResolver
{
    public static async Task<DeviceStatusResult> ReadAsync(IDeviceHost runner, IAdbCommandHost adbCommandHost, string? selectedSerial)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(adbCommandHost);

        var readiness = await adbCommandHost.ReadPreflightAsync(null).ConfigureAwait(false);
        var inventory = DeviceInventory.FromDeviceList(await runner.GetDevicesAsync().ConfigureAwait(false));
        var matchedDevice = FindSelectedDevice(inventory, selectedSerial)
            ?? FindSelectedDevice(inventory, readiness.Serial);

        if (matchedDevice is null)
        {
            throw new DeviceInventorySelectionException(selectedSerial ?? readiness.Serial);
        }

        return new DeviceStatusResult(matchedDevice, readiness);
    }

    private static DeviceState? FindSelectedDevice(DeviceInventoryResult inventory, string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        return inventory.Devices.FirstOrDefault(device => string.Equals(device.Serial, serial, StringComparison.OrdinalIgnoreCase));
    }
}
