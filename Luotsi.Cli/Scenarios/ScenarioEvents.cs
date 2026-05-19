using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Infrastructure.Contracts;

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

internal sealed class ScenarioRunEventCoordinatorFactory(IFileSystem fileSystem, TimeProvider timeProvider)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public ScenarioRunEventCoordinator Create(string? path)
    {
        IScenarioEventSink sink = string.IsNullOrWhiteSpace(path)
            ? NullScenarioEventSink.Instance
            : new JsonlScenarioEventSink(_fileSystem, path);
        return new ScenarioRunEventCoordinator(_timeProvider, sink);
    }
}

internal sealed class ScenarioRunEventCoordinator(TimeProvider timeProvider, IScenarioEventSink eventSink) : IAsyncDisposable
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IScenarioEventSink _eventSink = eventSink ?? throw new ArgumentNullException(nameof(eventSink));

    public async Task<ScenarioRunResult> RunFileAsync(string file, Func<IScenarioEventSink, Task<ScenarioRunResult>> runAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(runAsync);

        return await RunAsync(
            new ScenarioEvent("scenario_run_started", _timeProvider.GetUtcNow(), Path: file),
            runAsync,
            result => new ScenarioEvent(
                "scenario_run_ended",
                _timeProvider.GetUtcNow(),
                result.Status,
                Path: file,
                PassedCount: result.Status == "passed" ? 1 : 0,
                FailedCount: result.Status == "passed" ? 0 : 1,
                Metrics: result.Metrics),
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
                ShardStrategy: plan.Query.ShardStrategy),
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
                Metrics: result.Metrics),
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

    public async Task<ScenarioRunBatchResult> RunPathAsync(
        ScenarioQuery query,
        Func<IScenarioEventSink, Task<ScenarioRunPlan>> planAsync,
        Func<ScenarioRunPlan, IScenarioEventSink, Task<ScenarioRunBatchResult>> runAsync)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(planAsync);
        ArgumentNullException.ThrowIfNull(runAsync);

        ScenarioRunPlan plan;
        try
        {
            plan = await planAsync(_eventSink).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _eventSink.EmitAsync(new ScenarioEvent(
                "scenario_run_started",
                _timeProvider.GetUtcNow(),
                Path: query.Path,
                ShardCount: query.ShardCount,
                ShardIndex: query.ShardIndex,
                ShardStrategy: query.ShardStrategy)).ConfigureAwait(false);
            await _eventSink.EmitAsync(CreateFailedRunEndedEvent(
                query.Path,
                ex,
                shardCount: query.ShardCount,
                shardIndex: query.ShardIndex,
                shardStrategy: query.ShardStrategy)).ConfigureAwait(false);
            throw;
        }

        return await RunBatchAsync(plan, sink => runAsync(plan, sink)).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _eventSink.DisposeAsync();

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
            await _eventSink.EmitAsync(createEnded(result)).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await _eventSink.EmitAsync(createFailedEnded(ex)).ConfigureAwait(false);
            throw;
        }
    }

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
            Error: ScenarioErrorInfo.From(exception));
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
    [property: JsonPropertyName("error")] object? Error = null);
