using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal static class LabDoctorRepairActions
{
    public static async Task<IReadOnlyList<string>> ApplyAsync(IDeviceHost runner, LabStatusResult status)
    {
        var appliedFixes = new List<string>();
        if (status.Devices.Any(static device => string.Equals(device.State, "offline", StringComparison.OrdinalIgnoreCase)))
        {
            appliedFixes.Add(await ReconnectOfflineAsync(runner).ConfigureAwait(false));
        }

        var hasMultipleAttachedDevices = status.Devices
            .Select(static device => device.Serial)
            .Where(static serial => !string.IsNullOrWhiteSpace(serial))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any();
        var hasExplicitQuerySelection = status.Decisions.Any(static decision =>
            decision.Selected && decision.Reason.StartsWith("matched query", StringComparison.OrdinalIgnoreCase));
        if (hasMultipleAttachedDevices && !hasExplicitQuerySelection)
        {
            appliedFixes.Add("Skipped stale Luotsi port cleanup because multiple devices are attached; rerun with --device or --device-query so adb remove commands are serial-scoped.");
            return appliedFixes.Where(static fix => !string.IsNullOrWhiteSpace(fix)).ToArray();
        }

        appliedFixes.AddRange(await RemoveStaleLuotsiPortPlumbingAsync(runner).ConfigureAwait(false));
        return appliedFixes.Where(static fix => !string.IsNullOrWhiteSpace(fix)).ToArray();
    }

    private static async Task<string> ReconnectOfflineAsync(IDeviceHost runner)
    {
        if (runner is not IAdbCommandHost adb)
        {
            return "Safe offline-device repair was requested, but this device host cannot run adb reconnect.";
        }

        var reconnect = await adb.ReconnectAdbAsync("offline").ConfigureAwait(false);
        return reconnect.Command.Succeeded
            ? "Ran `adb reconnect offline`."
            : $"Tried `adb reconnect offline`, but it exited {reconnect.Command.ExitCode}.";
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
        catch (Exception ex) when (ex is not OperationCanceledException)
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
        catch (Exception ex) when (ex is not OperationCanceledException)
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
