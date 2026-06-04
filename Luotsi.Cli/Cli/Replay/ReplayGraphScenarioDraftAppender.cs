using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal sealed class ReplayGraphScenarioDraftAppender(IFileSystem fileSystem)
{
    private const string ScenarioDraftSummaryFileName = "scenario-draft-summary.json";
    private static readonly Regex StableIdChars = new("[^a-zA-Z0-9._:-]+", RegexOptions.Compiled);
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public void Add(
        string artifactRoot,
        IReadOnlyList<string> files,
        Dictionary<string, ReplayGraphNodeResult> nodes,
        List<ReplayGraphEdgeResult> edges)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        foreach (var file in files.Where(static file => Path.GetFileName(file).Equals(ScenarioDraftSummaryFileName, StringComparison.OrdinalIgnoreCase)))
        {
            TryAddDraft(artifactRoot, file, nodes, edges);
        }
    }

    private void TryAddDraft(
        string artifactRoot,
        string file,
        Dictionary<string, ReplayGraphNodeResult> nodes,
        List<ReplayGraphEdgeResult> edges)
    {
        try
        {
            using var stream = _fileSystem.OpenRead(Path.Join(artifactRoot, file));
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!TryGetString(root, "schema", out var schema) ||
                !string.Equals(schema, ResultSchemas.ScenarioDraft, StringComparison.Ordinal))
            {
                return;
            }

            AddDraft(root, file.Replace('\\', '/'), nodes, edges);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    private static void AddDraft(
        JsonElement root,
        string file,
        Dictionary<string, ReplayGraphNodeResult> nodes,
        List<ReplayGraphEdgeResult> edges)
    {
        var draftId = "scenario_draft:" + StableId(file);
        AddNode(nodes, new ReplayGraphNodeResult(
            draftId,
            "scenario_draft",
            TryGetObjectString(root, "scenario", "name") ?? "draft scenario",
            new Dictionary<string, string?>
            {
                ["path"] = file,
                ["confidence"] = TryGetString(root, "confidence"),
                ["output"] = TryGetString(root, "output")
            }));

        AddStepOrigins(root, draftId, nodes, edges);
        AddSourceSummaries(root, draftId, nodes, edges);
        AddNormalizations(root, draftId, nodes, edges);
    }

    private static void AddStepOrigins(
        JsonElement root,
        string draftId,
        Dictionary<string, ReplayGraphNodeResult> nodes,
        List<ReplayGraphEdgeResult> edges)
    {
        if (!TryGetProperty(root, "stepOrigins", "step_origins", out var origins) || origins.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var origin in origins.EnumerateArray())
        {
            AddStepOrigin(draftId, origin, nodes, edges);
        }
    }

    private static void AddStepOrigin(
        string draftId,
        JsonElement origin,
        Dictionary<string, ReplayGraphNodeResult> nodes,
        List<ReplayGraphEdgeResult> edges)
    {
        var stepIndex = TryGetInt32(origin, "stepIndex", "step_index");
        var source = TryGetString(origin, "source");
        var eventType = TryGetString(origin, "eventType", "event_type");
        var command = TryGetString(origin, "command");
        var sourcePath = TryGetString(origin, "sourcePath", "source_path");
        var sequence = TryGetInt32(origin, "sequence")?.ToString(CultureInfo.InvariantCulture);
        var timestamp = TryGetString(origin, "timestamp");
        var sourceCommand = TryGetString(origin, "sourceCommand", "source_command");
        var stepId = draftId + ":step:" + (stepIndex?.ToString(CultureInfo.InvariantCulture) ?? "unknown");
        AddNode(nodes, new ReplayGraphNodeResult(
            stepId,
            "generated_step",
            "step " + (stepIndex?.ToString(CultureInfo.InvariantCulture) ?? "?"),
            new Dictionary<string, string?>
            {
                ["step_index"] = stepIndex?.ToString(CultureInfo.InvariantCulture),
                ["source"] = source,
                ["event_type"] = eventType,
                ["command"] = command,
                ["confidence"] = TryGetString(origin, "confidence"),
                ["detail"] = TryGetString(origin, "detail"),
                ["source_path"] = sourcePath,
                ["sequence"] = sequence,
                ["timestamp"] = timestamp,
                ["source_command"] = sourceCommand
            }));
        edges.Add(new ReplayGraphEdgeResult(draftId, stepId, "generates_step", new Dictionary<string, string?>()));

        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var sourceId = AddSourceNode(source, nodes);
        edges.Add(new ReplayGraphEdgeResult(stepId, sourceId, "derived_from", new Dictionary<string, string?>
        {
            ["event_type"] = eventType,
            ["command"] = command,
            ["source_path"] = sourcePath,
            ["sequence"] = sequence,
            ["timestamp"] = timestamp,
            ["source_command"] = sourceCommand
        }));
    }

    private static void AddSourceSummaries(
        JsonElement root,
        string draftId,
        Dictionary<string, ReplayGraphNodeResult> nodes,
        List<ReplayGraphEdgeResult> edges)
    {
        if (!TryGetProperty(root, "sourceSummaries", "source_summaries", out var sourceSummaries) || sourceSummaries.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var sourceSummary in sourceSummaries.EnumerateArray())
        {
            AddSourceSummary(draftId, sourceSummary, nodes, edges);
        }
    }

    private static void AddSourceSummary(
        string draftId,
        JsonElement sourceSummary,
        Dictionary<string, ReplayGraphNodeResult> nodes,
        List<ReplayGraphEdgeResult> edges)
    {
        if (!TryGetString(sourceSummary, "source", out var source))
        {
            return;
        }

        var sourceId = SourceId(source);
        AddNode(nodes, new ReplayGraphNodeResult(
            sourceId,
            "draft_source",
            source,
            new Dictionary<string, string?>
            {
                ["source"] = source,
                ["step_count"] = TryGetInt32(sourceSummary, "stepCount", "step_count")?.ToString(CultureInfo.InvariantCulture),
                ["normalization_count"] = TryGetInt32(sourceSummary, "normalizationCount", "normalization_count")?.ToString(CultureInfo.InvariantCulture),
                ["confidence"] = TryGetString(sourceSummary, "confidence")
            }));
        edges.Add(new ReplayGraphEdgeResult(draftId, sourceId, "uses_source", new Dictionary<string, string?>()));
    }

    private static void AddNormalizations(
        JsonElement root,
        string draftId,
        Dictionary<string, ReplayGraphNodeResult> nodes,
        List<ReplayGraphEdgeResult> edges)
    {
        if (!root.TryGetProperty("normalizations", out var normalizations) || normalizations.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var sequence = 0;
        foreach (var normalization in normalizations.EnumerateArray())
        {
            AddNormalization(draftId, normalization, sequence++, nodes, edges);
        }
    }

    private static void AddNormalization(
        string draftId,
        JsonElement normalization,
        int sequence,
        Dictionary<string, ReplayGraphNodeResult> nodes,
        List<ReplayGraphEdgeResult> edges)
    {
        var kind = TryGetString(normalization, "kind") ?? "normalization";
        var source = TryGetString(normalization, "source");
        var eventType = TryGetString(normalization, "eventType", "event_type");
        var sourcePath = TryGetString(normalization, "sourcePath", "source_path");
        var sequenceValue = TryGetInt32(normalization, "sequence");
        var sequenceText = sequenceValue?.ToString(CultureInfo.InvariantCulture);
        var timestamp = TryGetString(normalization, "timestamp");
        var sourceCommand = TryGetString(normalization, "sourceCommand", "source_command");
        var normalizationId = draftId + ":normalization:" + sequence.ToString(CultureInfo.InvariantCulture);
        AddNode(nodes, new ReplayGraphNodeResult(
            normalizationId,
            "draft_normalization",
            kind,
            new Dictionary<string, string?>
            {
                ["kind"] = kind,
                ["detail"] = TryGetString(normalization, "detail"),
                ["source"] = source,
                ["event_type"] = eventType,
                ["confidence"] = TryGetString(normalization, "confidence"),
                ["source_path"] = sourcePath,
                ["sequence"] = sequenceText,
                ["timestamp"] = timestamp,
                ["source_command"] = sourceCommand
            }));
        edges.Add(new ReplayGraphEdgeResult(draftId, normalizationId, "applies_normalization", new Dictionary<string, string?> { ["kind"] = kind }));

        if (!string.IsNullOrWhiteSpace(source))
        {
            var sourceId = AddSourceNode(source, nodes);
            edges.Add(new ReplayGraphEdgeResult(normalizationId, sourceId, "derived_from", new Dictionary<string, string?>
            {
                ["event_type"] = eventType,
                ["source_path"] = sourcePath,
                ["sequence"] = sequenceText,
                ["timestamp"] = timestamp,
                ["source_command"] = sourceCommand
            }));
        }
    }

    private static string AddSourceNode(string source, Dictionary<string, ReplayGraphNodeResult> nodes)
    {
        var sourceId = SourceId(source);
        AddNode(nodes, new ReplayGraphNodeResult(sourceId, "draft_source", source, new Dictionary<string, string?> { ["source"] = source }));
        return sourceId;
    }

    private static string SourceId(string source) => "draft_source:" + StableId(source);

    private static void AddNode(Dictionary<string, ReplayGraphNodeResult> nodes, ReplayGraphNodeResult node)
    {
        if (nodes.TryAdd(node.Id, node))
        {
            return;
        }

        var existing = nodes[node.Id];
        var properties = existing.Properties.ToDictionary(static property => property.Key, static property => property.Value, StringComparer.Ordinal);
        foreach (var property in node.Properties)
        {
            if (string.IsNullOrWhiteSpace(property.Value))
            {
                continue;
            }

            if (!properties.TryGetValue(property.Key, out var existingValue) || string.IsNullOrWhiteSpace(existingValue))
            {
                properties[property.Key] = property.Value;
            }
        }

        nodes[node.Id] = existing with { Properties = properties };
    }

    private static string StableId(string value)
    {
        var stable = StableIdChars.Replace(value.Trim(), "-").Trim('-');
        return stable.Length == 0 ? "value" : stable.ToLowerInvariant();
    }

    private static bool TryGetProperty(JsonElement root, string name, string fallbackName, out JsonElement property) =>
        root.TryGetProperty(name, out property) || root.TryGetProperty(fallbackName, out property);

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? TryGetString(JsonElement root, string name) =>
        TryGetString(root, name, out var value) ? value : null;

    private static string? TryGetString(JsonElement root, string name, string fallbackName) =>
        TryGetString(root, name) ?? TryGetString(root, fallbackName);

    private static string? TryGetObjectString(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetString(property, propertyName);
    }

    private static int? TryGetInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            return null;
        }

        return value;
    }

    private static int? TryGetInt32(JsonElement root, string name, string fallbackName) =>
        TryGetInt32(root, name) ?? TryGetInt32(root, fallbackName);
}
