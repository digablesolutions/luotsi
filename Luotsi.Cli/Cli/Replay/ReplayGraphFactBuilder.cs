using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal static class ReplayGraphFactBuilder
{
    public static IReadOnlyList<ReplayGraphFactResult> Build(
        string artifactRoot,
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges,
        IReadOnlyList<ReplayGraphEvidenceResult> evidence,
        IReadOnlyList<ReplayGraphFailurePathResult> failurePaths)
    {
        var facts = new List<ReplayGraphFactResult>();
        AddFailureFacts(artifactRoot, failurePaths, facts);
        AddEvidenceFacts(artifactRoot, evidence, facts);
        AddTransitionFacts(edges, facts);
        AddSelectorActionFacts(artifactRoot, nodes, edges, facts);

        return facts
            .GroupBy(static fact => $"{fact.Category}\n{fact.Subject}\n{fact.Predicate}\n{fact.Object}", StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static fact => Rank(fact.Category))
            .ThenBy(static fact => fact.Subject, StringComparer.Ordinal)
            .ThenBy(static fact => fact.Predicate, StringComparer.Ordinal)
            .ThenBy(static fact => fact.Object, StringComparer.Ordinal)
            .Take(100)
            .ToArray();
    }

    private static void AddFailureFacts(
        string artifactRoot,
        IReadOnlyList<ReplayGraphFailurePathResult> failurePaths,
        List<ReplayGraphFactResult> facts)
    {
        foreach (var path in failurePaths.Take(20))
        {
            facts.Add(new ReplayGraphFactResult(
                "failure",
                path.FailureNodeId,
                "has_failure_path",
                path.Summary,
                0.95,
                path.NodeIds,
                path.EdgeIds,
                $"luotsi replay scrub --artifacts {Quote(artifactRoot)} --failures --context 3 --write-markdown"));
        }
    }

    private static void AddEvidenceFacts(
        string artifactRoot,
        IReadOnlyList<ReplayGraphEvidenceResult> evidence,
        List<ReplayGraphFactResult> facts)
    {
        foreach (var item in evidence.Take(50))
        {
            facts.Add(new ReplayGraphFactResult(
                "evidence",
                item.NodeId,
                "has_evidence",
                string.IsNullOrWhiteSpace(item.Detail) ? item.Title : item.Detail!,
                0.9,
                [item.NodeId],
                item.EdgeIds,
                item.Command ?? $"luotsi replay graph --artifacts {Quote(artifactRoot)} --node {Quote(item.NodeId)} --depth 2 --write-markdown"));
        }
    }

    private static void AddTransitionFacts(
        IReadOnlyList<ReplayGraphEdgeResult> edges,
        List<ReplayGraphFactResult> facts)
    {
        foreach (var edge in edges.Where(static edge => string.Equals(edge.Kind, "transitions_to", StringComparison.Ordinal)).Take(50))
        {
            var category = GetProperty(edge, "category") ?? "progression";
            facts.Add(new ReplayGraphFactResult(
                "transition",
                edge.From,
                category,
                edge.To,
                category.Contains("failure", StringComparison.Ordinal) ? 0.9 : 0.75,
                [edge.From, edge.To],
                [EdgeId(edge)],
                null));
        }
    }

    private static void AddSelectorActionFacts(
        string artifactRoot,
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges,
        List<ReplayGraphFactResult> facts)
    {
        var nodeById = nodes.ToDictionary(static node => node.Id, StringComparer.Ordinal);
        foreach (var edge in edges.Take(500))
        {
            if (!nodeById.TryGetValue(edge.To, out var target))
            {
                continue;
            }

            if (string.Equals(edge.Kind, "mentions_selector", StringComparison.Ordinal) &&
                string.Equals(target.Kind, "selector", StringComparison.Ordinal))
            {
                facts.Add(new ReplayGraphFactResult(
                    "selector",
                    edge.From,
                    "mentions_selector",
                    target.Label,
                    0.85,
                    [edge.From, target.Id],
                    [EdgeId(edge)],
                    $"luotsi replay graph --artifacts {Quote(artifactRoot)} --selector {Quote(target.Label)} --write-markdown"));
                continue;
            }

            if (string.Equals(edge.Kind, "describes_action", StringComparison.Ordinal) &&
                string.Equals(target.Kind, "action", StringComparison.Ordinal))
            {
                facts.Add(new ReplayGraphFactResult(
                    "action",
                    edge.From,
                    "describes_action",
                    target.Label,
                    0.85,
                    [edge.From, target.Id],
                    [EdgeId(edge)],
                    $"luotsi replay graph --artifacts {Quote(artifactRoot)} --action {Quote(target.Label)} --write-markdown"));
            }
        }
    }

    private static string? GetProperty(ReplayGraphEdgeResult edge, string name) =>
        edge.Properties.GetValueOrDefault(name);

    private static string EdgeId(ReplayGraphEdgeResult edge) =>
        edge.From + " -> " + edge.Kind + " -> " + edge.To;

    private static int Rank(string category) => category switch
    {
        "failure" => 0,
        "transition" => 1,
        "evidence" => 2,
        "selector" => 3,
        "action" => 4,
        _ => 10
    };

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;
}
