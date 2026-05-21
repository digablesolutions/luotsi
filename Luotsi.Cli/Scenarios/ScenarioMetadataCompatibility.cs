using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal static class ScenarioMetadataCompatibility
{
    public static IReadOnlyList<ScenarioMetadataWarning> Evaluate(
        ScenarioMetadata? metadata,
        ScenarioDeviceAllocation? allocation)
    {
        if (metadata is null || allocation is null)
        {
            return [];
        }

        var warnings = new List<ScenarioMetadataWarning>();
        AddMismatch(warnings, "package", "Scenario expects a different app package.", metadata.Package, allocation.Package ?? allocation.Readiness?.Package);
        AddMismatch(warnings, "activity", "Scenario expects a different foreground activity.", metadata.Activity, allocation.Readiness?.CurrentFocus);
        AddMismatch(warnings, "device_serial", "Scenario expects a different device serial.", metadata.Device?.Serial, allocation.Serial);
        AddMismatch(warnings, "device_model", "Scenario expects a different device model.", metadata.Device?.Model, allocation.Readiness?.Model ?? allocation.Device?.Model);
        AddMismatch(warnings, "android_release", "Scenario expects a different Android release.", metadata.Device?.AndroidRelease, allocation.Readiness?.AndroidRelease);
        AddMismatch(warnings, "android_sdk", "Scenario expects a different Android SDK.", metadata.Device?.Sdk, allocation.Readiness?.Sdk);
        return warnings;
    }

    private static void AddMismatch(
        ICollection<ScenarioMetadataWarning> warnings,
        string code,
        string message,
        string? expected,
        string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
        {
            return;
        }

        if (actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        warnings.Add(new ScenarioMetadataWarning(code, message, expected, actual));
    }
}
