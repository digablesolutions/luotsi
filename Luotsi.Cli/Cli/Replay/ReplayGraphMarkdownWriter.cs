using System.Text;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal static class ReplayGraphMarkdownWriter
{
    public static string Build(ReplayGraphResult graph)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Replay Graph");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{graph.ArtifactRoot}`");
        builder.AppendLine($"Nodes: `{graph.NodeCount}` of `{graph.TotalNodeCount}`");
        builder.AppendLine($"Edges: `{graph.EdgeCount}` of `{graph.TotalEdgeCount}`");
        builder.AppendLine($"Evidence: `{graph.Evidence.Count}`");
        builder.AppendLine($"Matched: `{graph.MatchedNodeCount}` nodes, `{graph.MatchedEdgeCount}` edges");
        builder.AppendLine($"Truncated: `{graph.Truncated.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"Query: `{ReplayGraphQueryEngine.Describe(graph.Query)}`");
        builder.AppendLine();
        AppendOutputArtifacts(builder, graph);
        AppendAgentSummary(builder, graph);
        AppendFailureSummary(builder, graph);
        AppendActions(builder, graph);
        AppendEvidence(builder, graph);
        AppendInsights(builder, graph);
        AppendFailurePaths(builder, graph);
        AppendTransitions(builder, graph);
        AppendQueryExamples(builder, graph);
        AppendKindTables(builder, graph);
        AppendNodes(builder, graph);
        AppendEdges(builder, graph);
        return builder.ToString();
    }

    private static void AppendOutputArtifacts(StringBuilder builder, ReplayGraphResult graph)
    {
        if (string.IsNullOrWhiteSpace(graph.JsonPath) &&
            string.IsNullOrWhiteSpace(graph.JsonlPath) &&
            string.IsNullOrWhiteSpace(graph.MarkdownPath))
        {
            return;
        }

        builder.AppendLine("## Output Artifacts");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(graph.JsonPath))
        {
            builder.AppendLine($"- JSON: `{EscapeMarkdown(graph.JsonPath)}`");
        }

        if (!string.IsNullOrWhiteSpace(graph.JsonlPath))
        {
            builder.AppendLine($"- JSONL: `{EscapeMarkdown(graph.JsonlPath)}`");
        }

        if (!string.IsNullOrWhiteSpace(graph.MarkdownPath))
        {
            builder.AppendLine($"- Markdown: `{EscapeMarkdown(graph.MarkdownPath)}`");
        }

        builder.AppendLine();
    }

    private static void AppendFailureSummary(StringBuilder builder, ReplayGraphResult graph)
    {
        builder.AppendLine("## What Failed");
        builder.AppendLine();
        var failures = graph.Nodes.Where(ReplayGraphPredicates.IsFailureNode).Take(10).ToArray();
        if (failures.Length == 0)
        {
            builder.AppendLine("No failure-relevant nodes are present in this graph view.");
        }
        else
        {
            foreach (var failure in failures)
            {
                builder.AppendLine($"- `{EscapeMarkdown(failure.Id)}` {EscapeMarkdown(failure.Label)}: {EscapeMarkdown(ReplayGraphPredicates.GetProperty(failure, "detail") ?? ReplayGraphPredicates.GetProperty(failure, "error_message"))}");
            }
        }

        builder.AppendLine();
    }

    private static void AppendAgentSummary(StringBuilder builder, ReplayGraphResult graph)
    {
        builder.AppendLine("## Agent Summary");
        builder.AppendLine();
        builder.AppendLine($"- **What failed**: {EscapeMarkdown(graph.AgentSummary.WhatFailed)}");
        builder.AppendLine($"- **What changed**: {EscapeMarkdown(graph.AgentSummary.WhatChanged)}");
        builder.AppendLine($"- **What can I act on**: {EscapeMarkdown(graph.AgentSummary.WhatCanActOn)}");
        builder.AppendLine();
    }

    private static void AppendActions(StringBuilder builder, ReplayGraphResult graph)
    {
        builder.AppendLine("## What Agents Can Act On");
        builder.AppendLine();
        foreach (var action in graph.Actions)
        {
            builder.AppendLine($"- **{EscapeMarkdown(action.Kind)}**: {EscapeMarkdown(action.Message)}");
            if (!string.IsNullOrWhiteSpace(action.Command))
            {
                builder.AppendLine($"  `{EscapeMarkdown(action.Command)}`");
            }
        }

        builder.AppendLine();
    }

    private static void AppendInsights(StringBuilder builder, ReplayGraphResult graph)
    {
        builder.AppendLine("## Insights");
        builder.AppendLine();
        if (graph.Insights.Count == 0)
        {
            builder.AppendLine("No semantic insights were derived from this graph.");
        }
        else
        {
            builder.AppendLine("| Kind | Severity | Message | Nodes |");
            builder.AppendLine("|---|---|---|---|");
            foreach (var insight in graph.Insights)
            {
                builder.AppendLine($"| {EscapeMarkdown(insight.Kind)} | {EscapeMarkdown(insight.Severity)} | {EscapeMarkdown(insight.Message)} | {EscapeMarkdown(string.Join(", ", insight.NodeIds.Take(5)))} |");
            }
        }

        builder.AppendLine();
    }

    private static void AppendEvidence(StringBuilder builder, ReplayGraphResult graph)
    {
        if (graph.Evidence.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Evidence");
        builder.AppendLine();
        if (graph.EvidenceKinds.Count > 0)
        {
            builder.AppendLine("Kinds: " + string.Join(", ", graph.EvidenceKinds.Select(static kind => $"`{kind.Key}`={kind.Value}")));
            builder.AppendLine();
        }

        builder.AppendLine("| Kind | Node | Title | Detail | Artifact | Edges | Command |");
        builder.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var evidence in graph.Evidence)
        {
            builder.AppendLine($"| {EscapeMarkdown(evidence.Kind)} | {EscapeMarkdown(evidence.NodeId)} | {EscapeMarkdown(evidence.Title)} | {EscapeMarkdown(evidence.Detail)} | {EscapeMarkdown(evidence.ArtifactPath)} | {EscapeMarkdown(string.Join(", ", evidence.EdgeIds.Take(3)))} | {EscapeMarkdown(evidence.Command)} |");
        }

        builder.AppendLine();
    }

    private static void AppendKindTables(StringBuilder builder, ReplayGraphResult graph)
    {
        builder.AppendLine("## Node Kinds");
        builder.AppendLine();
        builder.AppendLine("| Kind | Count |");
        builder.AppendLine("|---|---:|");
        foreach (var kind in graph.NodeKinds)
        {
            builder.AppendLine($"| {EscapeMarkdown(kind.Key)} | {kind.Value} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Edge Kinds");
        builder.AppendLine();
        builder.AppendLine("| Kind | Count |");
        builder.AppendLine("|---|---:|");
        foreach (var kind in graph.EdgeKinds)
        {
            builder.AppendLine($"| {EscapeMarkdown(kind.Key)} | {kind.Value} |");
        }

        builder.AppendLine();
    }

    private static void AppendQueryExamples(StringBuilder builder, ReplayGraphResult graph)
    {
        if (graph.Taxonomy.QueryExamples.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Query Examples");
        builder.AppendLine();
        foreach (var example in graph.Taxonomy.QueryExamples)
        {
            builder.AppendLine($"- **{EscapeMarkdown(example.Kind)}**: {EscapeMarkdown(example.Description)}");
            builder.AppendLine($"  `{EscapeMarkdown(example.Command)}`");
        }

        builder.AppendLine();
    }

    private static void AppendFailurePaths(StringBuilder builder, ReplayGraphResult graph)
    {
        if (graph.FailurePaths.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Failure Paths");
        builder.AppendLine();
        builder.AppendLine("| Failure | Summary | Nodes |");
        builder.AppendLine("|---|---|---|");
        foreach (var path in graph.FailurePaths.Take(20))
        {
            builder.AppendLine($"| {EscapeMarkdown(path.FailureNodeId)} | {EscapeMarkdown(path.Summary)} | {EscapeMarkdown(string.Join(", ", path.NodeIds))} |");
        }

        builder.AppendLine();
    }

    private static void AppendTransitions(StringBuilder builder, ReplayGraphResult graph)
    {
        var transitions = graph.Edges
            .Where(static edge => string.Equals(edge.Kind, "transitions_to", StringComparison.Ordinal))
            .Take(20)
            .ToArray();
        if (transitions.Length == 0)
        {
            return;
        }

        builder.AppendLine("## Transitions");
        builder.AppendLine();
        builder.AppendLine("| Category | From | To | Elapsed ms |");
        builder.AppendLine("|---|---|---|---:|");
        foreach (var transition in transitions)
        {
            builder.AppendLine($"| {EscapeMarkdown(GetProperty(transition, "category"))} | {EscapeMarkdown(GetProperty(transition, "from_type"))} | {EscapeMarkdown(GetProperty(transition, "to_type"))} | {EscapeMarkdown(GetProperty(transition, "elapsed_ms"))} |");
        }

        builder.AppendLine();
    }

    private static void AppendNodes(StringBuilder builder, ReplayGraphResult graph)
    {
        builder.AppendLine("## Nodes");
        builder.AppendLine();
        builder.AppendLine("| Id | Kind | Label |");
        builder.AppendLine("|---|---|---|");
        foreach (var node in graph.Nodes)
        {
            builder.AppendLine($"| {EscapeMarkdown(node.Id)} | {EscapeMarkdown(node.Kind)} | {EscapeMarkdown(node.Label)} |");
        }

        builder.AppendLine();
    }

    private static void AppendEdges(StringBuilder builder, ReplayGraphResult graph)
    {
        builder.AppendLine("## Edges");
        builder.AppendLine();
        builder.AppendLine("| From | Kind | To |");
        builder.AppendLine("|---|---|---|");
        foreach (var edge in graph.Edges)
        {
            builder.AppendLine($"| {EscapeMarkdown(edge.From)} | {EscapeMarkdown(edge.Kind)} | {EscapeMarkdown(edge.To)} |");
        }
    }

    private static string EscapeMarkdown(string? value) =>
        (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string? GetProperty(ReplayGraphEdgeResult edge, string name) =>
        edge.Properties.TryGetValue(name, out var value) ? value : null;
}
