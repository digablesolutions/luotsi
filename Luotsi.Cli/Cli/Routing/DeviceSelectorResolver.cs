using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Hosting;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal static class DeviceSelectorResolver
{
    public static async Task<string?> ResolveAsync(
        CliOptions options,
        string adbExecutable,
        ArtifactSession artifacts,
        string? command,
        DeviceHostLauncher deviceHostLauncher,
        LabLeaseStore? leaseStore = null,
        LabQuarantineStore? quarantineStore = null,
        LabDeviceInventoryStore? inventoryStore = null)
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
        var requirements = DeviceAdmissionRequirementsParser.Parse(
            options.Get("device-pool"),
            options.Get("require-capabilities"),
            "--device-pool",
            "--require-capabilities");
        var status = await LabCommandResolver.ReadStatusAsync(
            inventoryHost,
            query,
            leaseStore,
            quarantineStore,
            inventoryStore,
            requirements).ConfigureAwait(false);
        if (CanWaitForClaim(options) && TryResolveSingleScheduledMatch(status.Decisions, query, out var scheduledSerial))
        {
            return scheduledSerial;
        }

        var selector = new DeviceQuery(query);
        var matches = status.Devices.Where(selector.Matches).ToArray();
        var selected = status.Decisions.Where(static decision => decision.Selected).ToArray();
        if (selected.Length == 0 &&
            TryResolveSingleDiagnosticMatch(options.Command, matches, status.Decisions, requirements, out var diagnosticSerial))
        {
            return diagnosticSerial;
        }

        if (selected.Length == 0)
        {
            ThrowSelectionFailure(query, matches, status.Decisions, requirements);
        }

        return selected.Length switch
        {
            1 when string.IsNullOrWhiteSpace(selected[0].Serial) => throw new UsageException($"--device-query '{query}' selected a device without a serial."),
            1 => selected[0].Serial,
            _ => throw new UsageException($"--device-query '{query}' matched multiple devices: {string.Join(", ", selected.Select(static device => device.Serial ?? "<unknown>"))}. Add another query clause or pass --device.")
        };
    }

    private static void ThrowSelectionFailure(
        string query,
        IReadOnlyList<DeviceState> matches,
        IReadOnlyList<LabDeviceDecision> decisions,
        DeviceAdmissionRequirements? requirements)
    {
        if (matches.Count == 0)
        {
            throw new UsageException($"--device-query '{query}' matched no devices.");
        }

        var matchedDecisions = decisions
            .Where(decision => matches.Any(device => string.Equals(device.Serial, decision.Serial, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (matchedDecisions.All(static decision => decision.Reason.Contains("leased by", StringComparison.OrdinalIgnoreCase)))
        {
            throw new UsageException($"--device-query '{query}' matched only leased devices: {string.Join(", ", matchedDecisions.Select(static decision => $"{decision.Serial} {decision.Reason}"))}. Run `luotsi lab leases` or release a lease before selecting this device.");
        }

        if (matchedDecisions.All(static decision => decision.Reason.Contains("queued claim depth", StringComparison.OrdinalIgnoreCase)))
        {
            throw new UsageException($"--device-query '{query}' matched only devices with queued claims: {string.Join(", ", matchedDecisions.Select(static decision => $"{decision.Serial} {decision.Reason}"))}. Run `luotsi lab queue` or use --claim-wait-sec to join the queue.");
        }

        if (matchedDecisions.All(static decision => decision.Reason.Contains("quarantined by", StringComparison.OrdinalIgnoreCase)))
        {
            throw new UsageException($"--device-query '{query}' matched only quarantined devices: {string.Join(", ", matchedDecisions.Select(static decision => $"{decision.Serial} {decision.Reason}"))}. Run `luotsi lab quarantines` or unquarantine a healthy device before selecting it.");
        }

        if (matchedDecisions.All(IsRequirementFailure))
        {
            throw new UsageException(
                $"--device-query '{query}' matched only devices that failed the requested admission requirements: {string.Join(", ", matchedDecisions.Select(static decision => $"{decision.Serial} {decision.Reason}"))}. " +
                $"Run `luotsi lab inventory` or `{BuildInventoryCommand(requirements)}` before retrying.");
        }

        if (matchedDecisions.All(IsUnavailableFailure))
        {
            throw new UsageException($"--device-query '{query}' matched only unavailable devices: {string.Join(", ", matchedDecisions.Select(static decision => $"{decision.Serial} {decision.Reason}"))}. Run `luotsi lab status --device-query {Quote(query)}` to inspect selection.");
        }

        throw new UsageException($"--device-query '{query}' matched devices but none were allocatable. Run `luotsi lab status --device-query {Quote(query)}` to inspect selection.");
    }

    private static bool CanWaitForClaim(CliOptions options) =>
        string.Equals(options.Command, "run", StringComparison.OrdinalIgnoreCase) &&
        options.HasFlag("claim-device") &&
        options.Int("claim-wait-sec", 0) > 0;

    private static bool TryResolveSingleScheduledMatch(
        IReadOnlyList<LabDeviceDecision> decisions,
        string query,
        out string? serial)
    {
        serial = null;
        var matched = decisions
            .Where(decision => decision.Reason.Contains($"matched query '{query}'", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matched.Length != 1 || string.IsNullOrWhiteSpace(matched[0].Serial))
        {
            return false;
        }

        if (matched[0].Reason.Contains("quarantined by", StringComparison.OrdinalIgnoreCase) ||
            matched[0].Reason.Contains("requires pool", StringComparison.OrdinalIgnoreCase) ||
            matched[0].Reason.Contains("requires capabilities", StringComparison.OrdinalIgnoreCase) ||
            matched[0].Reason.Contains("not allocatable", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        serial = matched[0].Serial;
        return true;
    }

    private static bool TryResolveSingleDiagnosticMatch(
        string? command,
        IReadOnlyList<DeviceState> matches,
        IReadOnlyList<LabDeviceDecision> decisions,
        DeviceAdmissionRequirements? requirements,
        out string? serial)
    {
        serial = null;
        if (string.Equals(command, "run", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0].Serial))
        {
            return false;
        }

        var matchedDecision = decisions.SingleOrDefault(decision => string.Equals(decision.Serial, matches[0].Serial, StringComparison.OrdinalIgnoreCase));
        if (matchedDecision is null ||
            matchedDecision.Reason.Contains("leased by", StringComparison.OrdinalIgnoreCase) ||
            matchedDecision.Reason.Contains("queued claim depth", StringComparison.OrdinalIgnoreCase) ||
            matchedDecision.Reason.Contains("quarantined by", StringComparison.OrdinalIgnoreCase) ||
            (HasRequirements(requirements) && IsRequirementFailure(matchedDecision)))
        {
            return false;
        }

        serial = matches[0].Serial;
        return true;
    }

    private static bool HasRequirements(DeviceAdmissionRequirements? requirements) =>
        !string.IsNullOrWhiteSpace(requirements?.Pool) || requirements?.Capabilities is { Count: > 0 };

    private static bool IsRequirementFailure(LabDeviceDecision decision) =>
        decision.Reason.Contains("requires pool", StringComparison.OrdinalIgnoreCase) ||
        decision.Reason.Contains("requires capabilities", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnavailableFailure(LabDeviceDecision decision) =>
        decision.Reason.Contains("not allocatable", StringComparison.OrdinalIgnoreCase) ||
        decision.Reason.Contains("state is", StringComparison.OrdinalIgnoreCase);

    private static string BuildInventoryCommand(DeviceAdmissionRequirements? requirements)
    {
        var command = "luotsi lab inventory set --serial <adb serial>";
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
}
