using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioRunReportCoordinatorFactory(IFileSystem fileSystem, TimeProvider timeProvider)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public ScenarioRunReportCoordinator Create(ScenarioRunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var writers = new List<IScenarioRunReportWriter>();
        if (!string.IsNullOrWhiteSpace(configuration.JsonReportPath))
        {
            writers.Add(new JsonScenarioRunReportWriter(_fileSystem, configuration.JsonReportPath));
        }

        if (!string.IsNullOrWhiteSpace(configuration.JUnitReportPath))
        {
            writers.Add(new JUnitScenarioRunReportWriter(_fileSystem, configuration.JUnitReportPath));
        }

        return new ScenarioRunReportCoordinator(
            _timeProvider,
            new CompositeScenarioRunReportWriter(writers),
            configuration.ArtifactAttachmentPolicy);
    }
}

internal sealed class ScenarioRunReportCoordinator(
    TimeProvider timeProvider,
    IScenarioRunReportWriter writer,
    ScenarioArtifactAttachmentPolicy attachmentPolicy)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IScenarioRunReportWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public async Task<ScenarioRunResult> RunFileAsync(string file, Func<Task<ScenarioRunResult>> runAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(runAsync);

        var startedAt = _timeProvider.GetUtcNow();
        try
        {
            var result = await runAsync().ConfigureAwait(false);
            await WriteAsync(ScenarioRunReportFactory.FromSingle(file, result, startedAt, _timeProvider.GetUtcNow(), attachmentPolicy)).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await WriteAsync(ScenarioRunReportFactory.FromSingleFailure(file, ex, startedAt, _timeProvider.GetUtcNow(), attachmentPolicy)).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ScenarioRunBatchResult> RunBatchAsync(ScenarioRunPlan plan, Func<Task<ScenarioRunBatchResult>> runAsync)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runAsync);

        var startedAt = _timeProvider.GetUtcNow();
        try
        {
            var result = await runAsync().ConfigureAwait(false);
            await WriteAsync(ScenarioRunReportFactory.FromBatch(result, startedAt, _timeProvider.GetUtcNow(), attachmentPolicy)).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await WriteAsync(ScenarioRunReportFactory.FromBatchFailure(plan, ex, startedAt, _timeProvider.GetUtcNow(), attachmentPolicy)).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ScenarioRunPlan> PlanPathAsync(ScenarioQuery query, Func<Task<ScenarioRunPlan>> planAsync)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(planAsync);

        var startedAt = _timeProvider.GetUtcNow();
        try
        {
            return await planAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteAsync(ScenarioRunReportFactory.FromQueryFailure(query, ex, startedAt, _timeProvider.GetUtcNow())).ConfigureAwait(false);
            throw;
        }
    }

    private Task WriteAsync(ScenarioRunReport report) => _writer.WriteAsync(report);
}
