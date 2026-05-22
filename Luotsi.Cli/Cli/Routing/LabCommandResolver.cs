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
        var probes = await RunProbesAsync(runner).ConfigureAwait(false);
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

        if (fix && status.Devices.Any(static device => string.Equals(device.State, "offline", StringComparison.OrdinalIgnoreCase)))
        {
            if (runner is IAdbCommandHost adb)
            {
                var reconnect = await adb.ReconnectAdbAsync("offline").ConfigureAwait(false);
                appliedFixes.Add(reconnect.Command.Succeeded
                    ? "Ran `adb reconnect offline`."
                    : $"Tried `adb reconnect offline`, but it exited {reconnect.Command.ExitCode}.");
            }
            else
            {
                findings.Add("Safe offline-device repair was requested, but this device host cannot run adb reconnect.");
            }
        }

        if (fix)
        {
            appliedFixes.AddRange(await RemoveStaleLuotsiPortPlumbingAsync(runner).ConfigureAwait(false));
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

    private static async Task<IReadOnlyList<LabDoctorProbe>> RunProbesAsync(IDeviceHost runner)
    {
        if (runner is not IAdbCommandHost adb)
        {
            return [];
        }

        var probes = new List<LabDoctorProbe>();
        foreach (var probe in new (string Name, Func<Task<AdbDiagnosticResult>> Run)[]
        {
            ("server-status", adb.GetAdbServerStatusAsync),
            ("version", adb.GetAdbVersionAsync),
            ("features", adb.GetAdbFeaturesAsync),
            ("mdns-check", adb.CheckAdbMdnsAsync)
        })
        {
            try
            {
                var result = await probe.Run().ConfigureAwait(false);
                probes.Add(new LabDoctorProbe(probe.Name, result.Command.Succeeded, result.Command.ExitCode, result.Command.Invocation));
            }
            catch (Exception ex)
            {
                probes.Add(new LabDoctorProbe(probe.Name, false, -1, ex.Message));
            }
        }

        return probes;
    }

    private static async Task<IReadOnlyList<string>> RemoveStaleLuotsiPortPlumbingAsync(IDeviceHost runner)
    {
        var fixes = new List<string>();
        try
        {
            var forwards = await runner.ListForwardsAsync().ConfigureAwait(false);
            foreach (var entry in forwards.Entries.Where(IsLuotsiForward))
            {
                await runner.RemoveForwardAsync(entry.Local).ConfigureAwait(false);
                fixes.Add($"Removed stale Luotsi forward `{entry.Local}` -> `{entry.Remote}`.");
            }
        }
        catch (Exception ex)
        {
            fixes.Add($"Skipped stale forward cleanup: {ex.Message}");
        }

        try
        {
            var reverses = await runner.ListReversesAsync().ConfigureAwait(false);
            foreach (var entry in reverses.Entries.Where(IsLuotsiReverse))
            {
                await runner.RemoveReverseAsync(entry.Remote).ConfigureAwait(false);
                fixes.Add($"Removed stale Luotsi reverse `{entry.Remote}` -> `{entry.Local}`.");
            }
        }
        catch (Exception ex)
        {
            fixes.Add($"Skipped stale reverse cleanup: {ex.Message}");
        }

        return fixes;
    }

    private static bool IsLuotsiForward(PortForwardEntry entry) =>
        ContainsLuotsiPortMarker(entry.Local) || ContainsLuotsiPortMarker(entry.Remote);

    private static bool IsLuotsiReverse(PortReverseEntry entry) =>
        ContainsLuotsiPortMarker(entry.Local) || ContainsLuotsiPortMarker(entry.Remote);

    private static bool ContainsLuotsiPortMarker(string value) =>
        value.Contains("localabstract:luotsi_view_", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("device-e2e", StringComparison.OrdinalIgnoreCase);
}
