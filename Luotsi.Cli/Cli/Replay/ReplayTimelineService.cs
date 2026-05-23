using System.Text;
using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal sealed class ReplayTimelineService(IFileSystem fileSystem)
{
    private const int DefaultLimit = 200;
    private const string TimelineJsonFileName = "replay-timeline.json";
    private const string TimelineJsonlFileName = "replay-timeline.jsonl";
    private const string TimelineMarkdownFileName = "replay-timeline.md";
    private static readonly JsonSerializerOptions JsonLineOptions = new(AppJson.Options) { WriteIndented = false };
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<ReplayTimelineResult> ReadAsync(CliOptions options, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var limit = options.Int("limit", DefaultLimit);
        if (limit <= 0)
        {
            throw new UsageException("replay timeline --limit must be greater than zero.");
        }

        var typeFilter = options.Get("type");
        var containsFilter = options.Get("contains");
        var failuresOnly = options.HasFlag("failures");
        var events = new List<ReplayTimelineEventResult>();
        var files = _fileSystem.GetFiles(artifacts.Root, SessionReplayArtifacts.TimelineFileName, SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            throw new UsageException($"No {SessionReplayArtifacts.TimelineFileName} was found under artifact root '{artifacts.Root}'.");
        }

        var truncated = false;
        foreach (var file in files)
        {
            await foreach (var evt in ReadFileAsync(artifacts.Root, file).ConfigureAwait(false))
            {
                if (!Matches(evt, typeFilter, containsFilter, failuresOnly))
                {
                    continue;
                }

                if (events.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                events.Add(evt);
            }

            if (truncated)
            {
                break;
            }
        }

        var jsonPath = options.HasFlag("write-json")
            ? Path.Join(artifacts.Root, TimelineJsonFileName)
            : null;
        var jsonlPath = options.HasFlag("write-jsonl")
            ? Path.Join(artifacts.Root, TimelineJsonlFileName)
            : null;
        var markdownPath = options.HasFlag("write-markdown")
            ? Path.Join(artifacts.Root, TimelineMarkdownFileName)
            : null;
        var result = new ReplayTimelineResult(
            ResultSchemas.ReplayTimeline,
            artifacts.Root,
            events.Count,
            files.Length,
            truncated,
            jsonPath,
            jsonlPath,
            markdownPath,
            events);

        if (jsonPath is not null)
        {
            await artifacts.WriteJsonAsync(TimelineJsonFileName, result).ConfigureAwait(false);
        }

        if (jsonlPath is not null)
        {
            await artifacts.WriteTextAsync(TimelineJsonlFileName, ToJsonLines(result)).ConfigureAwait(false);
        }

        if (markdownPath is not null)
        {
            await artifacts.WriteTextAsync(TimelineMarkdownFileName, ToMarkdown(result)).ConfigureAwait(false);
        }

        return result;
    }

    public static IEnumerable<object> ToJsonLineObjects(ReplayTimelineResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        yield return new ReplayTimelineJsonLine(
            ResultSchemas.ReplayTimeline,
            "summary",
            result.ArtifactRoot,
            result.EventCount,
            result.ScannedFileCount,
            result.Truncated,
            null);

        foreach (var evt in result.Events)
        {
            yield return new ReplayTimelineJsonLine(
                ResultSchemas.ReplayTimeline,
                "event",
                result.ArtifactRoot,
                null,
                null,
                null,
                evt);
        }
    }

    private static string ToJsonLines(ReplayTimelineResult result) =>
        string.Join('\n', ToJsonLineObjects(result).Select(static line => JsonSerializer.Serialize(line, JsonLineOptions))) + "\n";

    private static string ToMarkdown(ReplayTimelineResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Replay Timeline");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{result.ArtifactRoot}`");
        builder.AppendLine($"Events: `{result.EventCount}`");
        builder.AppendLine($"Timeline files scanned: `{result.ScannedFileCount}`");
        builder.AppendLine($"Truncated: `{result.Truncated.ToString().ToLowerInvariant()}`");
        builder.AppendLine();
        builder.AppendLine("| # | Time | Type | Failure | Detail | Source |");
        builder.AppendLine("|---:|---|---|---|---|---|");
        foreach (var evt in result.Events)
        {
            builder.Append("| ");
            builder.Append(evt.Sequence);
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(evt.Timestamp?.ToString("O") ?? string.Empty));
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(evt.Type));
            builder.Append(" | ");
            builder.Append(evt.FailureRelevant ? "yes" : "no");
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(evt.Detail));
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(evt.Path));
            builder.AppendLine(" |");
        }

        return builder.ToString();
    }

    private async IAsyncEnumerable<ReplayTimelineEventResult> ReadFileAsync(string artifactRoot, string file)
    {
        using var stream = _fileSystem.OpenRead(file);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096);
        var sequence = 0;
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var evt = TryReadEvent(artifactRoot, file, sequence++, line);
            if (evt is not null)
            {
                yield return evt;
            }
        }
    }

    private static ReplayTimelineEventResult? TryReadEvent(string artifactRoot, string file, int sequence, string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryGetString(root, "type", out var type))
            {
                return null;
            }

            return new ReplayTimelineEventResult(
                Path.GetRelativePath(artifactRoot, file).Replace('\\', '/'),
                sequence,
                TryGetTimestamp(root),
                type,
                IsFailureRelevant(root, type),
                BuildDetail(root));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Matches(ReplayTimelineEventResult evt, string? typeFilter, string? containsFilter, bool failuresOnly)
    {
        if (failuresOnly && !evt.FailureRelevant)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(typeFilter) &&
            !string.Equals(evt.Type, typeFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(containsFilter) ||
            evt.Type.Contains(containsFilter, StringComparison.OrdinalIgnoreCase) ||
            evt.Detail.Contains(containsFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? TryGetTimestamp(JsonElement root)
    {
        foreach (var propertyName in new[] { "received_at", "occurred_at", "observed_at", "captured_at", "started_at", "ended_at", "reconnected_at" })
        {
            if (root.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(property.GetString(), out var timestamp))
            {
                return timestamp;
            }
        }

        return null;
    }

    private static bool IsFailureRelevant(JsonElement root, string type)
    {
        if (type.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("diagnostic", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("fallback", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (root.TryGetProperty("ok", out var okProperty) && okProperty.ValueKind == JsonValueKind.False)
        {
            return true;
        }

        if (root.TryGetProperty("reason", out var reasonProperty) &&
            reasonProperty.ValueKind == JsonValueKind.String &&
            string.Equals(reasonProperty.GetString(), "error", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return root.TryGetProperty("error", out _);
    }

    private static string BuildDetail(JsonElement root)
    {
        var parts = new List<string>();
        AddString(parts, root, "scenario");
        AddNumber(parts, root, "step_index");
        AddString(parts, root, "phase");
        AddString(parts, root, "step");
        AddString(parts, root, "action");
        AddString(parts, root, "status");
        AddString(parts, root, "command");
        AddString(parts, root, "reason");
        AddString(parts, root, "category");
        AddString(parts, root, "message");
        AddBool(parts, root, "ok", includeWhenTrue: false);
        AddError(parts, root);
        return string.Join(" | ", parts);
    }

    private static void AddString(List<string> parts, JsonElement root, string name)
    {
        if (TryGetString(root, name, out var value))
        {
            parts.Add($"{name}={value}");
        }
    }

    private static void AddNumber(List<string> parts, JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number)
        {
            parts.Add($"{name}={property}");
        }
    }

    private static void AddBool(List<string> parts, JsonElement root, string name, bool includeWhenTrue)
    {
        if (root.TryGetProperty(name, out var property) &&
            property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            (includeWhenTrue || !property.GetBoolean()))
        {
            parts.Add($"{name}={property.GetBoolean().ToString().ToLowerInvariant()}");
        }
    }

    private static void AddError(List<string> parts, JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (TryGetString(error, "category", out var category))
        {
            parts.Add($"error_category={category}");
        }

        if (TryGetString(error, "message", out var message))
        {
            parts.Add($"error_message={message}");
        }
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }

    private static string EscapeMarkdown(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private sealed record ReplayTimelineJsonLine(
        string Schema,
        string Type,
        string ArtifactRoot,
        int? EventCount,
        int? ScannedFileCount,
        bool? Truncated,
        ReplayTimelineEventResult? Event);
}
