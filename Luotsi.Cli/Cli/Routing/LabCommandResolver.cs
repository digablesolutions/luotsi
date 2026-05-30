using System.Text;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;
using Polly;

namespace Luotsi.Cli.Cli.Routing;

internal static class LabCommandResolver
{
    public static async Task<LabStatusResult> ReadStatusAsync(
        IDeviceHost runner,
        string? query,
        LabLeaseStore? leaseStore = null,
        LabQuarantineStore? quarantineStore = null,
        LabDeviceInventoryStore? inventoryStore = null,
        DeviceAdmissionRequirements? requirements = null,
        ResiliencePipeline? labProbePipeline = null,
        bool includeProbes = false)
    {
        ArgumentNullException.ThrowIfNull(runner);

        var inventory = DeviceInventory.FromDeviceList(await runner.GetDevicesAsync().ConfigureAwait(false));
        var selector = string.IsNullOrWhiteSpace(query) ? null : new DeviceQuery(query);
        var leases = leaseStore?.ReadActiveLeasesBySerial() ?? new Dictionary<string, LabLeaseResult>(StringComparer.OrdinalIgnoreCase);
        var queueDepthBySerial = leaseStore?.ReadActiveQueueDepthBySerial() ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var quarantines = quarantineStore?.ReadBySerial() ?? new Dictionary<string, LabQuarantineResult>(StringComparer.OrdinalIgnoreCase);
        var registeredDevices = inventoryStore?.ReadBySerial() ?? new Dictionary<string, LabInventoryDeviceResult>(StringComparer.OrdinalIgnoreCase);
        var decisions = inventory.Devices
            .Select(device => ToDecision(device, selector, requirements, registeredDevices, leases, queueDepthBySerial, quarantines))
            .ToArray();

        var probes = includeProbes ? await LabDoctorProbes.RunAsync(runner, labProbePipeline).ConfigureAwait(false) : null;
        return new LabStatusResult(
            inventory.Devices.Count,
            inventory.Devices.Count(static device => string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase)),
            inventory.Devices.Count(static device => !string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase)),
            inventory.Devices,
            decisions,
            probes,
            queueDepthBySerial.Values.Sum(),
            requirements);
    }

    public static async Task<LabDoctorResult> DiagnoseAsync(
        IDeviceHost runner,
        string? query,
        bool fix = false,
        LabLeaseStore? leaseStore = null,
        LabQuarantineStore? quarantineStore = null,
        LabDeviceInventoryStore? inventoryStore = null,
        DeviceAdmissionRequirements? requirements = null,
        ResiliencePipeline? labProbePipeline = null)
    {
        var status = await ReadStatusAsync(runner, query, leaseStore, quarantineStore, inventoryStore, requirements).ConfigureAwait(false);
        var matchedDecisions = GetMatchedDecisions(status, query);
        var findings = new List<string>();
        var actions = new List<string>();
        var appliedFixes = new List<string>();
        var probes = await LabDoctorProbes.RunAsync(runner, labProbePipeline).ConfigureAwait(false);
        foreach (var probe in probes.Where(static probe => !probe.Succeeded))
        {
            var retryDetail = probe.RetryCount > 0 ? $" after {probe.RetryCount} {(probe.RetryCount == 1 ? "retry" : "retries")}." : ".";
            findings.Add($"ADB probe '{probe.Name}' failed with exit code {probe.ExitCode}{retryDetail}");
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

        if (status.Decisions.Count(static decision => decision.Selected) > 1 && string.IsNullOrWhiteSpace(query))
        {
            findings.Add("Multiple available devices are attached; implicit selection is ambiguous.");
            actions.Add("Use `--device <serial>` or `--device-query state=online,type=physical,model=<model>`.");
        }

        if (!string.IsNullOrWhiteSpace(query) && status.Decisions.All(static decision => !decision.Selected))
        {
            findings.Add($"Device query '{query}' selected no devices.");
            actions.Add(matchedDecisions.Any(static decision => decision.Reason.Contains("leased by", StringComparison.OrdinalIgnoreCase))
                ? "Run `luotsi lab leases` or `luotsi lab release --serial <serial>` if the lease is stale."
                : matchedDecisions.Any(static decision => decision.Reason.Contains("queued claim depth", StringComparison.OrdinalIgnoreCase))
                    ? "Run `luotsi lab queue` or retry with `--claim-wait-sec` to join the scheduler queue."
                : matchedDecisions.Any(static decision => decision.Reason.Contains("quarantined by", StringComparison.OrdinalIgnoreCase))
                    ? "Run `luotsi lab quarantines` or `luotsi lab unquarantine --serial <serial>` after the device is healthy."
                : matchedDecisions.Any(static decision => decision.Reason.Contains("requires pool", StringComparison.OrdinalIgnoreCase) || decision.Reason.Contains("requires capabilities", StringComparison.OrdinalIgnoreCase))
                    ? "Run `luotsi lab inventory` and register the required pool/capabilities with `luotsi lab inventory set --serial <adb serial> ...`."
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

    private static IReadOnlyList<LabDeviceDecision> GetMatchedDecisions(LabStatusResult status, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return status.Decisions;
        }

        var selector = new DeviceQuery(query);
        var matchedSerials = status.Devices
            .Where(selector.Matches)
            .Select(static device => device.Serial)
            .Where(static serial => !string.IsNullOrWhiteSpace(serial))
            .Select(static serial => serial!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return status.Decisions
            .Where(decision => !string.IsNullOrWhiteSpace(decision.Serial) && matchedSerials.Contains(decision.Serial))
            .ToArray();
    }

    public static async Task<LabPlanResult> PlanAsync(
        IDeviceHost runner,
        string? query,
        LabLeaseStore? leaseStore = null,
        LabQuarantineStore? quarantineStore = null,
        LabDeviceInventoryStore? inventoryStore = null,
        DeviceAdmissionRequirements? requirements = null)
    {
        var status = await ReadStatusAsync(runner, query, leaseStore, quarantineStore, inventoryStore, requirements).ConfigureAwait(false);
        var selected = status.Decisions.Where(static decision => decision.Selected).ToArray();
        var planContext = DescribePlanContext(status, query, leaseStore, quarantineStore, requirements);
        return selected.Length switch
        {
            1 => new LabPlanResult(
                "ready",
                query,
                selected[0].Serial,
                $"Device `{selected[0].Serial}` would be selected.",
                BuildPlanCommands("ready", query, selected, planContext, requirements),
                status.Decisions,
                Requirements: requirements),
            0 => new LabPlanResult(
                "blocked",
                query,
                null,
                planContext.Summary,
                BuildPlanCommands("blocked", query, selected, planContext, requirements),
                status.Decisions,
                planContext.BlockedReason,
                planContext.NextCapacityAt,
                planContext.SuggestedWaitSec,
                planContext.QueueDepth,
                requirements),
            _ => new LabPlanResult(
                "ambiguous",
                query,
                null,
                $"Multiple devices would be selected: {string.Join(", ", selected.Select(static decision => decision.Serial ?? "<unknown>"))}.",
                BuildPlanCommands("ambiguous", query, selected, planContext, requirements),
                status.Decisions,
                Requirements: requirements)
        };
    }

    private static IReadOnlyList<string> BuildPlanCommands(
        string status,
        string? query,
        IReadOnlyList<LabDeviceDecision> selected,
        LabPlanContext context,
        DeviceAdmissionRequirements? requirements)
    {
        return status switch
        {
            "ready" => [
                BuildLabCommand("claim", query, requirements),
                BuildRunCommand(query, selected.FirstOrDefault()?.Serial, requirements)
            ],
            "ambiguous" => ["luotsi lab status", "luotsi lab plan --device-query state=online,type=physical,model=<model>"],
            "blocked" when string.Equals(context.BlockedReason, "queued", StringComparison.OrdinalIgnoreCase) =>
                ["luotsi lab queue", BuildQueuedRunCommand(query, selected.FirstOrDefault()?.Serial, requirements)],
            "blocked" when string.Equals(context.BlockedReason, "leased", StringComparison.OrdinalIgnoreCase) && context.QueueDepth > 0 =>
                ["luotsi lab queue", "luotsi lab leases", "luotsi lab release --serial <serial>"],
            "blocked" when string.Equals(context.BlockedReason, "leased", StringComparison.OrdinalIgnoreCase) =>
                ["luotsi lab leases", "luotsi lab release --serial <serial>"],
            "blocked" when string.Equals(context.BlockedReason, "quarantined", StringComparison.OrdinalIgnoreCase) =>
                ["luotsi lab quarantines", "luotsi lab unquarantine --serial <serial>"],
            "blocked" when string.Equals(context.BlockedReason, "requirements", StringComparison.OrdinalIgnoreCase) =>
                ["luotsi lab inventory", BuildInventoryCommand(selected.FirstOrDefault()?.Serial, requirements)],
            _ => ["luotsi lab status"]
        };
    }

    private static string BuildLabCommand(string subcommand, string? query, DeviceAdmissionRequirements? requirements)
    {
        var command = "luotsi lab " + subcommand;
        if (!string.IsNullOrWhiteSpace(query))
        {
            command += " --device-query " + Quote(query);
        }

        return command + BuildRequirementOptions(requirements);
    }

    private static string BuildRunCommand(string? query, string? serial, DeviceAdmissionRequirements? requirements)
    {
        const string command = "luotsi run --path <scenarios> --claim-device";
        if (!string.IsNullOrWhiteSpace(query))
        {
            return command + " --device-query " + Quote(query) + BuildRequirementOptions(requirements);
        }

        var resolved = string.IsNullOrWhiteSpace(serial)
            ? command + " --device <adb serial>"
            : command + " --device " + Quote(serial);
        return resolved + BuildRequirementOptions(requirements);
    }

    private static string BuildQueuedRunCommand(string? query, string? serial, DeviceAdmissionRequirements? requirements)
    {
        const string waitOption = " --claim-wait-sec 60";
        var baseCommand = BuildRunCommand(query, serial, requirements);
        return baseCommand + waitOption;
    }

    private static string BuildRequirementOptions(DeviceAdmissionRequirements? requirements)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(requirements?.Pool))
        {
            builder.Append(" --device-pool ");
            builder.Append(Quote(requirements.Pool));
        }

        var capabilities = DeviceAdmissionRequirementsParser.FormatCapabilities(requirements?.Capabilities);
        if (!string.IsNullOrWhiteSpace(capabilities))
        {
            builder.Append(" --require-capabilities ");
            builder.Append(Quote(capabilities));
        }

        return builder.ToString();
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

    public static async Task<LabLeaseResult> ClaimAsync(
        IDeviceHost runner,
        string? query,
        string? owner,
        int ttlSec,
        int claimWaitSec,
        LabLeaseClaimCoordinator claimCoordinator,
        LabLeaseStore leaseStore,
        LabQuarantineStore? quarantineStore = null,
        LabDeviceInventoryStore? inventoryStore = null,
        DeviceAdmissionRequirements? requirements = null)
    {
        ArgumentNullException.ThrowIfNull(leaseStore);
        ArgumentNullException.ThrowIfNull(claimCoordinator);

        var status = await ReadStatusAsync(runner, query, leaseStore, quarantineStore, inventoryStore, requirements).ConfigureAwait(false);
        var selected = status.Decisions
            .Where(static decision => decision.Selected && string.Equals(decision.Status, "online", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var schedulable = status.Decisions
            .Where(decision => CanWaitForClaim(decision, query))
            .ToArray();
        if (selected.Length == 0)
        {
            if (claimWaitSec > 0 && schedulable.Length == 1)
            {
                return await claimCoordinator.ClaimAsync(schedulable[0].Serial!, owner, ttlSec, claimWaitSec).ConfigureAwait(false);
            }

            throw new UsageException("lab claim found no available selected device. Use `luotsi lab status --device-query <query>` to inspect selection.");
        }

        if (selected.Length > 1)
        {
            throw new UsageException("lab claim selected multiple available devices. Add --device-query clauses to claim exactly one device.");
        }

        return await claimCoordinator.ClaimAsync(selected[0].Serial!, owner, ttlSec, claimWaitSec).ConfigureAwait(false);
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
        DeviceAdmissionRequirements? requirements,
        IReadOnlyDictionary<string, LabInventoryDeviceResult> inventory,
        IReadOnlyDictionary<string, LabLeaseResult> leases,
        IReadOnlyDictionary<string, int> queueDepthBySerial,
        IReadOnlyDictionary<string, LabQuarantineResult> quarantines)
    {
        var registered = device.Serial is not null && inventory.TryGetValue(device.Serial, out var registeredDevice)
            ? registeredDevice
            : null;
        var requirementReason = GetRequirementReason(requirements, registered, device);
        LabLeaseResult? lease = null;
        var leased = device.Serial is not null && leases.TryGetValue(device.Serial, out lease);
        var leaseReason = lease is null ? null : $"leased by {lease.Owner} until {lease.ExpiresAt:O}";
        var queueDepth = device.Serial is not null && queueDepthBySerial.TryGetValue(device.Serial, out var queuedClaims) ? queuedClaims : 0;
        var queued = queueDepth > 0;
        var queueReason = queued ? $"queued claim depth {queueDepth}" : null;
        LabQuarantineResult? quarantine = null;
        var quarantined = device.Serial is not null && quarantines.TryGetValue(device.Serial, out quarantine);
        var quarantineReason = quarantine is null ? null : $"quarantined by {quarantine.Owner} at {quarantine.QuarantinedAt:O}: {quarantine.Reason}";
        var queryMatched = selector?.Matches(device) ?? true;
        var available = string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase);
        var selected = queryMatched && available && !leased && !queued && !quarantined && requirementReason is null;
        var reason = selector is null
            ? quarantined
                ? quarantineReason!
                : queued
                ? queueReason!
                : leased
                ? leaseReason!
                : !available
                ? device.RecommendedFix ?? $"not allocatable because state is {device.State}"
                : requirementReason is not null
                ? requirementReason
                : string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase)
                ? "available for implicit allocation"
                : $"not allocatable because state is {device.State}"
            : selected
                ? $"matched query '{selector.RawQuery}'"
                : !queryMatched
                    ? $"rejected by query '{selector.RawQuery}'"
                    : !available
                        ? $"matched query '{selector.RawQuery}' but {device.RecommendedFix ?? $"not allocatable because state is {device.State}"}"
                : quarantined
                    ? $"matched query '{selector.RawQuery}' but {quarantineReason}"
                    : queued
                    ? $"matched query '{selector.RawQuery}' but {queueReason}"
                    : leased
                    ? $"matched query '{selector.RawQuery}' but {leaseReason}"
                    : requirementReason is not null
                        ? $"matched query '{selector.RawQuery}' but {requirementReason}"
                        : $"rejected by query '{selector.RawQuery}'";

        return new LabDeviceDecision(device.Serial, device.State, reason, selected, BuildCapabilities(device, registered), queueDepth, registered?.Pool, registered?.Registered ?? false);
    }

    private static bool CanWaitForClaim(LabDeviceDecision decision, string? query)
    {
        if (string.IsNullOrWhiteSpace(decision.Serial) ||
            string.Equals(decision.Status, "online", StringComparison.OrdinalIgnoreCase) is false)
        {
            return false;
        }

        if (decision.Reason.Contains("quarantined by", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return decision.Reason.Contains("leased by", StringComparison.OrdinalIgnoreCase) ||
                decision.Reason.Contains("queued claim depth", StringComparison.OrdinalIgnoreCase);
        }

        return decision.Reason.Contains($"matched query '{query}'", StringComparison.OrdinalIgnoreCase);
    }

    private static LabPlanContext DescribePlanContext(
        LabStatusResult status,
        string? query,
        LabLeaseStore? leaseStore,
        LabQuarantineStore? quarantineStore,
        DeviceAdmissionRequirements? requirements)
    {
        var selector = string.IsNullOrWhiteSpace(query) ? null : new DeviceQuery(query);
        var leases = leaseStore?.ReadActiveLeasesBySerial() ?? new Dictionary<string, LabLeaseResult>(StringComparer.OrdinalIgnoreCase);
        var queueDepthBySerial = leaseStore?.ReadActiveQueueDepthBySerial() ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var quarantines = quarantineStore?.ReadBySerial() ?? new Dictionary<string, LabQuarantineResult>(StringComparer.OrdinalIgnoreCase);
        var matches = selector is null
            ? status.Devices.ToArray()
            : status.Devices.Where(selector.Matches).ToArray();

        if (matches.Length == 0)
        {
            return new LabPlanContext("no_match", "No device would be selected because the query matched no devices.", null, null, 0);
        }

        var matchSerials = matches
            .Select(static device => device.Serial)
            .Where(static serial => !string.IsNullOrWhiteSpace(serial))
            .Select(static serial => serial!)
            .ToArray();
        var queueDepth = matchSerials.Sum(serial => queueDepthBySerial.GetValueOrDefault(serial));
        var matchingLeases = matchSerials
            .Where(serial => leases.ContainsKey(serial))
            .Select(serial => leases[serial])
            .OrderBy(static lease => lease.ExpiresAt)
            .ToArray();
        var matchingQuarantines = matchSerials
            .Where(serial => quarantines.ContainsKey(serial))
            .Select(serial => quarantines[serial])
            .ToArray();
        var matchedDecisions = status.Decisions
            .Where(decision => matchSerials.Contains(decision.Serial ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (matchingQuarantines.Length == matchSerials.Length && matchingQuarantines.Length > 0)
        {
            return new LabPlanContext("quarantined", "No device would be selected because every matching device is quarantined.", null, null, queueDepth);
        }

        if (matchingLeases.Length == matchSerials.Length && matchingLeases.Length > 0)
        {
            var nextCapacityAt = matchingLeases[0].ExpiresAt;
            var now = leaseStore?.CurrentTime ?? DateTimeOffset.UtcNow;
            var suggestedWaitSec = Math.Max(0, (int)Math.Ceiling((nextCapacityAt - now).TotalSeconds));
            var summary = queueDepth > 0
                ? $"No device would be selected because every matching device is leased. Next capacity is at {nextCapacityAt:O}, and queue depth is {queueDepth}."
                : $"No device would be selected because every matching device is leased. Next capacity is at {nextCapacityAt:O}.";
            return new LabPlanContext("leased", summary, nextCapacityAt, suggestedWaitSec, queueDepth);
        }

        if (queueDepth > 0 && matchSerials.All(serial => queueDepthBySerial.ContainsKey(serial)))
        {
            return new LabPlanContext("queued", $"No device would be selected because matching devices already have queued claims. Queue depth is {queueDepth}.", null, null, queueDepth);
        }

        if (DeviceAdmissionRequirementsParser.HasRequirements(requirements) &&
            matchedDecisions.Any(static decision => decision.Selected) is false &&
            matchedDecisions.Any(static decision => decision.Reason.Contains("requires pool", StringComparison.OrdinalIgnoreCase) || decision.Reason.Contains("requires capabilities", StringComparison.OrdinalIgnoreCase)))
        {
            return new LabPlanContext("requirements", "No device would be selected because matching devices do not satisfy the requested pool/capability requirements.", null, null, queueDepth);
        }

        return new LabPlanContext("blocked", "No device would be selected. Inspect decisions for rejection reasons.", null, null, queueDepth);
    }

    private static string? GetRequirementReason(
        DeviceAdmissionRequirements? requirements,
        LabInventoryDeviceResult? registered,
        DeviceState device)
    {
        if (!DeviceAdmissionRequirementsParser.HasRequirements(requirements))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(requirements?.Pool))
        {
            if (registered is null || !registered.Registered)
            {
                return $"requires pool '{requirements.Pool}' but the device is not registered in lab inventory";
            }

            if (string.IsNullOrWhiteSpace(registered.Pool))
            {
                return $"requires pool '{requirements.Pool}' but the device has no registered pool";
            }

            if (!string.Equals(registered.Pool, requirements.Pool, StringComparison.OrdinalIgnoreCase))
            {
                return $"requires pool '{requirements.Pool}' but inventory pool is '{registered.Pool}'";
            }
        }

        var capabilities = BuildCapabilities(device, registered);
        var missingCapabilities = (requirements?.Capabilities ?? [])
            .Where(required => capabilities.Contains(required, StringComparer.OrdinalIgnoreCase) is false)
            .ToArray();
        if (missingCapabilities.Length == 0)
        {
            return null;
        }

        var advertised = capabilities.Count == 0 ? "none" : string.Join(", ", capabilities);
        return $"requires capabilities [{string.Join(", ", missingCapabilities)}] but the device advertises [{advertised}]";
    }

    private static IReadOnlyList<string> BuildCapabilities(DeviceState device, LabInventoryDeviceResult? registered)
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

        if (registered?.Capabilities is not null)
        {
            capabilities.AddRange(registered.Capabilities);
        }

        return capabilities
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static capability => capability, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

}

internal sealed record LabPlanContext(
    string? BlockedReason,
    string Summary,
    DateTimeOffset? NextCapacityAt,
    int? SuggestedWaitSec,
    int QueueDepth);
