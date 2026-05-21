using System.Globalization;
using System.Text;

using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal static class ScenarioMetadataCompatibility
{
    public static ScenarioRunResult Attach(ScenarioRunResult result, ScenarioDeviceAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(allocation);

        return result with
        {
            DeviceAllocation = allocation,
            MetadataWarnings = Evaluate(result.Metadata, allocation)
        };
    }

    public static ScenarioRunBatchResult Attach(ScenarioRunBatchResult result, ScenarioDeviceAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(allocation);

        return result with
        {
            DeviceAllocation = allocation,
            Scenarios = result.Scenarios.Select(scenario => Attach(scenario, allocation)).ToArray()
        };
    }

    public static ScenarioBatchItemResult Attach(ScenarioBatchItemResult result, ScenarioDeviceAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(allocation);

        var data = result.Data is null ? null : Attach(result.Data, allocation);
        return result with
        {
            Data = data,
            MetadataWarnings = Evaluate(data?.Metadata ?? result.Metadata, allocation)
        };
    }

    public static ScenarioRunFailureData Attach(ScenarioRunFailureData result, ScenarioDeviceAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(allocation);

        return result with { MetadataWarnings = Evaluate(result.Metadata, allocation) };
    }

    public static IReadOnlyList<ScenarioMetadataWarning> Evaluate(
        ScenarioMetadata? metadata,
        ScenarioDeviceAllocation? allocation)
    {
        if (metadata is null || allocation is null)
        {
            return [];
        }

        var warnings = new List<ScenarioMetadataWarning>();
        var readiness = allocation.Readiness;
        AddExactMismatch(warnings, "package", "Scenario expects a different app package.", metadata.Package, readiness?.ForegroundPackage ?? readiness?.Package);
        AddContainsMismatch(warnings, "activity", "Scenario expects a different foreground activity.", metadata.Activity, readiness?.CurrentFocus);
        AddExactMismatch(warnings, "device_serial", "Scenario expects a different device serial.", metadata.Device?.Serial, allocation.Serial);
        AddNormalizedMismatch(warnings, "device_model", "Scenario expects a different device model.", metadata.Device?.Model, readiness?.Model ?? allocation.Device?.Model);
        AddExactMismatch(warnings, "android_release", "Scenario expects a different Android release.", metadata.Device?.AndroidRelease, readiness?.AndroidRelease);
        AddExactMismatch(warnings, "android_sdk", "Scenario expects a different Android SDK.", metadata.Device?.Sdk, readiness?.Sdk);
        AddIntegerMismatch(warnings, "layout_width", "Scenario expects a different screen width.", metadata.Layout?.Width, readiness?.DisplayWidth);
        AddIntegerMismatch(warnings, "layout_height", "Scenario expects a different screen height.", metadata.Layout?.Height, readiness?.DisplayHeight);
        AddExactMismatch(warnings, "layout_orientation", "Scenario expects a different screen orientation.", metadata.Layout?.Orientation, readiness?.DisplayOrientation);
        return warnings;
    }

    private static void AddExactMismatch(
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

        var normalizedExpected = expected.Trim();
        var normalizedActual = actual.Trim();
        if (string.Equals(normalizedExpected, normalizedActual, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        warnings.Add(new ScenarioMetadataWarning(code, message, normalizedExpected, normalizedActual));
    }

    private static void AddContainsMismatch(
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

        var normalizedExpected = expected.Trim();
        var normalizedActual = actual.Trim();
        if (normalizedActual.Contains(normalizedExpected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        warnings.Add(new ScenarioMetadataWarning(code, message, normalizedExpected, normalizedActual));
    }

    private static void AddNormalizedMismatch(
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

        var normalizedExpected = expected.Trim();
        var normalizedActual = actual.Trim();
        if (string.Equals(NormalizeLooseText(normalizedExpected), NormalizeLooseText(normalizedActual), StringComparison.Ordinal))
        {
            return;
        }

        warnings.Add(new ScenarioMetadataWarning(code, message, normalizedExpected, normalizedActual));
    }

    private static void AddIntegerMismatch(
        ICollection<ScenarioMetadataWarning> warnings,
        string code,
        string message,
        int? expected,
        int? actual)
    {
        if (!expected.HasValue || !actual.HasValue || expected.Value == actual.Value)
        {
            return;
        }

        warnings.Add(new ScenarioMetadataWarning(
            code,
            message,
            expected.Value.ToString(CultureInfo.InvariantCulture),
            actual.Value.ToString(CultureInfo.InvariantCulture)));
    }

    private static string NormalizeLooseText(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Where(char.IsLetterOrDigit))
        {
            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
