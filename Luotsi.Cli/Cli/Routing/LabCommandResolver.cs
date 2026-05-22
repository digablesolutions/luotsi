using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal static class LabCommandResolver
{
    public static async Task<LabStatusResult> ReadStatusAsync(IDeviceHost runner, string? query)
    {
        ArgumentNullException.ThrowIfNull(runner);

        var inventory = DeviceInventory.FromDeviceList(await runner.GetDevicesAsync().ConfigureAwait(false));
        var selector = string.IsNullOrWhiteSpace(query) ? null : new DeviceQuery(query);
        var decisions = inventory.Devices
            .Select(device => ToDecision(device, selector))
            .ToArray();

        return new LabStatusResult(
            inventory.Devices.Count,
            inventory.Devices.Count(static device => string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase)),
            inventory.Devices.Count(static device => !string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase)),
            inventory.Devices,
            decisions);
    }

    public static async Task<LabDoctorResult> DiagnoseAsync(IDeviceHost runner, string? query, bool fix = false)
    {
        var status = await ReadStatusAsync(runner, query).ConfigureAwait(false);
        var findings = new List<string>();
        var actions = new List<string>();
        var appliedFixes = new List<string>();
        var probes = await LabDoctorProbes.RunAsync(runner).ConfigureAwait(false);
        foreach (var probe in probes.Where(static probe => !probe.Succeeded))
        {
            findings.Add($"ADB probe '{probe.Name}' failed with exit code {probe.ExitCode}.");
        }

        if (status.Total == 0)
        {
            findings.Add("No adb-visible devices were found.");
            actions.Add("Run `adb devices -l`, reconnect USB, authorize debugging, or connect wireless ADB.");
        }

        foreach (var device in status.Devices.Where(static device => !string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add($"{device.Serial ?? "<unknown>"} is {device.State}: {device.RecommendedFix ?? "not available for allocation."}");
            if (!string.IsNullOrWhiteSpace(device.RecommendedFix))
            {
                actions.Add(device.RecommendedFix);
            }
        }

        if (fix)
        {
            appliedFixes.AddRange(await LabDoctorRepairActions.ApplyAsync(runner, status).ConfigureAwait(false));
        }

        if (status.Available > 1 && string.IsNullOrWhiteSpace(query))
        {
            findings.Add("Multiple available devices are attached; implicit selection is ambiguous.");
            actions.Add("Use `--device <serial>` or `--device-query state=online,type=physical,model=<model>`.");
        }

        if (!string.IsNullOrWhiteSpace(query) && status.Decisions.All(static decision => !decision.Selected))
        {
            findings.Add($"Device query '{query}' selected no devices.");
            actions.Add("Run `luotsi lab status` and refine --device-query clauses.");
        }

        return new LabDoctorResult(
            findings.Count == 0 ? "ready" : "attention_required",
            status,
            findings,
            actions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            appliedFixes,
            probes);
    }

    private static LabDeviceDecision ToDecision(DeviceState device, DeviceQuery? selector)
    {
        var selected = selector?.Matches(device) ?? string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase);
        var reason = selector is null
            ? string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase)
                ? "available for implicit allocation"
                : device.RecommendedFix ?? $"not allocatable because state is {device.State}"
            : selected
                ? $"matched query '{selector.RawQuery}'"
                : $"rejected by query '{selector.RawQuery}'";

        return new LabDeviceDecision(device.Serial, device.State, reason, selected, BuildCapabilities(device));
    }

    private static IReadOnlyList<string> BuildCapabilities(DeviceState device)
    {
        var capabilities = new List<string>();
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

        return capabilities.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

}
