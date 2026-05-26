using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Artifacts;

internal sealed class ArtifactEvidenceDetailReader(string root, IFileSystem fileSystem)
{
    private const int MaxJsonlDetailBytes = 256 * 1024;
    private const int MaxJsonlDetailLines = 500;

    private readonly string _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public string? TryBuild(string path)
    {
        try
        {
            var extension = Path.GetExtension(path);
            if (string.Equals(extension, ".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                return BuildJsonlDetail(path);
            }

            if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
            {
                return BuildJsonDetail(path);
            }

            return string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase)
                ? BuildXmlDetail(path)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private string? BuildJsonDetail(string path)
    {
        using var stream = _fileSystem.OpenRead(Path.Join(_root, path));
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schema", out var schema) ||
            schema.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var schemaName = schema.GetString();
        return schemaName switch
        {
            ResultSchemas.FailureCapsule => BuildFailureCapsuleSummary(root),
            "luotsi-scenario-run-report.v1" => BuildScenarioRunReportSummary(root),
            ResultSchemas.ReplayOpen => BuildReplayOpenSummary(root),
            ResultSchemas.ReplayCapsule => BuildReplayCapsuleSummary(root),
            ResultSchemas.ReplayScrub => BuildReplayScrubSummary(root),
            ResultSchemas.ScenarioDraft => BuildScenarioDraftSummary(root),
            ResultSchemas.SessionReplay => BuildSessionReplayDetail(root),
            _ => null
        };
    }

    private string? BuildXmlDetail(string path)
    {
        if (!Path.GetFileName(path).Contains("junit", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var stream = _fileSystem.OpenRead(Path.Join(_root, path));
        var document = XDocument.Load(stream);
        var root = document.Root;
        if (root is null)
        {
            return null;
        }

        var parts = new List<string> { "format=junit" };
        AddXmlAttribute(parts, root, "tests");
        AddXmlAttribute(parts, root, "failures");
        AddXmlAttribute(parts, root, "errors");
        AddXmlAttribute(parts, root, "skipped");
        AddXmlAttribute(parts, root, "time", "duration_sec");
        return parts.Count == 1 ? null : string.Join(" | ", parts);
    }

    private string? BuildJsonlDetail(string path)
    {
        using var stream = _fileSystem.OpenRead(Path.Join(_root, path));
        var truncatedByBytes = false;
        if (stream is {CanSeek: true, Length: > MaxJsonlDetailBytes})
        {
            stream.Seek(-MaxJsonlDetailBytes, SeekOrigin.End);
            truncatedByBytes = true;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? firstFailure = null;
        var sampledLines = new Queue<string>();
        var lineCount = 0;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (truncatedByBytes && lineCount == 0)
            {
                lineCount++;
                continue;
            }

            if (sampledLines.Count == MaxJsonlDetailLines)
            {
                sampledLines.Dequeue();
            }

            sampledLines.Enqueue(line);
            lineCount++;
        }

        foreach (var line in sampledLines)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
            var item = document.RootElement;
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("type", out var typeProperty) ||
                typeProperty.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var type = typeProperty.GetString();
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            counts[type] = counts.GetValueOrDefault(type) + 1;
            firstFailure ??= IsTimelineFailureRelevant(item, type) ? BuildTimelineFailureSummary(item, type) : null;
            }
        }

        if (counts.Count == 0)
        {
            return null;
        }

        var topTypes = string.Join(
            ", ",
            counts
                .OrderByDescending(static item => item.Value)
                .ThenBy(static item => item.Key)
                .Take(3)
                .Select(static item => $"{item.Key}={item.Value}"));
        var prefix = lineCount > sampledLines.Count || truncatedByBytes
            ? $"events_sampled={counts.Values.Sum()}"
            : $"events={counts.Values.Sum()}";
        return string.IsNullOrWhiteSpace(firstFailure)
            ? $"{prefix} | top={topTypes}"
            : $"{prefix} | first_failure={firstFailure} | top={topTypes}";
    }

    private static string? BuildSessionReplayDetail(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "sessionKind", "session_kind");
        AddJsonProperty(parts, root, "reason");
        AddJsonProperty(parts, root, "exitCode", "exit_code");
        AddJsonProperty(parts, root, "eventCount", "event_count");
        AddJsonProperty(parts, root, "target");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildScenarioRunReportSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "status");
        AddJsonProperty(parts, root, "total");
        AddJsonProperty(parts, root, "passed");
        AddJsonProperty(parts, root, "failed");
        AddJsonProperty(parts, root, "skipped");
        AddJsonProperty(parts, root, "durationMs", "duration_ms");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildFailureCapsuleSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "status");

        if (root.TryGetProperty("scenarios", out var scenarios) && scenarios.ValueKind == JsonValueKind.Array)
        {
            var scenarioItems = scenarios.EnumerateArray().ToArray();
            parts.Add($"scenarios={scenarioItems.Length}");
            AddObjectArraySummary(parts, scenarioItems, "scenario", "failed_scenarios");
            AddNestedObjectArraySummary(parts, scenarioItems, "failedStep", "name", "failed_steps");
        }

        AddArrayCount(parts, root, "screenshots");
        AddArrayCount(parts, root, "logcat");
        AddArrayCount(parts, root, "hierarchies");
        AddArrayCount(parts, root, "screenStates", "screen_states");
        AddArrayCount(parts, root, "failureBundles", "failure_bundles");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildReplayOpenSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "sessionCount", "session_count");
        AddJsonProperty(parts, root, "failureCount", "failure_count");
        AddJsonProperty(parts, root, "opened");
        AddArrayCount(parts, root, "commands");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildReplayCapsuleSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "sessionCount", "session_count");
        AddJsonProperty(parts, root, "failureCount", "failure_count");
        AddJsonProperty(parts, root, "scenarioDraftAvailable", "scenario_draft_available");
        AddArrayCount(parts, root, "artifactManifest", "artifact_manifest");
        AddArrayCount(parts, root, "failureTimeline", "failure_timeline");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildReplayScrubSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "eventCount", "event_count");
        AddJsonProperty(parts, root, "focusIndex", "focus_index");
        AddJsonProperty(parts, root, "markdownPath", "markdown_path");
        AddArrayCount(parts, root, "commands");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildScenarioDraftSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "confidence");
        AddArrayCount(parts, root, "sourceSummaries", "source_summaries");
        AddArrayCount(parts, root, "warnings");
        AddArrayCount(parts, root, "reviewItems", "review_items");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static bool IsTimelineFailureRelevant(JsonElement root, string type)
    {
        if (type.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("diagnostic", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("fallback", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return root.TryGetProperty("error", out _);
    }

    private static string BuildTimelineFailureSummary(JsonElement root, string type)
    {
        var parts = new List<string> { type };
        AddJsonProperty(parts, root, "category");
        AddJsonProperty(parts, root, "message");
        AddErrorProperty(parts, root);
        return string.Join(" | ", parts);
    }

    private static void AddJsonProperty(List<string> parts, JsonElement root, string name, string? label = null)
    {
        if (!root.TryGetProperty(name, out var property))
        {
            return;
        }

        var value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label ?? ToSnakeCase(name)}={value}");
        }
    }

    private static void AddErrorProperty(List<string> parts, JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var category = TryGetString(error, "category");
        var message = TryGetString(error, "message");
        if (!string.IsNullOrWhiteSpace(category) || !string.IsNullOrWhiteSpace(message))
        {
            parts.Add($"error={category}: {message}".TrimEnd(' ', ':'));
        }
    }

    private static void AddXmlAttribute(List<string> parts, XElement element, string name, string? label = null)
    {
        var value = element.Attribute(name)?.Value;
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label ?? ToSnakeCase(name)}={value}");
        }
    }

    private static void AddArrayCount(List<string> parts, JsonElement root, string name, string? label = null)
    {
        if (root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Array)
        {
            parts.Add($"{label ?? ToSnakeCase(name)}={property.GetArrayLength()}");
        }
    }

    private static void AddObjectArraySummary(List<string> parts, JsonElement[] items, string propertyName, string label)
    {
        var values = items
            .Select(item => TryGetString(item, propertyName))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Take(2)
            .Cast<string>()
            .ToArray();
        if (values.Length > 0)
        {
            parts.Add($"{label}={string.Join(", ", values)}");
        }
    }

    private static void AddNestedObjectArraySummary(List<string> parts, JsonElement[] items, string objectName, string propertyName, string label)
    {
        var values = items
            .Select(item => TryGetObjectString(item, objectName, propertyName))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Take(2)
            .Cast<string>()
            .ToArray();
        if (values.Length > 0)
        {
            parts.Add($"{label}={string.Join(", ", values)}");
        }
    }

    private static string? TryGetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static string? TryGetObjectString(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetString(property, propertyName);
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        foreach (var ch in value)
        {
            if (char.IsUpper(ch) && builder.Length > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}
