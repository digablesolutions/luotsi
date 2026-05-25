using System.Text.Json;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Artifacts;

internal sealed class ReplayGraphSignalReader(string root, IFileSystem fileSystem)
{
    private const string GraphFileName = "replay-graph.json";
    private readonly string _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public SemanticSignalSummary? TryRead()
    {
        var fullPath = Path.Join(_root, GraphFileName);
        if (!_fileSystem.FileExists(fullPath))
        {
            return null;
        }

        try
        {
            using var stream = _fileSystem.OpenRead(fullPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schema", out var schema) ||
                !string.Equals(schema.GetString(), ResultSchemas.ReplayGraph, StringComparison.Ordinal))
            {
                return null;
            }

            var items = new List<SemanticSignalItem>();
            AddAgentSummarySignals(items, root);
            AddHypothesisSignals(items, root);
            AddInsightSignals(items, root);
            return items.Count == 0 ? null : new SemanticSignalSummary(GraphFileName, items);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void AddAgentSummarySignals(List<SemanticSignalItem> items, JsonElement root)
    {
        if (!root.TryGetProperty("agentSummary", out var summary) || summary.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        AddSignal(items, "what failed", TryGetString(summary, "whatFailed"));
        AddSignal(items, "what changed", TryGetString(summary, "whatChanged"));
        AddSignal(items, "act on", TryGetString(summary, "whatCanActOn"));
    }

    private static void AddHypothesisSignals(List<SemanticSignalItem> items, JsonElement root)
    {
        if (!root.TryGetProperty("hypotheses", out var hypotheses) || hypotheses.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var hypothesis in hypotheses.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object).Take(2))
        {
            var summary = TryGetString(hypothesis, "summary");
            if (string.IsNullOrWhiteSpace(summary))
            {
                continue;
            }

            var severity = TryGetString(hypothesis, "severity");
            var confidence = TryGetScalarText(hypothesis, "confidence");
            var label = string.IsNullOrWhiteSpace(severity)
                ? "hypothesis"
                : "hypothesis/" + severity;
            var text = string.IsNullOrWhiteSpace(confidence)
                ? summary
                : $"{summary} (confidence {confidence})";
            items.Add(new SemanticSignalItem(label, text, TryGetString(hypothesis, "command")));
        }
    }

    private static void AddInsightSignals(List<SemanticSignalItem> items, JsonElement root)
    {
        if (!root.TryGetProperty("insights", out var insights) || insights.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var insight in insights.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object).Take(2))
        {
            var message = TryGetString(insight, "message");
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            var kind = TryGetString(insight, "kind") ?? "insight";
            var severity = TryGetString(insight, "severity");
            items.Add(new SemanticSignalItem(
                string.IsNullOrWhiteSpace(severity) ? kind : $"{kind}/{severity}",
                message,
                null));
        }
    }

    private static void AddSignal(List<SemanticSignalItem> items, string kind, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            items.Add(new SemanticSignalItem(kind, text, null));
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

    private static string? TryGetScalarText(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}

internal sealed record SemanticSignalSummary(string Path, IReadOnlyList<SemanticSignalItem> Items);

internal sealed record SemanticSignalItem(string Kind, string Text, string? Command);
