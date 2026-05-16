using System.Collections.Frozen;
using VisitLab.Cli.Errors;

namespace VisitLab.Cli.Cli;

/// <summary>
/// Minimal command-line parser for command plus dash-prefixed options.
/// </summary>
public sealed class CliOptions
{
    private static readonly FrozenSet<string> KnownCommands =
    new[]
    {
        "devices",
        "preflight",
        "screen-state",
        "inspect",
        "view",
        "telemetry-tail",
        "telemetry-watch",
        "wait-step",
        "wait-action-ready",
        "tap",
        "tap-text",
        "wait-visible",
        "type-text",
        "keyevent",
        "logcat",
        "wait-log",
        "record",
        "run"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    private CliOptions(string? command)
    {
        Command = command;
    }

    /// <summary>
    /// Gets the command name.
    /// </summary>
    public string? Command { get; }

    /// <summary>
    /// Parses command-line arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Parsed options.</returns>
    public static CliOptions Parse(string[] args)
    {
        var command = args.FirstOrDefault(static a => KnownCommands.Contains(a));
        var parsed = new CliOptions(command);

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (string.Equals(token, command, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!token.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var key = token.TrimStart('-');
            var value = "true";
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            parsed._values[key] = value;
        }

        return parsed;
    }

    /// <summary>
    /// Gets an optional option value.
    /// </summary>
    /// <param name="key">Option name.</param>
    /// <returns>The option value, if supplied.</returns>
    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Gets whether a flag was supplied.
    /// </summary>
    /// <param name="key">Flag name.</param>
    /// <returns>True when the flag was supplied.</returns>
    public bool HasFlag(string key) => _values.ContainsKey(key);

    /// <summary>
    /// Gets a required option.
    /// </summary>
    /// <param name="key">Option name.</param>
    /// <returns>The option value.</returns>
    public string Require(string key) => Get(key) ?? throw new UsageException($"Missing required option --{key}.");

    /// <summary>
    /// Gets an integer option.
    /// </summary>
    /// <param name="key">Option name.</param>
    /// <param name="defaultValue">Default value.</param>
    /// <returns>The parsed integer.</returns>
    public int Int(string key, int defaultValue)
    {
        var value = Get(key);
        if (value is null)
        {
            return defaultValue;
        }

        return int.TryParse(value, out var parsed) ? parsed : throw new UsageException($"Option --{key} must be an integer.");
    }
}