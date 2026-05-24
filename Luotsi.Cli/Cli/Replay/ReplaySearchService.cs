using System.Text;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal sealed class ReplaySearchService(IFileSystem fileSystem)
{
    private const int DefaultLimit = 50;
    private const int MaxPreviewLength = 240;
    private static readonly HashSet<string> SearchableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json",
        ".jsonl",
        ".txt",
        ".log",
        ".xml",
        ".md",
        ".html",
        ".csv"
    };

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<ReplaySearchResult> SearchAsync(CliOptions options, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var query = options.Get("contains") ?? options.Get("text") ?? options.Get("query");
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new UsageException("replay search requires --contains <text>.");
        }

        var limit = options.Int("limit", DefaultLimit);
        if (limit <= 0)
        {
            throw new UsageException("replay search --limit must be greater than zero.");
        }

        var matches = new List<ReplaySearchMatchResult>();
        var scannedFileCount = 0;
        var truncated = false;
        foreach (var file in GetSearchableFiles(artifacts.Root))
        {
            scannedFileCount++;
            await foreach (var match in SearchFileAsync(artifacts.Root, file, query).ConfigureAwait(false))
            {
                if (matches.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                matches.Add(match);
            }

            if (truncated)
            {
                break;
            }
        }

        return new ReplaySearchResult(
            ResultSchemas.ReplaySearch,
            artifacts.Root,
            query,
            matches.Count,
            scannedFileCount,
            truncated,
            BuildCommandHints(artifacts.Root, query, matches).ToArray(),
            matches);
    }

    private static IEnumerable<ReplaySearchCommandHint> BuildCommandHints(
        string artifactRoot,
        string query,
        IReadOnlyCollection<ReplaySearchMatchResult> matches)
    {
        yield return new ReplaySearchCommandHint(
            "describe_replay_capsule",
            "Open the replay front door for the artifact root.",
            $"luotsi replay capsule --artifacts {Quote(artifactRoot)} --write-readme --write-json");

        yield return new ReplaySearchCommandHint(
            "open_artifact_index",
            "Open the artifact browser for screenshots, logs, reports, and generated replay files.",
            $"luotsi replay open --artifacts {Quote(artifactRoot)}");

        if (matches.Any(static match => string.Equals(match.Kind, "timeline", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ReplaySearchCommandHint(
                "scrub_failures",
                "Scrub failure-relevant timeline events near this search result.",
                $"luotsi replay scrub --artifacts {Quote(artifactRoot)} --failures --context 3 --write-markdown");
        }

        if (matches.Any(IsGraphUsefulMatch))
        {
            yield return new ReplaySearchCommandHint(
                "graph_matching_context",
                "Query the semantic graph for the same text.",
                $"luotsi replay graph --artifacts {Quote(artifactRoot)} --contains {Quote(query)} --write-markdown");
        }
    }

    private static bool IsGraphUsefulMatch(ReplaySearchMatchResult match) =>
        string.Equals(match.Kind, "timeline", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(match.Kind, "failure", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(match.Kind, "screen_state", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(match.Kind, "hierarchy", StringComparison.OrdinalIgnoreCase);

    private IEnumerable<string> GetSearchableFiles(string artifactRoot) =>
        _fileSystem
            .GetFiles(artifactRoot, "*", SearchOption.AllDirectories)
            .Where(IsSearchableFile)
            .Order(StringComparer.OrdinalIgnoreCase);

    private async IAsyncEnumerable<ReplaySearchMatchResult> SearchFileAsync(string artifactRoot, string file, string query)
    {
        using var stream = _fileSystem.OpenRead(file);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096);
        var lineNumber = 0;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                yield return new ReplaySearchMatchResult(
                    ToArtifactRelativePath(artifactRoot, file),
                    lineNumber,
                    Classify(file),
                    CreatePreview(line, query));
            }
        }
    }

    private static bool IsSearchableFile(string file)
    {
        var extension = Path.GetExtension(file);
        return !string.IsNullOrWhiteSpace(extension) && SearchableExtensions.Contains(extension);
    }

    private static string Classify(string file)
    {
        var fileName = Path.GetFileName(file);
        if (fileName.Equals(SessionReplayArtifacts.TimelineFileName, StringComparison.OrdinalIgnoreCase))
        {
            return "timeline";
        }

        if (fileName.Equals(SessionReplayArtifacts.MetadataFileName, StringComparison.OrdinalIgnoreCase))
        {
            return "replay_metadata";
        }

        if (fileName.Contains("failure-capsule", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("failure", StringComparison.OrdinalIgnoreCase))
        {
            return "failure";
        }

        if (fileName.Contains("logcat", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
        {
            return "log";
        }

        if (fileName.Contains("screen-state", StringComparison.OrdinalIgnoreCase))
        {
            return "screen_state";
        }

        if (fileName.Contains("hierarchy", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return "hierarchy";
        }

        if (fileName.Contains("report", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("junit.xml", StringComparison.OrdinalIgnoreCase))
        {
            return "report";
        }

        return "artifact";
    }

    private static string CreatePreview(string line, string query)
    {
        var normalized = line.Trim();
        if (normalized.Length <= MaxPreviewLength)
        {
            return normalized;
        }

        var matchIndex = normalized.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (matchIndex < 0)
        {
            return normalized[..MaxPreviewLength] + "...";
        }

        var start = Math.Max(0, matchIndex - 80);
        var length = Math.Min(MaxPreviewLength, normalized.Length - start);
        var preview = normalized.Substring(start, length);
        if (start > 0)
        {
            preview = "..." + preview;
        }

        if (start + length < normalized.Length)
        {
            preview += "...";
        }

        return preview;
    }

    private static string ToArtifactRelativePath(string artifactRoot, string file)
    {
        var relative = Path.GetRelativePath(artifactRoot, file);
        return relative.Replace('\\', '/');
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
