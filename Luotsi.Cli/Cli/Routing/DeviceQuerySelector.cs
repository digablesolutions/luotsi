using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class DeviceQuery(string rawQuery)
{
    private readonly IReadOnlyList<DeviceQueryClause> _clauses = Parse(rawQuery);

    public string RawQuery { get; } = string.IsNullOrWhiteSpace(rawQuery) ? throw new UsageException("--device-query must be non-empty.") : rawQuery;

    public bool Matches(DeviceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return _clauses.All(clause => clause.Matches(state));
    }

    private static IReadOnlyList<DeviceQueryClause> Parse(string rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            throw new UsageException("--device-query must be non-empty.");
        }

        var clauses = rawQuery
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(DeviceQueryClause.Parse)
            .ToArray();

        if (clauses.Length == 0)
        {
            throw new UsageException("--device-query must include at least one key=value clause.");
        }

        return clauses;
    }
}

internal sealed record DeviceQueryClause(string Key, string Value)
{
    public static DeviceQueryClause Parse(string text)
    {
        var parts = text.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new UsageException("--device-query clauses must use key=value syntax separated by commas.");
        }

        var key = parts[0].ToLowerInvariant();
        if (key is not ("serial" or "state" or "status" or "transport" or "type" or "model" or "product" or "device" or "availability"))
        {
            throw new UsageException($"Unsupported --device-query key '{parts[0]}'. Supported keys: serial, state, status, transport, type, model, product, device, availability.");
        }

        return new DeviceQueryClause(key, parts[1]);
    }

    public bool Matches(DeviceState state)
    {
        var actual = Key switch
        {
            "serial" => state.Serial,
            "state" or "status" => state.State,
            "transport" => state.Transport,
            "type" => state.Type,
            "model" => state.Model,
            "product" => state.Product,
            "device" => state.Device,
            "availability" => state.Availability,
            _ => null
        };

        return string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class DeviceQuerySelector
{
    public static DeviceState Select(DeviceInventoryResult inventory, string query)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var parsed = new DeviceQuery(query);
        var matches = inventory.Devices.Where(parsed.Matches).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new UsageException($"--device-query '{query}' matched no devices."),
            _ => throw new UsageException($"--device-query '{query}' matched multiple devices: {string.Join(", ", matches.Select(static device => device.Serial ?? "<unknown>"))}. Add another query clause or pass --device.")
        };
    }
}