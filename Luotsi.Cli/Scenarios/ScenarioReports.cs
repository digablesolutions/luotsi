using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal sealed class ScenarioRunReportCoordinatorFactory(IFileSystem fileSystem, TimeProvider timeProvider, BuildProvenance provenance)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly BuildProvenance _provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));

    public ScenarioRunReportCoordinator Create(ScenarioRunConfiguration configuration, ArtifactSession? artifacts = null)
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
            configuration.ArtifactAttachmentPolicy,
            _provenance,
            artifacts is null ? null : new ScenarioFailureCapsuleWriter(_fileSystem, artifacts),
            configuration.JsonReportPath,
            configuration.JUnitReportPath,
            FormatProgressMode(configuration.ProgressMode));
    }

    private static string FormatProgressMode(ScenarioProgressMode progressMode) =>
        progressMode.ToString().ToLowerInvariant();
}

internal sealed class ScenarioRunReportCoordinator(
    TimeProvider timeProvider,
    IScenarioRunReportWriter writer,
    ScenarioArtifactAttachmentPolicy attachmentPolicy,
    BuildProvenance provenance,
    ScenarioFailureCapsuleWriter? failureCapsuleWriter = null,
    string? jsonReportPath = null,
    string? junitReportPath = null,
    string? progressMode = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IScenarioRunReportWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly BuildProvenance _provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
    private readonly ScenarioFailureCapsuleWriter? _failureCapsuleWriter = failureCapsuleWriter;
    private readonly string? _jsonReportPath = jsonReportPath;
    private readonly string? _junitReportPath = junitReportPath;
    private readonly string? _progressMode = progressMode;

    public ScenarioRunReportScope BeginScope() => new(_timeProvider.GetUtcNow());

    public Task WriteFileAsync(string file, ScenarioRunResult result, ScenarioRunReportScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(result);

        return WriteAsync(ScenarioRunReportFactory.FromSingle(file, result, scope.StartedAt, _timeProvider.GetUtcNow(), attachmentPolicy, _provenance, _progressMode));
    }

    public Task WriteFileFailureAsync(string file, Exception exception, ScenarioRunReportScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(exception);

        return WriteAsync(ScenarioRunReportFactory.FromSingleFailure(file, exception, scope.StartedAt, _timeProvider.GetUtcNow(), attachmentPolicy, _provenance, _progressMode));
    }

    public Task WriteBatchAsync(ScenarioRunBatchResult result, ScenarioRunReportScope scope)
    {
        ArgumentNullException.ThrowIfNull(result);

        return WriteAsync(ScenarioRunReportFactory.FromBatch(result, scope.StartedAt, _timeProvider.GetUtcNow(), attachmentPolicy, _provenance, _progressMode));
    }

    public Task WriteBatchFailureAsync(ScenarioRunPlan plan, Exception exception, ScenarioRunReportScope scope)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(exception);

        return WriteAsync(ScenarioRunReportFactory.FromBatchFailure(plan, exception, scope.StartedAt, _timeProvider.GetUtcNow(), attachmentPolicy, _provenance, _progressMode));
    }

    public Task WriteQueryFailureAsync(ScenarioQuery query, Exception exception, ScenarioRunReportScope scope)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(exception);

        return WriteAsync(ScenarioRunReportFactory.FromQueryFailure(query, exception, scope.StartedAt, _timeProvider.GetUtcNow(), _provenance, _progressMode));
    }

    private async Task WriteAsync(ScenarioRunReport report)
    {
        await _writer.WriteAsync(report).ConfigureAwait(false);
        if (_failureCapsuleWriter is not null)
        {
            await _failureCapsuleWriter.WriteAsync(report, _jsonReportPath, _junitReportPath).ConfigureAwait(false);
        }
    }
}

internal readonly record struct ScenarioRunReportScope(DateTimeOffset StartedAt);
