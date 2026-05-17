using System.Collections.Frozen;
using Luotsi.Cli.Errors;

namespace Luotsi.Cli.Cli;

/// <summary>
/// Minimal command-line parser for command plus dash-prefixed options.
/// </summary>
public sealed class CliOptions
{
    private static readonly FrozenSet<string> KnownCommands =
    new[]
    {
        "adb",
        "devices",
        "device-wait",
        "preflight",
        "screen-state",
        "inspect",
        "view",
        "reconnect",
        "view-doctor",
        "profile-list",
        "profile-delete",
        "scenario-list",
        "wireless",
        "wireless-scan",
        "wireless-pair",
        "wireless-connect",
        "forward-list",
        "forward",
        "forward-remove",
        "reverse-list",
        "reverse",
        "reverse-remove",
        "start-app",
        "start-uri",
        "force-stop",
        "clear",
        "clear-app",
        "wait-for-activity",
        "wait-for-not-activity",
        "is-app-installed",
        "list-installed-packages",
        "grant-permission",
        "revoke-permission",
        "telemetry-tail",
        "telemetry-watch",
        "wait-for-device",
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

    private static readonly FrozenSet<string> KnownFlagOptions =
    new[]
    {
        "always-on-top",
        "defaults",
        "dry-run",
        "h",
        "headless",
        "help",
        "last",
        "overlay-screen-state",
        "overlay-telemetry",
        "read-only"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _arguments = [];

    private CliOptions(string? command)
    {
        Command = command;
    }

    /// <summary>
    /// Gets the command name.
    /// </summary>
    public string? Command { get; }

    /// <summary>
    /// Gets positional arguments that follow the command token.
    /// </summary>
    public IReadOnlyList<string> Arguments => _arguments;

    /// <summary>
    /// Parses command-line arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Parsed options.</returns>
    public static CliOptions Parse(string[] args)
    {
        var command = FindCommand(args);
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
                parsed._arguments.Add(token);
                continue;
            }

            var key = token.TrimStart('-');
            var value = "true";
            if (!KnownFlagOptions.Contains(key) && i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            parsed._values[key] = value;
        }

        return parsed;
    }

    private static string? FindCommand(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (token.StartsWith("-", StringComparison.Ordinal))
            {
                var key = token.TrimStart('-');
                if (!KnownFlagOptions.Contains(key) && i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    i++;
                }

                continue;
            }

            if (KnownCommands.Contains(token))
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets an optional option value.
    /// </summary>
    /// <param name="key">Option name.</param>
    /// <returns>The option value, if supplied.</returns>
    public string? Get(string key) => _values.GetValueOrDefault(key);

    /// <summary>
    /// Applies default values for options that were not supplied on the command line.
    /// </summary>
    /// <param name="defaults">Default option values keyed by option name.</param>
    public void ApplyDefaults(IReadOnlyDictionary<string, string?> defaults)
    {
        foreach (var (key, value) in defaults)
        {
            _values.TryAdd(key, value);
        }
    }

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
