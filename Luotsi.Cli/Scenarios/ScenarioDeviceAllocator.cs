using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal interface IScenarioDeviceAllocator
{
    Task<ScenarioDeviceAllocation> AllocateAsync(IDeviceHost runner, ScenarioRunConfiguration configuration);
}

internal sealed class ScenarioDeviceAllocator : IScenarioDeviceAllocator
{
    public async Task<ScenarioDeviceAllocation> AllocateAsync(IDeviceHost runner, ScenarioRunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(configuration);

        PreflightResult? readiness = null;
        if (runner is IAdbCommandHost adb)
        {
            if (configuration.RequireDeviceReady)
            {
                await adb.WaitForDeviceAsync(configuration.DeviceWaitTimeoutSec).ConfigureAwait(false);
            }

            readiness = await adb.ReadPreflightAsync(configuration.DeviceReadinessPackage).ConfigureAwait(false);
        }

        var inventory = readiness is null
            ? DeviceInventory.FromDeviceList(await runner.GetDevicesAsync().ConfigureAwait(false))
            : await TryReadInventoryAsync(runner).ConfigureAwait(false);
        var serial = readiness?.Serial ?? (inventory is null ? null : GetSingleDeviceSerial(inventory));
        var device = string.IsNullOrWhiteSpace(serial)
            ? null
            : inventory?.Devices.FirstOrDefault(candidate => string.Equals(candidate.Serial, serial, StringComparison.OrdinalIgnoreCase));
        return new ScenarioDeviceAllocation(
            "allocated",
            serial,
            device,
            readiness,
            configuration.RequireDeviceReady,
            configuration.DeviceWaitTimeoutSec,
            configuration.DeviceReadinessPackage,
            configuration.LabLease);
    }

    private static async Task<DeviceInventoryResult?> TryReadInventoryAsync(IDeviceHost runner)
    {
        try
        {
            return DeviceInventory.FromDeviceList(await runner.GetDevicesAsync().ConfigureAwait(false));
        }
        catch (Exception ex) when (!IsFatalException(ex))
        {
            return null;
        }
    }

    private static string? GetSingleDeviceSerial(DeviceInventoryResult inventory) =>
        inventory.Devices.Count == 1 ? inventory.Devices[0].Serial : null;

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}
