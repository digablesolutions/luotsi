using System.Collections.Frozen;
using Luotsi.Cli.Errors;

namespace Luotsi.Cli.Cli;

/// <summary>
/// Minimal command-line parser for command plus dash-prefixed options.
/// </summary>
public sealed class CliOptions
{
    private const string ViewCommand = "view";
    private const string ViewSetupCommand = "view-setup";
    private const string ViewSetupAlias = "setup";

    private static readonly FrozenSet<string> KnownCommands =
    new[]
    {
        "adb",
        "help",
        "version",
        "update",
        "devices",
        "device-status",
        "device-wait",
        "preflight",
        "doctor",
        "screen-state",
        "inspect",
        "view",
        "reconnect",
        "view-doctor",
        "view-setup",
        "profile-list",
        "profile-delete",
        "scenario-init",
        "scenario-list",
        "scenario-validate",
        "scenario-explain",
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
        "replay",
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
        "lab",
        "logcat",
        "wait-log",
        "record",
        "run"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> KnownCommandNames => KnownCommands;

    private static readonly FrozenSet<string> KnownFlagOptions =
    new[]
    {
        "always-on-top",
        "detach",
        "defaults",
        "dry-run",
        "fix",
        "force",
        "h",
        "headless",
        "human",
        "help",
        "json",
        "last",
        "overlay-screen-state",
        "overlay-telemetry",
        "no-require-device-ready",
        "quiet",
        "read-only",
        "validate-only"
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
        var commandMatch = FindCommand(args);
        var parsed = new CliOptions(commandMatch.Command);

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (i == commandMatch.CommandIndex || i == commandMatch.NormalizedArgumentIndex)
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

    private static CommandMatch FindCommand(string[] args)
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
                if (string.Equals(token, ViewCommand, StringComparison.OrdinalIgnoreCase)
                    && TryFindNormalizedViewArgumentIndex(args, i, out var normalizedArgumentIndex))
                {
                    return new CommandMatch(ViewSetupCommand, i, normalizedArgumentIndex);
                }

                return new CommandMatch(token, i, -1);
            }
        }

        return new CommandMatch(null, -1, -1);
    }

    private static bool TryFindNormalizedViewArgumentIndex(string[] args, int commandIndex, out int normalizedArgumentIndex)
    {
        for (var i = commandIndex + 1; i < args.Length; i++)
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

            if (string.Equals(token, ViewSetupAlias, StringComparison.OrdinalIgnoreCase))
            {
                normalizedArgumentIndex = i;
                return true;
            }

            break;
        }

        normalizedArgumentIndex = -1;
        return false;
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

    private readonly record struct CommandMatch(string? Command, int CommandIndex, int NormalizedArgumentIndex);
}
