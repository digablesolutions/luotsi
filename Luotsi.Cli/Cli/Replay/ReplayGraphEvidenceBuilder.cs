using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal static class ReplayGraphEvidenceBuilder
{
    public static IReadOnlyList<ReplayGraphEvidenceResult> Build(
        string artifactRoot,
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges)
    {
        var evidence = new List<ReplayGraphEvidenceResult>();
        foreach (var node in nodes)
        {
            var item = BuildEvidence(artifactRoot, node, EdgeIdsFor(node.Id, edges));
            if (item is not null)
            {
                evidence.Add(item);
            }
        }

        return evidence
            .OrderBy(static item => Rank(item.Kind))
            .ThenBy(static item => item.NodeId, StringComparer.Ordinal)
            .Take(50)
            .ToArray();
    }

    private static ReplayGraphEvidenceResult? BuildEvidence(
        string artifactRoot,
        ReplayGraphNodeResult node,
        IReadOnlyList<string> edgeIds)
    {
        return node.Kind switch
        {
            "failure" => new ReplayGraphEvidenceResult(
                "failure",
                node.Id,
                node.Label,
                GetProperty(node, "detail") ?? GetProperty(node, "error_message"),
                null,
                $"luotsi replay scrub --artifacts {Quote(artifactRoot)} --failures --context 3 --write-markdown",
                edgeIds),
            "artifact" => new ReplayGraphEvidenceResult(
                "artifact",
                node.Id,
                node.Label,
                GetProperty(node, "kind"),
                GetProperty(node, "path"),
                $"luotsi replay open --artifacts {Quote(artifactRoot)} --dry-run",
                edgeIds),
            "selector" => new ReplayGraphEvidenceResult(
                "selector",
                node.Id,
                node.Label,
                GetProperty(node, "value"),
                null,
                $"luotsi replay graph --artifacts {Quote(artifactRoot)} --selector {Quote(node.Label)} --write-markdown",
                edgeIds),
            "screen_state" => new ReplayGraphEvidenceResult(
                "screen_state",
                node.Id,
                node.Label,
                GetProperty(node, "screenshot_path") ?? GetProperty(node, "detail"),
                GetProperty(node, "screenshot_path"),
                null,
                edgeIds),
            "telemetry_signal" => new ReplayGraphEvidenceResult(
                "telemetry_signal",
                node.Id,
                node.Label,
                GetProperty(node, "value") ?? GetProperty(node, "detail"),
                null,
                $"luotsi replay graph --artifacts {Quote(artifactRoot)} --node {Quote(node.Id)} --depth 2 --write-markdown",
                edgeIds),
            "generated_step" => new ReplayGraphEvidenceResult(
                "generated_step",
                node.Id,
                node.Label,
                GetProperty(node, "action") ?? GetProperty(node, "source"),
                null,
                $"luotsi replay graph --artifacts {Quote(artifactRoot)} --node {Quote(node.Id)} --depth 2 --write-markdown",
                edgeIds),
            _ => null
        };
    }

    private static IReadOnlyList<string> EdgeIdsFor(string nodeId, IReadOnlyList<ReplayGraphEdgeResult> edges) =>
        edges
            .Where(edge => string.Equals(edge.From, nodeId, StringComparison.Ordinal) || string.Equals(edge.To, nodeId, StringComparison.Ordinal))
            .Select(static edge => edge.From + " -> " + edge.Kind + " -> " + edge.To)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .Take(8)
            .ToArray();

    private static string? GetProperty(ReplayGraphNodeResult node, string name) =>
        node.Properties.TryGetValue(name, out var value) ? value : null;

    private static int Rank(string kind) => kind switch
    {
        "failure" => 0,
        "artifact" => 1,
        "screen_state" => 2,
        "selector" => 3,
        "telemetry_signal" => 4,
        "generated_step" => 5,
        _ => 10
    };

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;
}
