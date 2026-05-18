namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioRunPlanner(ScenarioCatalog catalog)
{
    private readonly ScenarioCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public async Task<ScenarioListSelection> CreateListSelectionAsync(ScenarioQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var discovery = await DiscoverMatchedAsync(query).ConfigureAwait(false);
        return new ScenarioListSelection(query, discovery.TotalCount, discovery.MatchedScenarios);
    }

    public async Task<ScenarioRunPlan> CreateAsync(ScenarioQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var discovery = await DiscoverMatchedAsync(query).ConfigureAwait(false);
        var selected = ScenarioCatalog.SelectShard(discovery.MatchedScenarios, query);
        return new ScenarioRunPlan(query, discovery.TotalCount, discovery.MatchedScenarios, selected, discovery.MatchedScenarios.Count - selected.Count);
    }

    private async Task<ScenarioDiscovery> DiscoverMatchedAsync(ScenarioQuery query)
    {
        var discovered = await _catalog.DiscoverAsync(query.Path).ConfigureAwait(false);
        var matched = ScenarioCatalog.Filter(discovered, query);
        return new ScenarioDiscovery(discovered.Count, matched);
    }
}

internal sealed record ScenarioListSelection(
    ScenarioQuery Query,
    int TotalCount,
    IReadOnlyList<ScenarioCatalogEntry> MatchedScenarios)
{
    public int MatchedCount => MatchedScenarios.Count;
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

internal sealed record ScenarioDiscovery(int TotalCount, IReadOnlyList<ScenarioCatalogEntry> MatchedScenarios);