using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Infrastructure.Devices;

internal static class DeviceAdmissionRequirementsParser
{
    public static DeviceAdmissionRequirements? Parse(
        string? pool,
        string? capabilitiesCsv,
        string poolOptionName,
        string capabilitiesOptionName)
    {
        var normalizedPool = NormalizePool(pool, poolOptionName);
        var capabilities = ParseCapabilities(capabilitiesCsv, capabilitiesOptionName);
        return string.IsNullOrWhiteSpace(normalizedPool) && capabilities.Count == 0
            ? null
            : new DeviceAdmissionRequirements(normalizedPool, capabilities);
    }

    public static IReadOnlyList<string> ParseCapabilities(string? capabilitiesCsv, string optionName)
    {
        if (string.IsNullOrWhiteSpace(capabilitiesCsv))
        {
            return [];
        }

        var capabilities = capabilitiesCsv
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(static capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static capability => capability, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (capabilities.Length == 0)
        {
            throw new UsageException($"{optionName} must include one or more comma-separated capability names.");
        }

        return capabilities;
    }

    public static string FormatCapabilities(IReadOnlyList<string>? capabilities) =>
        capabilities is null || capabilities.Count == 0
            ? string.Empty
            : string.Join(",", capabilities);

    public static bool HasRequirements(DeviceAdmissionRequirements? requirements) =>
        requirements is not null &&
        (!string.IsNullOrWhiteSpace(requirements.Pool) ||
         requirements.Capabilities is { Count: > 0 });

    private static string? NormalizePool(string? pool, string optionName)
    {
        if (pool is null)
        {
            return null;
        }

        var normalized = pool.Trim();
        if (normalized.Length == 0)
        {
            throw new UsageException($"{optionName} must be non-empty when supplied.");
        }

        return normalized;
    }
}
