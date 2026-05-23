using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal static class LabCommandResolver
{
    public static async Task<LabStatusResult> ReadStatusAsync(
        IDeviceHost runner,
        string? query,
        LabLeaseStore? leaseStore = null,
        LabQuarantineStore? quarantineStore = null)
    {
        ArgumentNullException.ThrowIfNull(runner);

        var inventory = DeviceInventory.FromDeviceList(await runner.GetDevicesAsync().ConfigureAwait(false));
        var selector = string.IsNullOrWhiteSpace(query) ? null : new DeviceQuery(query);
        var leases = leaseStore?.ReadActiveLeasesBySerial() ?? new Dictionary<string, LabLeaseResult>(StringComparer.OrdinalIgnoreCase);
        var quarantines = quarantineStore?.ReadBySerial() ?? new Dictionary<string, LabQuarantineResult>(StringComparer.OrdinalIgnoreCase);
        var decisions = inventory.Devices
            .Select(device => ToDecision(device, selector, leases, quarantines))
            .ToArray();

        return new LabStatusResult(
            inventory.Devices.Count,
            inventory.Devices.Count(static device => string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase)),
            inventory.Devices.Count(static device => !string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase)),
            inventory.Devices,
            decisions);
    }

    public static async Task<LabDoctorResult> DiagnoseAsync(
        IDeviceHost runner,
        string? query,
        bool fix = false,
        LabLeaseStore? leaseStore = null,
        LabQuarantineStore? quarantineStore = null)
    {
        var status = await ReadStatusAsync(runner, query, leaseStore, quarantineStore).ConfigureAwait(false);
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
            actions.Add(status.Decisions.Any(static decision => decision.Reason.Contains("leased by", StringComparison.OrdinalIgnoreCase))
                ? "Run `luotsi lab leases` or `luotsi lab release --lease <lease-id>` if the lease is stale."
                : status.Decisions.Any(static decision => decision.Reason.Contains("quarantined by", StringComparison.OrdinalIgnoreCase))
                    ? "Run `luotsi lab quarantines` or `luotsi lab unquarantine --serial <serial>` after the device is healthy."
                : "Run `luotsi lab status` and refine --device-query clauses.");
        }

        return new LabDoctorResult(
            findings.Count == 0 ? "ready" : "attention_required",
            status,
            findings,
            actions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            appliedFixes,
            probes);
    }

    public static async Task<LabPlanResult> PlanAsync(
        IDeviceHost runner,
        string? query,
        LabLeaseStore? leaseStore = null,
        LabQuarantineStore? quarantineStore = null)
    {
        var status = await ReadStatusAsync(runner, query, leaseStore, quarantineStore).ConfigureAwait(false);
        var selected = status.Decisions.Where(static decision => decision.Selected).ToArray();
        return selected.Length switch
        {
            1 => new LabPlanResult(
                "ready",
                query,
                selected[0].Serial,
                $"Device `{selected[0].Serial}` would be selected.",
                BuildPlanCommands("ready", query, selected, status.Decisions),
                status.Decisions),
            0 => new LabPlanResult(
                "blocked",
                query,
                null,
                "No device would be selected. Inspect decisions for rejection reasons.",
                BuildPlanCommands("blocked", query, selected, status.Decisions),
                status.Decisions),
            _ => new LabPlanResult(
                "ambiguous",
                query,
                null,
                $"Multiple devices would be selected: {string.Join(", ", selected.Select(static decision => decision.Serial ?? "<unknown>"))}.",
                BuildPlanCommands("ambiguous", query, selected, status.Decisions),
                status.Decisions)
        };
    }

    private static IReadOnlyList<string> BuildPlanCommands(
        string status,
        string? query,
        IReadOnlyList<LabDeviceDecision> selected,
        IReadOnlyList<LabDeviceDecision> decisions)
    {
        return status switch
        {
            "ready" => [BuildLabCommand("claim", query)],
            "ambiguous" => ["luotsi lab status", "luotsi lab plan --device-query state=online,type=physical,model=<model>"],
            "blocked" when decisions.Any(static decision => decision.Reason.Contains("leased by", StringComparison.OrdinalIgnoreCase)) =>
                ["luotsi lab leases", "luotsi lab release --lease <lease-id>"],
            "blocked" when decisions.Any(static decision => decision.Reason.Contains("quarantined by", StringComparison.OrdinalIgnoreCase)) =>
                ["luotsi lab quarantines", "luotsi lab unquarantine --serial <serial>"],
            _ => ["luotsi lab status"]
        };
    }

    private static string BuildLabCommand(string subcommand, string? query)
    {
        var command = "luotsi lab " + subcommand;
        return string.IsNullOrWhiteSpace(query)
            ? command
            : command + " --device-query " + Quote(query);
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) || value.Contains(',', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;

    public static async Task<LabLeaseResult> ClaimAsync(
        IDeviceHost runner,
        string? query,
        string? owner,
        int ttlSec,
        LabLeaseStore leaseStore,
        LabQuarantineStore? quarantineStore = null)
    {
        ArgumentNullException.ThrowIfNull(leaseStore);

        var status = await ReadStatusAsync(runner, query, leaseStore, quarantineStore).ConfigureAwait(false);
        var selected = status.Decisions
            .Where(static decision => decision.Selected && string.Equals(decision.Status, "online", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (selected.Length == 0)
        {
            throw new UsageException("lab claim found no available selected device. Use `luotsi lab status --device-query <query>` to inspect selection.");
        }

        if (selected.Length > 1)
        {
            throw new UsageException("lab claim selected multiple available devices. Add --device-query clauses to claim exactly one device.");
        }

        return await leaseStore.ClaimAsync(selected[0].Serial!, owner, ttlSec).ConfigureAwait(false);
    }

    public static async Task<LabQuarantineResult> QuarantineAsync(
        IDeviceHost runner,
        string? query,
        string reason,
        string? owner,
        LabQuarantineStore quarantineStore)
    {
        ArgumentNullException.ThrowIfNull(quarantineStore);

        var inventory = DeviceInventory.FromDeviceList(await runner.GetDevicesAsync().ConfigureAwait(false));
        var selected = string.IsNullOrWhiteSpace(query)
            ? inventory.Devices.Where(static device => string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase)).ToArray()
            : inventory.Devices.Where(new DeviceQuery(query).Matches).ToArray();
        if (selected.Length == 0)
        {
            throw new UsageException("lab quarantine selected no devices.");
        }

        if (selected.Length > 1)
        {
            throw new UsageException("lab quarantine selected multiple devices. Add --device-query clauses to quarantine exactly one device.");
        }

        if (string.IsNullOrWhiteSpace(selected[0].Serial))
        {
            throw new UsageException("lab quarantine selected a device without a serial.");
        }

        return await quarantineStore.QuarantineAsync(selected[0].Serial!, reason, owner).ConfigureAwait(false);
    }

    private static LabDeviceDecision ToDecision(
        DeviceState device,
        DeviceQuery? selector,
        IReadOnlyDictionary<string, LabLeaseResult> leases,
        IReadOnlyDictionary<string, LabQuarantineResult> quarantines)
    {
        LabLeaseResult? lease = null;
        var leased = device.Serial is not null && leases.TryGetValue(device.Serial, out lease);
        var leaseReason = lease is null ? null : $"leased by {lease.Owner} until {lease.ExpiresAt:O}";
        LabQuarantineResult? quarantine = null;
        var quarantined = device.Serial is not null && quarantines.TryGetValue(device.Serial, out quarantine);
        var quarantineReason = quarantine is null ? null : $"quarantined by {quarantine.Owner} at {quarantine.QuarantinedAt:O}: {quarantine.Reason}";
        var selected = !leased && !quarantined && (selector?.Matches(device) ?? string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase));
        var reason = selector is null
            ? quarantined
                ? quarantineReason!
                : leased
                ? leaseReason!
                : string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase)
                ? "available for implicit allocation"
                : device.RecommendedFix ?? $"not allocatable because state is {device.State}"
            : selected
                ? $"matched query '{selector.RawQuery}'"
                : quarantined
                    ? $"matched query '{selector.RawQuery}' but {quarantineReason}"
                    : leased
                    ? $"matched query '{selector.RawQuery}' but {leaseReason}"
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
