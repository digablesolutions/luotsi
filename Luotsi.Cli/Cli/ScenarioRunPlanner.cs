using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli;

internal sealed class ScenarioRunPlanner(ScenarioCatalog catalog)
{
    private readonly ScenarioCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public async Task<ScenarioRunPlan> CreateAsync(ScenarioQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var discovered = await _catalog.DiscoverAsync(query.Path).ConfigureAwait(false);
        var matched = ScenarioCatalog.Filter(discovered, query);
        var selected = ScenarioCatalog.SelectShard(matched, query);
        return new ScenarioRunPlan(query, discovered.Count, matched, selected, matched.Count - selected.Count);
    }
}

internal sealed record ScenarioRunPlan(
    ScenarioQuery Query,
    int TotalCount,
    IReadOnlyList<ScenarioCatalogEntry> MatchedScenarios,
    IReadOnlyList<ScenarioCatalogEntry> SelectedScenarios,
    int ShardedOutCount)
{
    public int MatchedCount => MatchedScenarios.Count;

    public int SelectedCount => SelectedScenarios.Count;
}
