using System.Text.Json;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Infrastructure.System;
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
    IReadOnlyList<ScenarioCatalogEntry> Scenarios);

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
    IReadOnlyList<object> Scenarios);

public sealed record ScenarioQuery(
    string Path,
    IReadOnlyList<string> IncludeTags,
    IReadOnlyList<string> ExcludeTags,
    string? Name,
    string? Action,
    int? ShardCount,
    int? ShardIndex,
    bool DryRun);

internal sealed class ScenarioCatalog(
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    IEnvironmentVariables? environment = null)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    private readonly IScenarioTemplateResolver _templateResolver = new ScenarioTemplateResolver(
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)),
        environment ?? new SystemEnvironmentVariables());

    internal ScenarioCatalog(IFileSystem fileSystem, IScenarioTemplateResolver templateResolver)
        : this(fileSystem, TimeProvider.System, new SystemEnvironmentVariables())
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _templateResolver = templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));
    }

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

        return entries
            .Select((entry, index) => new {entry, index})
            .Where(item => item.index % shardCount == shardIndex)
            .Select(static item => item.entry)
            .ToArray();
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

    private async Task<ScenarioFile> LoadAsync(string file)
    {
        try
        {
            var text = await _fileSystem.ReadAllTextAsync(file).ConfigureAwait(false);
            var scenario = JsonSerializer.Deserialize<ScenarioFile>(text, AppJson.Options);
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
            $"{file}::{scenario.Name}",
            scenario.Name,
            file,
            tags,
            scenario.Steps.Count,
            actions);
    }
}
