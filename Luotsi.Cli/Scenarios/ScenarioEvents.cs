using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal interface IScenarioEventSink : IAsyncDisposable
{
    Task EmitAsync(ScenarioEvent scenarioEvent);
}

internal sealed class NullScenarioEventSink : IScenarioEventSink
{
    public static readonly NullScenarioEventSink Instance = new();

    private NullScenarioEventSink()
    {
    }

    public Task EmitAsync(ScenarioEvent scenarioEvent) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class JsonlScenarioEventSink : IScenarioEventSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly byte[] NewLineBytes = Encoding.UTF8.GetBytes(Environment.NewLine);

    private readonly Stream _stream;

    public JsonlScenarioEventSink(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Event JSONL path must be non-empty.", nameof(path));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            fileSystem.CreateDirectory(directory);
        }

        _stream = fileSystem.OpenWrite(path);
    }

    public async Task EmitAsync(ScenarioEvent scenarioEvent)
    {
        ArgumentNullException.ThrowIfNull(scenarioEvent);

        await JsonSerializer.SerializeAsync(_stream, scenarioEvent, JsonOptions).ConfigureAwait(false);
        await _stream.WriteAsync(NewLineBytes).ConfigureAwait(false);
        await _stream.FlushAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await _stream.DisposeAsync().ConfigureAwait(false);
}

internal sealed class CompositeScenarioEventSink(IReadOnlyList<IScenarioEventSink> sinks) : IScenarioEventSink
{
    private readonly IReadOnlyList<IScenarioEventSink> _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));

    public async Task EmitAsync(ScenarioEvent scenarioEvent)
    {
        foreach (var sink in _sinks)
        {
            await sink.EmitAsync(scenarioEvent).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sink in _sinks)
        {
            await sink.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal sealed class ScenarioReplayEventSink(IScenarioEventSink innerSink, SessionReplayArtifacts replayArtifacts) : IScenarioEventSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IScenarioEventSink _innerSink = innerSink ?? throw new ArgumentNullException(nameof(innerSink));
    private readonly SessionReplayArtifacts _replayArtifacts = replayArtifacts ?? throw new ArgumentNullException(nameof(replayArtifacts));

    public async Task EmitAsync(ScenarioEvent scenarioEvent)
    {
        ArgumentNullException.ThrowIfNull(scenarioEvent);

        await _innerSink.EmitAsync(scenarioEvent).ConfigureAwait(false);
        _replayArtifacts.RecordSerializedEvent(JsonSerializer.Serialize(ScenarioReplayTimelineEvent.FromScenarioEvent(scenarioEvent), JsonOptions));
    }

    public async ValueTask DisposeAsync() => await _innerSink.DisposeAsync().ConfigureAwait(false);
}

internal sealed class ScenarioRunEventCoordinatorFactory(IFileSystem fileSystem, TimeProvider timeProvider, BuildProvenance provenance, IConsoleIo console)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly BuildProvenance _provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));

    public ScenarioRunEventCoordinator Create(
        string? path,
        ArtifactSession? replayArtifacts = null,
        string? replayTarget = null,
        ScenarioProgressMode progressMode = ScenarioProgressMode.Plain)
    {
        var sinks = new List<IScenarioEventSink>();
        if (!string.IsNullOrWhiteSpace(path))
        {
            sinks.Add(new JsonlScenarioEventSink(_fileSystem, path));
        }

        if (progressMode != ScenarioProgressMode.Quiet)
        {
            sinks.Add(new ConsoleScenarioProgressEventSink(_console, progressMode));
        }

        IScenarioEventSink sink = sinks.Count switch
        {
            0 => NullScenarioEventSink.Instance,
            1 => sinks[0],
            _ => new CompositeScenarioEventSink(sinks)
        };

        SessionReplayArtifacts? replay = null;
        if (replayArtifacts is not null)
        {
            var startedAt = _timeProvider.GetUtcNow();
            replay = new SessionReplayArtifacts(replayArtifacts, "run", $"run-{startedAt:yyyyMMddHHmmssfff}", startedAt);
            replay.SetTarget(replayTarget);
            sink = new ScenarioReplayEventSink(sink, replay);
        }

        return new ScenarioRunEventCoordinator(_timeProvider, sink, _provenance, replay);
    }
}

internal sealed class ScenarioRunEventCoordinator(TimeProvider timeProvider, IScenarioEventSink eventSink, BuildProvenance provenance, SessionReplayArtifacts? replayArtifacts = null) : IAsyncDisposable
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IScenarioEventSink _eventSink = eventSink ?? throw new ArgumentNullException(nameof(eventSink));
    private readonly BuildProvenance _provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));

    public async Task<ScenarioRunResult> RunFileAsync(string file, Func<IScenarioEventSink, Task<ScenarioRunResult>> runAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(runAsync);

        return await RunAsync(
            new ScenarioEvent("scenario_run_started", _timeProvider.GetUtcNow(), Path: file, Provenance: _provenance),
            runAsync,
            result => new ScenarioEvent(
                "scenario_run_ended",
                _timeProvider.GetUtcNow(),
                result.Status,
                Path: file,
                PassedCount: IsPassed(result.Status) ? 1 : 0,
                FailedCount: IsFailed(result.Status) ? 1 : 0,
                Metrics: result.Metrics,
                DeviceAllocation: result.DeviceAllocation,
                Provenance: _provenance,
                Governance: result.Governance,
                DeviceHealth: result.DeviceHealth,
                CiPolicy: result.CiPolicy),
            ex => CreateFailedRunEndedEvent(file, ex, passedCount: 0, failedCount: 1)).ConfigureAwait(false);
    }

    public async Task<ScenarioRunBatchResult> RunBatchAsync(ScenarioRunPlan plan, Func<IScenarioEventSink, Task<ScenarioRunBatchResult>> runAsync)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runAsync);

        return await RunAsync(
            new ScenarioEvent(
                "scenario_run_started",
                _timeProvider.GetUtcNow(),
                Path: plan.Query.Path,
                TotalCount: plan.TotalCount,
                MatchedCount: plan.MatchedCount,
                SelectedCount: plan.SelectedCount,
                ShardedOutCount: plan.ShardedOutCount,
                ShardCount: plan.Query.ShardCount,
                ShardIndex: plan.Query.ShardIndex,
                ShardStrategy: plan.Query.ShardStrategy,
                Provenance: _provenance),
            runAsync,
            result => new ScenarioEvent(
                "scenario_run_ended",
                _timeProvider.GetUtcNow(),
                result.Status,
                Path: result.Path,
                TotalCount: result.TotalCount,
                MatchedCount: result.MatchedCount,
                SelectedCount: result.SelectedCount,
                PassedCount: result.PassedCount,
                FailedCount: result.FailedCount,
                ShardedOutCount: result.ShardedOutCount,
                ShardCount: result.ShardCount,
                ShardIndex: result.ShardIndex,
                ShardStrategy: result.ShardStrategy,
                Metrics: result.Metrics,
                DeviceAllocation: result.DeviceAllocation,
                Provenance: _provenance,
                Governance: result.Governance,
                DeviceHealth: result.DeviceHealth,
                CiPolicy: result.CiPolicy),
            ex => CreateFailedRunEndedEvent(
                plan.Query.Path,
                ex,
                totalCount: plan.TotalCount,
                matchedCount: plan.MatchedCount,
                selectedCount: plan.SelectedCount,
                shardedOutCount: plan.ShardedOutCount,
                shardCount: plan.Query.ShardCount,
                shardIndex: plan.Query.ShardIndex,
                shardStrategy: plan.Query.ShardStrategy)).ConfigureAwait(false);
    }

    public async Task<ScenarioRunPlan> PlanPathAsync(
        ScenarioQuery query,
        Func<Task<ScenarioRunPlan>> planAsync)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(planAsync);

        try
        {
            return await planAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var started = new ScenarioEvent(
                "scenario_run_started",
                _timeProvider.GetUtcNow(),
                Path: query.Path,
                ShardCount: query.ShardCount,
                ShardIndex: query.ShardIndex,
                ShardStrategy: query.ShardStrategy,
                Provenance: _provenance);
            await _eventSink.EmitAsync(started).ConfigureAwait(false);
            var ended = CreateFailedRunEndedEvent(
                query.Path,
                ex,
                shardCount: query.ShardCount,
                shardIndex: query.ShardIndex,
                shardStrategy: query.ShardStrategy);
            await _eventSink.EmitAsync(ended).ConfigureAwait(false);
            await PersistReplayAsync(ended.Timestamp, "error", ResolveExceptionExitCode(ex)).ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync() => _eventSink.DisposeAsync();

    private static bool IsPassed(string status) =>
        string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailed(string status) =>
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);

    private async Task<TResult> RunAsync<TResult>(
        ScenarioEvent started,
        Func<IScenarioEventSink, Task<TResult>> runAsync,
        Func<TResult, ScenarioEvent> createEnded,
        Func<Exception, ScenarioEvent> createFailedEnded)
    {
        await _eventSink.EmitAsync(started).ConfigureAwait(false);
        try
        {
            var result = await runAsync(_eventSink).ConfigureAwait(false);
            var ended = createEnded(result);
            await _eventSink.EmitAsync(ended).ConfigureAwait(false);
            await PersistReplayAsync(ended.Timestamp, ended.Status ?? "completed", ResolveResultExitCode(result)).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            var ended = createFailedEnded(ex);
            await _eventSink.EmitAsync(ended).ConfigureAwait(false);
            await PersistReplayAsync(ended.Timestamp, "error", ResolveExceptionExitCode(ex)).ConfigureAwait(false);
            throw;
        }
    }

    private Task PersistReplayAsync(DateTimeOffset endedAt, string reason, int exitCode) =>
        replayArtifacts?.PersistAsync(endedAt, reason, exitCode) ?? Task.CompletedTask;

    private static int ResolveResultExitCode<TResult>(TResult result) =>
        AppCommandExitCodeResolver.Resolve(result!);

    private static int ResolveExceptionExitCode(Exception exception) =>
        exception switch
        {
            UsageException => 2,
            _ when ScenarioFailureDetails.TryGetData(exception) is ScenarioRunFailureData { CiPolicy: { ExitCodeApplied: true } ciPolicy } => ciPolicy.RecommendedExitCode,
            _ => 1
        };

    private ScenarioEvent CreateFailedRunEndedEvent(
        string path,
        Exception exception,
        int? totalCount = null,
        int? matchedCount = null,
        int? selectedCount = null,
        int? passedCount = null,
        int? failedCount = null,
        int? shardedOutCount = null,
        int? shardCount = null,
        int? shardIndex = null,
        string? shardStrategy = null) =>
        new(
            "scenario_run_ended",
            _timeProvider.GetUtcNow(),
            "failed",
            Path: path,
            TotalCount: totalCount,
            MatchedCount: matchedCount,
            SelectedCount: selectedCount,
            PassedCount: passedCount,
            FailedCount: failedCount,
            ShardedOutCount: shardedOutCount,
            ShardCount: shardCount,
            ShardIndex: shardIndex,
            ShardStrategy: shardStrategy,
            Metrics: ScenarioFailureDetails.TryGetMetrics(exception),
            DeviceAllocation: ScenarioFailureDetails.TryGetDeviceAllocation(exception),
            Provenance: _provenance,
            Error: ScenarioErrorInfo.From(exception),
            Governance: ScenarioGovernanceClassifier.FromException(exception),
            DeviceHealth: ScenarioFailureDetails.TryGetData(exception)?.DeviceHealth,
            CiPolicy: ScenarioFailureDetails.TryGetData(exception)?.CiPolicy);
}

internal readonly record struct ScenarioLifecycleContext(
    string File,
    string ScenarioId,
    string ScenarioName,
    DateTimeOffset StartedAt);

internal readonly record struct ScenarioLifecycleCompletion(
    ScenarioRunResult Result,
    DateTimeOffset? EndedAt = null);

internal sealed class ScenarioLifecycleCoordinator(TimeProvider timeProvider, IScenarioEventSink eventSink)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IScenarioEventSink _eventSink = eventSink ?? throw new ArgumentNullException(nameof(eventSink));

    public async Task<ScenarioRunResult> RunAsync(
        ScenarioLifecycleContext context,
        string? startedStatus,
        Func<ScenarioLifecycleContext, Task<ScenarioLifecycleCompletion>> runAsync)
    {
        ArgumentNullException.ThrowIfNull(runAsync);

        await _eventSink.EmitAsync(new ScenarioEvent(
            "scenario_started",
            context.StartedAt,
            startedStatus,
            File: context.File,
            ScenarioId: context.ScenarioId,
            Scenario: context.ScenarioName)).ConfigureAwait(false);

        try
        {
            var completion = await runAsync(context).ConfigureAwait(false);
            var result = completion.Result;
            var endedAt = completion.EndedAt ?? _timeProvider.GetUtcNow();
            await _eventSink.EmitAsync(new ScenarioEvent(
                "scenario_ended",
                endedAt,
                result.Status,
                File: context.File,
                ScenarioId: context.ScenarioId,
                Scenario: context.ScenarioName,
                DurationMs: Math.Max(0, (endedAt - context.StartedAt).TotalMilliseconds),
                Metrics: result.Metrics,
                Governance: result.Governance,
                DeviceHealth: result.DeviceHealth,
                CiPolicy: result.CiPolicy)).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            var failureData = ScenarioFailureDetails.TryGetData(ex);
            var endedAt = _timeProvider.GetUtcNow();
            await _eventSink.EmitAsync(new ScenarioEvent(
                "scenario_ended",
                endedAt,
                "failed",
                File: context.File,
                ScenarioId: context.ScenarioId,
                Scenario: context.ScenarioName,
                DurationMs: Math.Max(0, (endedAt - context.StartedAt).TotalMilliseconds),
                Metrics: ScenarioFailureDetails.TryGetMetrics(ex),
                Governance: ScenarioGovernanceClassifier.FromException(ex),
                DeviceHealth: failureData?.DeviceHealth,
                CiPolicy: failureData?.CiPolicy)).ConfigureAwait(false);
            throw;
        }
    }
}

internal sealed record ScenarioEvent(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("path")] string? Path = null,
    [property: JsonPropertyName("file")] string? File = null,
    [property: JsonPropertyName("scenario_id")] string? ScenarioId = null,
    [property: JsonPropertyName("scenario")] string? Scenario = null,
    [property: JsonPropertyName("phase")] string? Phase = null,
    [property: JsonPropertyName("step_index")] int? StepIndex = null,
    [property: JsonPropertyName("step")] string? Step = null,
    [property: JsonPropertyName("action")] string? Action = null,
    [property: JsonPropertyName("duration_ms")] double? DurationMs = null,
    [property: JsonPropertyName("total_count")] int? TotalCount = null,
    [property: JsonPropertyName("matched_count")] int? MatchedCount = null,
    [property: JsonPropertyName("selected_count")] int? SelectedCount = null,
    [property: JsonPropertyName("passed_count")] int? PassedCount = null,
    [property: JsonPropertyName("failed_count")] int? FailedCount = null,
    [property: JsonPropertyName("sharded_out_count")] int? ShardedOutCount = null,
    [property: JsonPropertyName("shard_count")] int? ShardCount = null,
    [property: JsonPropertyName("shard_index")] int? ShardIndex = null,
    [property: JsonPropertyName("shard_strategy")] string? ShardStrategy = null,
    [property: JsonPropertyName("metrics")] IReadOnlyDictionary<string, double>? Metrics = null,
    [property: JsonPropertyName("device_allocation")] ScenarioDeviceAllocation? DeviceAllocation = null,
    [property: JsonPropertyName("provenance")] BuildProvenance? Provenance = null,
    [property: JsonPropertyName("error")] object? Error = null,
    [property: JsonPropertyName("governance")] ScenarioGovernanceVerdict? Governance = null,
    [property: JsonPropertyName("device_health")] ScenarioDeviceHealthSnapshot? DeviceHealth = null,
    [property: JsonPropertyName("ci_policy")] ScenarioCiPolicyResult? CiPolicy = null);

internal sealed record ScenarioReplayTimelineEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("path")] string? Path = null,
    [property: JsonPropertyName("file")] string? File = null,
    [property: JsonPropertyName("scenario_id")] string? ScenarioId = null,
    [property: JsonPropertyName("scenario")] string? Scenario = null,
    [property: JsonPropertyName("phase")] string? Phase = null,
    [property: JsonPropertyName("step_index")] int? StepIndex = null,
    [property: JsonPropertyName("step")] string? Step = null,
    [property: JsonPropertyName("action")] string? Action = null,
    [property: JsonPropertyName("duration_ms")] double? DurationMs = null,
    [property: JsonPropertyName("total_count")] int? TotalCount = null,
    [property: JsonPropertyName("matched_count")] int? MatchedCount = null,
    [property: JsonPropertyName("selected_count")] int? SelectedCount = null,
    [property: JsonPropertyName("passed_count")] int? PassedCount = null,
    [property: JsonPropertyName("failed_count")] int? FailedCount = null,
    [property: JsonPropertyName("sharded_out_count")] int? ShardedOutCount = null,
    [property: JsonPropertyName("shard_count")] int? ShardCount = null,
    [property: JsonPropertyName("shard_index")] int? ShardIndex = null,
    [property: JsonPropertyName("shard_strategy")] string? ShardStrategy = null,
    [property: JsonPropertyName("metrics")] IReadOnlyDictionary<string, double>? Metrics = null,
    [property: JsonPropertyName("device_allocation")] ScenarioDeviceAllocation? DeviceAllocation = null,
    [property: JsonPropertyName("provenance")] BuildProvenance? Provenance = null,
    [property: JsonPropertyName("error")] object? Error = null,
    [property: JsonPropertyName("governance")] ScenarioGovernanceVerdict? Governance = null,
    [property: JsonPropertyName("device_health")] ScenarioDeviceHealthSnapshot? DeviceHealth = null,
    [property: JsonPropertyName("ci_policy")] ScenarioCiPolicyResult? CiPolicy = null)
{
    public static ScenarioReplayTimelineEvent FromScenarioEvent(ScenarioEvent scenarioEvent)
    {
        ArgumentNullException.ThrowIfNull(scenarioEvent);

        return new ScenarioReplayTimelineEvent(
            scenarioEvent.Event,
            scenarioEvent.Timestamp,
            scenarioEvent.Status,
            scenarioEvent.Path,
            scenarioEvent.File,
            scenarioEvent.ScenarioId,
            scenarioEvent.Scenario,
            scenarioEvent.Phase,
            scenarioEvent.StepIndex,
            scenarioEvent.Step,
            scenarioEvent.Action,
            scenarioEvent.DurationMs,
            scenarioEvent.TotalCount,
            scenarioEvent.MatchedCount,
            scenarioEvent.SelectedCount,
            scenarioEvent.PassedCount,
            scenarioEvent.FailedCount,
            scenarioEvent.ShardedOutCount,
            scenarioEvent.ShardCount,
            scenarioEvent.ShardIndex,
            scenarioEvent.ShardStrategy,
            scenarioEvent.Metrics,
            scenarioEvent.DeviceAllocation,
            scenarioEvent.Provenance,
            scenarioEvent.Error,
            scenarioEvent.Governance,
            scenarioEvent.DeviceHealth,
            scenarioEvent.CiPolicy);
    }
}
