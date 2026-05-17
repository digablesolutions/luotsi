using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli;

internal sealed class ScenarioCommandDispatcher(
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    IDelay delay,
    IEnvironmentVariables environment)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public async Task<ScenarioListResult> ListAsync(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var query = CreateQuery(options, requirePath: true);
        var selection = await DiscoverAsync(query).ConfigureAwait(false);
        return new ScenarioListResult(query.Path, selection.TotalCount, selection.Matched.Count, selection.Matched);
    }

    public async Task<object> RunAsync(CliOptions options, IDeviceHost runner)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);

        var scenarios = new ScenarioExecutor(runner, _fileSystem, _timeProvider, _delay, CreateTemplateResolver(), _environment);
        if (!UsesCatalogExecution(options))
        {
            if (options.HasFlag("dry-run"))
            {
                throw new UsageException("run --dry-run requires --path. Use run --file <scenario.json> without --dry-run for single-scenario execution.");
            }

            return await scenarios.RunAsync(options.Require("file")).ConfigureAwait(false);
        }

        var query = CreateQuery(options, requirePath: false);
        var selection = await DiscoverAsync(query).ConfigureAwait(false);

        if (query.DryRun)
        {
            return new ScenarioRunPlanResult(
                query.Path,
                true,
                selection.TotalCount,
                selection.Matched.Count,
                selection.Selected.Count,
                query.ShardCount,
                query.ShardIndex,
                selection.Selected);
        }

        return await new ScenarioBatchExecutor(scenarios).RunAsync(new ScenarioBatchExecutionRequest(
            query,
            selection.TotalCount,
            selection.Matched.Count,
            selection.Selected)).ConfigureAwait(false);
    }

    private static bool UsesCatalogExecution(CliOptions options) =>
        options.Get("path") is not null;

    private async Task<ScenarioSelection> DiscoverAsync(ScenarioQuery query)
    {
        var discovered = await CreateScenarioCatalog().DiscoverAsync(query.Path).ConfigureAwait(false);
        var matched = ScenarioCatalog.Filter(discovered, query);
        var selected = ScenarioCatalog.SelectShard(matched, query);
        return new ScenarioSelection(discovered.Count, matched, selected);
    }

    private ScenarioCatalog CreateScenarioCatalog() =>
        new(_fileSystem, CreateTemplateResolver());

    private IScenarioTemplateResolver CreateTemplateResolver() =>
        new ScenarioTemplateResolver(_timeProvider, _environment);

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

    private static IReadOnlyList<string> SplitOption(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private sealed record ScenarioSelection(
        int TotalCount,
        IReadOnlyList<ScenarioCatalogEntry> Matched,
        IReadOnlyList<ScenarioCatalogEntry> Selected);
}
