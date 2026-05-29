using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class ScenarioCommandDispatcher(
    ScenarioRunPlanner runPlanner,
    ScenarioRunOrchestrator scenarioRunOrchestrator,
    ScenarioAuthoringService authoringService,
    IEnvironmentVariables environment,
    LabLeaseStore? labLeaseStore = null)
{
    private readonly ScenarioRunPlanner _runPlanner = runPlanner ?? throw new ArgumentNullException(nameof(runPlanner));
    private readonly ScenarioRunOrchestrator _scenarioRunOrchestrator = scenarioRunOrchestrator ?? throw new ArgumentNullException(nameof(scenarioRunOrchestrator));
    private readonly ScenarioAuthoringService _authoringService = authoringService ?? throw new ArgumentNullException(nameof(authoringService));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly LabLeaseStore? _labLeaseStore = labLeaseStore;

    public async Task<ScenarioListResult> ListAsync(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var query = ScenarioQueryFactory.CreateListQuery(options);
        var selection = await _runPlanner.CreateListSelectionAsync(query).ConfigureAwait(false);
        return new ScenarioListResult(selection.Query.Path, selection.TotalCount, selection.MatchedCount, selection.MatchedScenarios);
    }

    public Task<ScenarioInitResult> InitAsync(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _authoringService.InitAsync(options);
    }

    public Task<ScenarioExplainResult> ExplainAsync(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _authoringService.ExplainAsync(options);
    }

    public static bool RequiresRunner(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return !options.HasFlag("validate-only") && !options.HasFlag("dry-run");
    }

    public Task<object> ValidateAsync(CliOptions options, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyDefaults(new Dictionary<string, string?> { ["validate-only"] = "true" });
        return RunAsync(options, null, artifacts);
    }

    public async Task<object> RunAsync(CliOptions options, IDeviceHost? runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(options);

        var configuration = ScenarioRunConfiguration.Create(options, _environment);

        if (!ScenarioQueryFactory.UsesCatalogExecution(options))
        {
            if (options.HasFlag("dry-run"))
            {
                throw new UsageException("run --dry-run requires --path. Use run --file <scenario.json> without --dry-run for single-scenario execution.");
            }

            var file = options.Require("file");
            if (configuration.ValidateOnly)
            {
                return await _scenarioRunOrchestrator.ValidateFileAsync(file, configuration).ConfigureAwait(false);
            }

            return await RunWithOptionalClaimAsync(options, configuration, claimedConfiguration => _scenarioRunOrchestrator.RunFileAsync(file, RequireRunner(runner), claimedConfiguration, artifacts)).ConfigureAwait(false);
        }

        var query = ScenarioQueryFactory.CreateCatalogRunQuery(options);
        if (configuration.ValidateOnly && query.DryRun)
        {
            throw new UsageException("Use either --validate-only or --dry-run, not both.");
        }

        if (query.DryRun)
        {
            var plan = await _runPlanner.CreateAsync(query).ConfigureAwait(false);
            return new ScenarioRunPlanResult(
                query.Path,
                true,
                plan.TotalCount,
                plan.MatchedCount,
                plan.SelectedCount,
                plan.ShardedOutCount,
                query.ShardCount,
                query.ShardIndex,
                query.ShardStrategy,
                plan.SelectedScenarios);
        }

        if (configuration.ValidateOnly)
        {
            return await _scenarioRunOrchestrator.ValidatePathAsync(query, configuration).ConfigureAwait(false);
        }

        return await RunWithOptionalClaimAsync(options, configuration, claimedConfiguration => _scenarioRunOrchestrator.RunPathAsync(query, RequireRunner(runner), claimedConfiguration, artifacts)).ConfigureAwait(false);
    }

    private async Task<T> RunWithOptionalClaimAsync<T>(
        CliOptions options,
        ScenarioRunConfiguration configuration,
        Func<ScenarioRunConfiguration, Task<T>> runAsync)
    {
        if (!options.HasFlag("claim-device"))
        {
            return await runAsync(configuration).ConfigureAwait(false);
        }

        if (_labLeaseStore is null)
        {
            throw new InvalidOperationException("Scenario device claiming is not available in this command host.");
        }

        var serial = options.Get("device");
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new UsageException("run --claim-device requires --device or --device-query so Luotsi can claim the selected serial.");
        }

        var lease = await _labLeaseStore.ClaimAsync(serial, options.Get("owner") ?? "luotsi-run", options.Int("ttl-sec", 3600)).ConfigureAwait(false);
        try
        {
            return await runAsync(configuration with { LabLease = lease }).ConfigureAwait(false);
        }
        finally
        {
            await _labLeaseStore.ReleaseAsync(lease.LeaseId).ConfigureAwait(false);
        }
    }

    private static IDeviceHost RequireRunner(IDeviceHost? runner) =>
        runner ?? throw new InvalidOperationException("Scenario execution requires a device host.");
}
