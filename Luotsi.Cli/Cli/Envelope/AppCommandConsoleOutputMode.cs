using Luotsi.Cli.Errors;

namespace Luotsi.Cli.Cli.Envelope;

internal enum AppCommandConsoleOutputMode
{
    Json,
    Human,
    Quiet
}

internal static class AppCommandConsoleOutputModeResolver
{
    public static AppCommandConsoleOutputMode Resolve(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var human = options.HasFlag("human");
        var json = options.HasFlag("json");
        var quiet = options.HasFlag("quiet");
        if (human && json)
        {
            throw new UsageException("Use either --human or --json, not both.");
        }

        if (quiet && (human || json))
        {
            throw new UsageException("Use only one of --quiet, --human, or --json.");
        }

        var value = options.Get("console-output");
        if (string.IsNullOrWhiteSpace(value))
        {
            if (quiet)
            {
                return AppCommandConsoleOutputMode.Quiet;
            }

            return human ? AppCommandConsoleOutputMode.Human : AppCommandConsoleOutputMode.Json;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (quiet && normalized != "quiet")
        {
            throw new UsageException("Use either --quiet or --console-output human/json, not both.");
        }

        if (human && normalized != "human")
        {
            throw new UsageException("Use either --human or --console-output json/quiet, not both.");
        }

        if (json && normalized != "json")
        {
            throw new UsageException("Use either --json or --console-output human/quiet, not both.");
        }

        return normalized switch
        {
            "human" => AppCommandConsoleOutputMode.Human,
            "json" => AppCommandConsoleOutputMode.Json,
            "quiet" => AppCommandConsoleOutputMode.Quiet,
            _ => throw new UsageException("Option --console-output must be human, json, or quiet.")
        };
    }

    public static AppCommandConsoleOutputMode ResolveForFailure(CliOptions options)
    {
        try
        {
            return Resolve(options);
        }
        catch (UsageException)
        {
            return AppCommandConsoleOutputMode.Json;
        }
    }
}
