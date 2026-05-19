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

    public ScenarioRunReportScope BeginScope() => new(_timeProvider.GetUtcNow());

    public Task WriteFileAsync(string file, ScenarioRunResult result, ScenarioRunReportScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(result);

        return WriteAsync(ScenarioRunReportFactory.FromSingle(file, result, scope.StartedAt, _timeProvider.GetUtcNow(), attachmentPolicy));
    }

    public Task WriteFileFailureAsync(string file, Exception exception, ScenarioRunReportScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(exception);

        return WriteAsync(ScenarioRunReportFactory.FromSingleFailure(file, exception, scope.StartedAt, _timeProvider.GetUtcNow(), attachmentPolicy));
    }

    public Task WriteBatchAsync(ScenarioRunBatchResult result, ScenarioRunReportScope scope)
    {
        ArgumentNullException.ThrowIfNull(result);

        return WriteAsync(ScenarioRunReportFactory.FromBatch(result, scope.StartedAt, _timeProvider.GetUtcNow(), attachmentPolicy));
    }

    public Task WriteBatchFailureAsync(ScenarioRunPlan plan, Exception exception, ScenarioRunReportScope scope)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(exception);

        return WriteAsync(ScenarioRunReportFactory.FromBatchFailure(plan, exception, scope.StartedAt, _timeProvider.GetUtcNow(), attachmentPolicy));
    }

    public Task WriteQueryFailureAsync(ScenarioQuery query, Exception exception, ScenarioRunReportScope scope)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(exception);

        return WriteAsync(ScenarioRunReportFactory.FromQueryFailure(query, exception, scope.StartedAt, _timeProvider.GetUtcNow()));
    }

    private Task WriteAsync(ScenarioRunReport report) => _writer.WriteAsync(report);
}

internal readonly record struct ScenarioRunReportScope(DateTimeOffset StartedAt);
