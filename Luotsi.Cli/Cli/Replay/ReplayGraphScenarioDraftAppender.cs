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
        if (!root.TryGetProperty("step_origins", out var origins) || origins.ValueKind != JsonValueKind.Array)
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
        var stepIndex = TryGetInt32(origin, "step_index");
        var source = TryGetString(origin, "source");
        var eventType = TryGetString(origin, "event_type");
        var command = TryGetString(origin, "command");
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
                ["detail"] = TryGetString(origin, "detail")
            }));
        edges.Add(new ReplayGraphEdgeResult(draftId, stepId, "generates_step", new Dictionary<string, string?>()));

        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var sourceId = "draft_source:" + StableId(source);
        AddNode(nodes, new ReplayGraphNodeResult(sourceId, "draft_source", source, new Dictionary<string, string?> { ["source"] = source }));
        edges.Add(new ReplayGraphEdgeResult(stepId, sourceId, "derived_from", new Dictionary<string, string?> { ["event_type"] = eventType, ["command"] = command }));
    }

    private static void AddSourceSummaries(
        JsonElement root,
        string draftId,
        Dictionary<string, ReplayGraphNodeResult> nodes,
        List<ReplayGraphEdgeResult> edges)
    {
        if (!root.TryGetProperty("source_summaries", out var sourceSummaries) || sourceSummaries.ValueKind != JsonValueKind.Array)
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

        var sourceId = "draft_source:" + StableId(source);
        AddNode(nodes, new ReplayGraphNodeResult(
            sourceId,
            "draft_source",
            source,
            new Dictionary<string, string?>
            {
                ["source"] = source,
                ["step_count"] = TryGetInt32(sourceSummary, "step_count")?.ToString(CultureInfo.InvariantCulture),
                ["normalization_count"] = TryGetInt32(sourceSummary, "normalization_count")?.ToString(CultureInfo.InvariantCulture),
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
        var normalizationId = draftId + ":normalization:" + sequence.ToString(CultureInfo.InvariantCulture);
        AddNode(nodes, new ReplayGraphNodeResult(
            normalizationId,
            "draft_normalization",
            kind,
            new Dictionary<string, string?>
            {
                ["kind"] = kind,
                ["detail"] = TryGetString(normalization, "detail"),
                ["source"] = TryGetString(normalization, "source"),
                ["event_type"] = TryGetString(normalization, "event_type"),
                ["confidence"] = TryGetString(normalization, "confidence")
            }));
        edges.Add(new ReplayGraphEdgeResult(draftId, normalizationId, "applies_normalization", new Dictionary<string, string?> { ["kind"] = kind }));
    }

    private static void AddNode(Dictionary<string, ReplayGraphNodeResult> nodes, ReplayGraphNodeResult node) =>
        nodes.TryAdd(node.Id, node);

    private static string StableId(string value)
    {
        var stable = StableIdChars.Replace(value.Trim(), "-").Trim('-');
        return stable.Length == 0 ? "value" : stable.ToLowerInvariant();
    }

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
}
