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
        builder.AppendLine($"Matched: `{graph.MatchedNodeCount}` nodes, `{graph.MatchedEdgeCount}` edges");
        builder.AppendLine($"Truncated: `{graph.Truncated.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"Query: `{ReplayGraphQueryEngine.Describe(graph.Query)}`");
        builder.AppendLine();
        AppendFailureSummary(builder, graph);
        AppendActions(builder, graph);
        AppendInsights(builder, graph);
        AppendKindTables(builder, graph);
        AppendNodes(builder, graph);
        AppendEdges(builder, graph);
        return builder.ToString();
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
}
