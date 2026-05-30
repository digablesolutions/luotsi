using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;
using Luotsi.Cli.Cli.Routing;

namespace Luotsi.Cli.Scenarios;

internal interface IScenarioDeviceAllocator
{
    Task<ScenarioDeviceAllocation> AllocateAsync(IDeviceHost runner, ScenarioRunConfiguration configuration);
}

internal sealed class ScenarioDeviceAllocator(LabDeviceInventoryStore? inventoryStore = null) : IScenarioDeviceAllocator
{
    private readonly LabDeviceInventoryStore? _inventoryStore = inventoryStore;

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
        var registered = string.IsNullOrWhiteSpace(serial) ? null : _inventoryStore?.TryGetBySerial(serial);
        var capabilities = BuildCapabilities(device, registered);
        var requirements = configuration.DeviceRequirements;
        if (DeviceAdmissionRequirementsParser.HasRequirements(requirements) && string.IsNullOrWhiteSpace(serial))
        {
            throw new UsageException(
                "Device admission requirements require a single selected device. " +
                "Use --device, --device-query, or attach only one device before retrying.");
        }

        var requirementMismatch = GetRequirementMismatchReason(requirements, device, registered, capabilities);
        if (requirementMismatch is not null)
        {
            throw new UsageException(
                $"Selected device '{serial ?? "<unknown>"}' does not satisfy the requested admission requirements: {requirementMismatch}. " +
                $"Run `luotsi lab inventory` or `{BuildInventoryCommand(serial, requirements)}` before retrying.");
        }

        return new ScenarioDeviceAllocation(
            "allocated",
            serial,
            device,
            readiness,
            configuration.RequireDeviceReady,
            configuration.DeviceWaitTimeoutSec,
            configuration.DeviceReadinessPackage,
            configuration.LabLease,
            registered?.Pool,
            capabilities,
            registered?.Registered ?? false,
            requirements);
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

    private static IReadOnlyList<string> BuildCapabilities(DeviceState? device, LabInventoryDeviceResult? registered)
    {
        var capabilities = new List<string>();
        if (device is not null)
        {
            if (string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase))
            {
                capabilities.Add("adb");
            }

            if (!string.IsNullOrWhiteSpace(device.Transport))
            {
                capabilities.Add(device.Transport);
            }

            if (!string.IsNullOrWhiteSpace(device.Type))
            {
                capabilities.Add(device.Type);
            }

            if (!string.IsNullOrWhiteSpace(device.Model))
            {
                capabilities.Add($"model:{device.Model}");
            }
        }

        if (registered?.Capabilities is not null)
        {
            capabilities.AddRange(registered.Capabilities);
        }

        return capabilities
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static capability => capability, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? GetRequirementMismatchReason(
        DeviceAdmissionRequirements? requirements,
        DeviceState? device,
        LabInventoryDeviceResult? registered,
        IReadOnlyList<string> capabilities)
    {
        if (!DeviceAdmissionRequirementsParser.HasRequirements(requirements))
        {
            return null;
        }

        var required = requirements!;
        if (!string.IsNullOrWhiteSpace(required.Pool))
        {
            if (registered is null || !registered.Registered)
            {
                return $"requires pool '{required.Pool}' but the device is not registered in lab inventory";
            }

            if (string.IsNullOrWhiteSpace(registered.Pool))
            {
                return $"requires pool '{required.Pool}' but the device has no registered pool";
            }

            if (!string.Equals(registered.Pool, required.Pool, StringComparison.OrdinalIgnoreCase))
            {
                return $"requires pool '{required.Pool}' but inventory pool is '{registered.Pool}'";
            }
        }

        var requiredCapabilities = required.Capabilities ?? [];
        var missingCapabilities = requiredCapabilities
            .Where(required => capabilities.Contains(required, StringComparer.OrdinalIgnoreCase) is false)
            .ToArray();
        if (missingCapabilities.Length == 0)
        {
            return null;
        }

        var advertised = capabilities.Count == 0 ? "none" : string.Join(", ", capabilities);
        return $"requires capabilities [{string.Join(", ", missingCapabilities)}] but the device advertises [{advertised}]";
    }

    private static string BuildInventoryCommand(string? serial, DeviceAdmissionRequirements? requirements)
    {
        var command = "luotsi lab inventory set --serial " + Quote(string.IsNullOrWhiteSpace(serial) ? "<adb serial>" : serial);
        if (!string.IsNullOrWhiteSpace(requirements?.Pool))
        {
            command += " --pool " + Quote(requirements.Pool);
        }

        var capabilities = DeviceAdmissionRequirementsParser.FormatCapabilities(requirements?.Capabilities);
        if (!string.IsNullOrWhiteSpace(capabilities))
        {
            command += " --capabilities " + Quote(capabilities);
        }

        return command;
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}
