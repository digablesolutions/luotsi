using Luotsi.Cli.Errors;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli;

internal static class ScenarioQueryFactory
{
    public static ScenarioQuery CreateListQuery(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return CreateQuery(options, requirePath: true);
    }

    public static ScenarioQuery CreateCatalogRunQuery(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return CreateQuery(options, requirePath: false);
    }

    public static bool UsesCatalogExecution(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Get("path") is not null;
    }

    private static ScenarioQuery CreateQuery(CliOptions options, bool requirePath)
    {
        var path = options.Get("path") ?? options.Get("file");
        if (requirePath && string.IsNullOrWhiteSpace(path))
        {
            throw new UsageException("Missing required option --path.");
        }

        path ??= options.Require("file");
        int? shardCount = options.Get("shard-count") is null ? null : options.Int("shard-count", 0);
        int? shardIndex = options.Get("shard-index") is null ? null : options.Int("shard-index", 0);
        return new ScenarioQuery(
            path,
            SplitOption(options.Get("include-tag") ?? options.Get("tag")),
            SplitOption(options.Get("exclude-tag")),
            options.Get("name"),
            options.Get("action"),
            shardCount,
            shardIndex,
            options.HasFlag("dry-run"));
    }

    private static string[] SplitOption(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}