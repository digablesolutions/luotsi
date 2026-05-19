using System.Text.Json;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

public sealed record ScenarioCatalogEntry(
    string Id,
    string Name,
    string File,
    IReadOnlyList<string> Tags,
    int StepCount,
    IReadOnlyList<string> Actions);

public sealed record ScenarioListResult(
    string Path,
    int TotalCount,
    int MatchedCount,
    IReadOnlyList<ScenarioCatalogEntry> Scenarios);

public sealed record ScenarioRunPlanResult(
    string Path,
    bool DryRun,
    int TotalCount,
    int MatchedCount,
    int SelectedCount,
    int ShardedOutCount,
    int? ShardCount,
    int? ShardIndex,
    string ShardStrategy,
    IReadOnlyList<ScenarioCatalogEntry> Scenarios);

public sealed record ScenarioRunTiming(
    double TotalMs,
    double PrologueMs,
    double StepsMs,
    double NonStepMs);

public sealed record ScenarioStepTiming(
    double TotalMs,
    int HarnessDelayMs,
    int? ConfiguredDelayMs,
    double NonDelayMs);

public sealed record ScenarioStepResult(
    string Step,
    string Action,
    double DurationMs,
    ScenarioStepTiming Timing,
    object? Result = null,
    string? Status = null,
    ErrorInfo? Error = null);

public sealed record ScenarioFailedStepResult(
    int Index,
    string Name,
    string Action,
    double DurationMs,
    ScenarioStepTiming Timing);

public sealed record ScenarioRunResult(
    string Scenario,
    string Status,
    ScenarioRunTiming Timing,
    IReadOnlyList<ScenarioStepResult> Steps,
    string? ScenarioId = null,
    string? File = null);

public sealed record ScenarioRunFailureData(
    string Scenario,
    string File,
    string Status,
    ScenarioRunTiming Timing,
    ScenarioFailedStepResult FailedStep,
    IReadOnlyList<ScenarioStepResult> Steps,
    FailureArtifactBundle FailureArtifacts,
    string? ScenarioId = null);

public sealed record ScenarioBatchItemResult(
    string Scenario,
    string Status,
    ScenarioRunTiming? Timing = null,
    IReadOnlyList<ScenarioStepResult>? Steps = null,
    string? File = null,
    ScenarioRunFailureData? Data = null,
    ErrorInfo? Error = null,
    string? ScenarioId = null)
{
    public static ScenarioBatchItemResult FromSuccess(ScenarioRunResult result, ScenarioCatalogEntry? catalogEntry = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ScenarioBatchItemResult(
            result.Scenario,
            result.Status,
            result.Timing,
            result.Steps,
            result.File ?? catalogEntry?.File,
            ScenarioId: result.ScenarioId ?? catalogEntry?.Id);
    }

    public static ScenarioBatchItemResult FromFailure(string scenario, string file, ScenarioRunFailureData? data, ErrorInfo error, string? scenarioId = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ScenarioBatchItemResult(
            scenario,
            "failed",
            File: file,
            Data: data,
            Error: error,
            ScenarioId: scenarioId ?? data?.ScenarioId ?? ScenarioIdentity.Create(file, scenario));
    }
}

public sealed record ScenarioRunBatchResult(
    string Path,
    string Status,
    int TotalCount,
    int MatchedCount,
    int SelectedCount,
    int PassedCount,
    int FailedCount,
    int ShardedOutCount,
    int? ShardCount,
    int? ShardIndex,
    IReadOnlyList<ScenarioBatchItemResult> Scenarios,
    string ShardStrategy = ScenarioShardStrategies.Index);

public sealed record ScenarioQuery(
    string Path,
    IReadOnlyList<string> IncludeTags,
    IReadOnlyList<string> ExcludeTags,
    string? Name,
    string? Action,
    int? ShardCount,
    int? ShardIndex,
    bool DryRun,
    string ShardStrategy = ScenarioShardStrategies.Index);

public static class ScenarioShardStrategies
{
    public const string Index = "index";
    public const string Hash = "hash";
}

internal enum ScenarioFailureArtifactCapturePolicy
{
    Failure,
    Never
}

public static class ScenarioIdentity
{
    public static string Create(string file, string scenarioName) => $"{file}::{scenarioName}";
}

internal sealed class ScenarioCatalog(
    IFileSystem fileSystem,
    IScenarioTemplateResolver templateResolver)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IScenarioTemplateResolver _templateResolver = templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));

    public async Task<IReadOnlyList<ScenarioCatalogEntry>> DiscoverAsync(string path)
    {
        var files = ResolveScenarioFiles(path);
        var entries = new List<ScenarioCatalogEntry>(files.Length);

        foreach (var file in files)
        {
            var scenario = _templateResolver.ResolveScenario(await LoadAsync(file).ConfigureAwait(false));
            entries.Add(ToEntry(file, scenario));
        }

        return entries
            .OrderBy(static entry => entry.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<ScenarioFile> LoadValidatedAsync(string file, IReadOnlySet<string> supportedScenarioActions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(supportedScenarioActions);

        var scenario = _templateResolver.ResolveScenario(await LoadAsync(file).ConfigureAwait(false));
        return ScenarioValidator.ValidateScenario(scenario, file, supportedScenarioActions);
    }

    public static IReadOnlyList<ScenarioCatalogEntry> Filter(IReadOnlyList<ScenarioCatalogEntry> entries,
        ScenarioQuery query)
    {
        var filtered = query.IncludeTags.Aggregate<string?, IEnumerable<ScenarioCatalogEntry>>(entries,
            (current, includeTag) =>
                current.Where(entry => entry.Tags.Contains(includeTag, StringComparer.OrdinalIgnoreCase)));

        filtered = query.ExcludeTags.Aggregate(filtered,
            (current, excludeTag) =>
                current.Where(entry => !entry.Tags.Contains(excludeTag, StringComparer.OrdinalIgnoreCase)));

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            filtered = filtered.Where(entry => entry.Name.Contains(query.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            filtered = filtered.Where(entry => entry.Actions.Contains(query.Action, StringComparer.OrdinalIgnoreCase));
        }

        return filtered.ToArray();
    }

    public static IReadOnlyList<ScenarioCatalogEntry> SelectShard(IReadOnlyList<ScenarioCatalogEntry> entries,
        ScenarioQuery query)
    {
        if (query.ShardCount is null && query.ShardIndex is null)
        {
            return entries;
        }

        var shardCount = query.ShardCount ??
                         throw new UsageException("--shard-count is required when --shard-index is supplied.");
        var shardIndex = query.ShardIndex ??
                         throw new UsageException("--shard-index is required when --shard-count is supplied.");
        if (shardCount <= 0)
        {
            throw new UsageException("--shard-count must be greater than zero.");
        }

        if (shardIndex < 0 || shardIndex >= shardCount)
        {
            throw new UsageException("--shard-index must be zero or greater and less than --shard-count.");
        }

        return query.ShardStrategy.ToLowerInvariant() switch
        {
            ScenarioShardStrategies.Index => entries
                .Select((entry, index) => new {entry, index})
                .Where(item => item.index % shardCount == shardIndex)
                .Select(static item => item.entry)
                .ToArray(),
            ScenarioShardStrategies.Hash => entries
                .Where(entry => GetStableShardIndex(entry.Id, shardCount) == shardIndex)
                .ToArray(),
            _ => throw new UsageException("--shard-strategy must be one of: index, hash.")
        };
    }

    private string[] ResolveScenarioFiles(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new UsageException("Scenario path must be non-empty.");
        }

        if (_fileSystem.FileExists(path))
        {
            return [path];
        }

        if (_fileSystem.DirectoryExists(path))
        {
            return _fileSystem.GetFiles(path, "*.json", SearchOption.AllDirectories)
                .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (!path.Contains('*', StringComparison.Ordinal) && !path.Contains('?', StringComparison.Ordinal))
        {
            throw new UsageException($"Scenario path '{path}' does not exist.");
        }

        if (TrySplitRecursiveGlob(path, out var recursiveRoot, out var recursivePattern))
        {
            if (!_fileSystem.DirectoryExists(recursiveRoot))
            {
                throw new UsageException($"Scenario path '{path}' does not exist.");
            }

            return _fileSystem.GetFiles(recursiveRoot, recursivePattern, SearchOption.AllDirectories)
                .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var directory = Path.GetDirectoryName(path);

        var searchRoot = string.IsNullOrWhiteSpace(directory) ? "." : directory;

        var pattern = Path.GetFileName(path);

        if (!_fileSystem.DirectoryExists(searchRoot))
        {
            throw new UsageException($"Scenario path '{path}' does not exist.");
        }

        return _fileSystem.GetFiles(searchRoot, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TrySplitRecursiveGlob(string path, out string root, out string pattern)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("**/", StringComparison.Ordinal))
        {
            root = ".";
            pattern = Path.GetFileName(normalized[3..]);
            if (string.IsNullOrWhiteSpace(pattern))
            {
                pattern = "*.json";
            }

            return true;
        }

        var markerIndex = normalized.IndexOf("/**/", StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            root = string.Empty;
            pattern = string.Empty;
            return false;
        }

        root = markerIndex == 0 ? "." : path[..markerIndex];
        var remainder = normalized[(markerIndex + 4)..];
        pattern = Path.GetFileName(remainder);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            pattern = "*.json";
        }

        return true;
    }

    private async Task<ScenarioFile> LoadAsync(string file)
    {
        if (!_fileSystem.FileExists(file))
        {
            throw new UsageException($"Scenario file '{file}' does not exist.");
        }

        try
        {
            await using var stream = _fileSystem.OpenRead(file);
            var scenario = await JsonSerializer.DeserializeAsync<ScenarioFile>(stream, AppJson.Options).ConfigureAwait(false);
            return scenario ?? throw new UsageException($"Scenario file '{file}' was empty.");
        }
        catch (JsonException ex)
        {
            throw new UsageException($"Scenario file '{file}' is not valid JSON: {ex.Message}");
        }
    }

    private static ScenarioCatalogEntry ToEntry(string file, ScenarioFile scenario)
    {
        var tags = scenario.Tags?
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        var actions = scenario.Steps
            .Select(static step => step.Action)
            .Where(static action => !string.IsNullOrWhiteSpace(action))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static action => action, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ScenarioCatalogEntry(
            ScenarioIdentity.Create(file, scenario.Name),
            scenario.Name,
            file,
            tags,
            scenario.Steps.Count,
            actions);
    }

    private static int GetStableShardIndex(string value, int shardCount)
    {
        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            var hash = offsetBasis;
            foreach (var character in value)
            {
                hash ^= char.ToUpperInvariant(character);
                hash *= prime;
            }

            return (int)(hash % (uint)shardCount);
        }
    }
}
