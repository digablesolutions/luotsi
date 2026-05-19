using Luotsi.Cli.Models;

namespace Luotsi.Cli.Infrastructure.Devices;

internal static class DeviceInventory
{
    public static DeviceInventoryResult FromDeviceList(DeviceListResult list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new DeviceInventoryResult(list.Devices.Select(ToState).ToArray());
    }

    public static DeviceState ToState(DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var details = ParseDetails(device.Details);
        var serial = string.IsNullOrWhiteSpace(device.Serial) ? null : device.Serial;
        var state = string.IsNullOrWhiteSpace(device.Status) ? "unknown" : device.Status.Trim();
        var transport = GetTransport(serial, details);
        var type = GetType(serial, details);
        var availability = string.Equals(state, "device", StringComparison.OrdinalIgnoreCase)
            ? "available"
            : "unavailable";

        return new DeviceState(
            serial,
            NormalizeState(state),
            transport,
            type,
            GetDetail(details, "model"),
            GetDetail(details, "product"),
            GetDetail(details, "device"),
            device.Details,
            availability,
            GetRecommendedFix(state));
    }

    private static IReadOnlyDictionary<string, string> ParseDetails(string details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return details
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Split(':', 2))
            .Where(static pair => pair.Length == 2 && !string.IsNullOrWhiteSpace(pair[0]))
            .GroupBy(static pair => pair[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First()[1], StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetDetail(IReadOnlyDictionary<string, string> details, string key) =>
        details.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string GetTransport(string? serial, IReadOnlyDictionary<string, string> details)
    {
        if (!string.IsNullOrWhiteSpace(serial) && serial.Contains(':', StringComparison.Ordinal))
        {
            return "wifi";
        }

        if (!string.IsNullOrWhiteSpace(serial) && serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase))
        {
            return "emulator";
        }

        if (details.ContainsKey("usb"))
        {
            return "usb";
        }

        return "unknown";
    }

    private static string GetType(string? serial, IReadOnlyDictionary<string, string> details)
    {
        if (!string.IsNullOrWhiteSpace(serial) && serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase))
        {
            return "emulator";
        }

        var model = GetDetail(details, "model");
        if (!string.IsNullOrWhiteSpace(model) && model.Contains("emulator", StringComparison.OrdinalIgnoreCase))
        {
            return "emulator";
        }

        return "physical";
    }

    private static string NormalizeState(string state) =>
        string.Equals(state, "device", StringComparison.OrdinalIgnoreCase) ? "online" : state.ToLowerInvariant();

    private static string? GetRecommendedFix(string state) =>
        state.ToLowerInvariant() switch
        {
            "offline" => "Run `adb reconnect offline`, reconnect USB, or reconnect wireless ADB.",
            "unauthorized" => "Authorize the device debugging prompt, then rerun the command.",
            "no permissions" => "Fix host USB permissions for adb access.",
            _ => null
        };
}
