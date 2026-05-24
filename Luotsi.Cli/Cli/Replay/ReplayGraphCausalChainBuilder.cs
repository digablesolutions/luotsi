using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal static class ReplayGraphCausalChainBuilder
{
    public static IReadOnlyList<ReplayGraphCausalChainResult> Build(
        string artifactRoot,
        IReadOnlyList<ReplayGraphFailurePathResult> failurePaths,
        IReadOnlyList<ReplayGraphEdgeResult> edges)
    {
        var edgeById = edges.ToDictionary(EdgeId, StringComparer.Ordinal);
        return failurePaths
            .Select(path => BuildChain(artifactRoot, path, edgeById))
            .Where(static chain => chain.Hops.Count > 0)
            .Take(20)
            .ToArray();
    }

    private static ReplayGraphCausalChainResult BuildChain(
        string artifactRoot,
        ReplayGraphFailurePathResult path,
        IReadOnlyDictionary<string, ReplayGraphEdgeResult> edgeById)
    {
        var hops = new List<ReplayGraphCausalHopResult>();
        foreach (var edgeId in path.EdgeIds)
        {
            if (!edgeById.TryGetValue(edgeId, out var edge))
            {
                continue;
            }

            hops.Add(new ReplayGraphCausalHopResult(
                edge.From,
                edge.To,
                edge.Kind,
                GetProperty(edge, "category"),
                GetProperty(edge, "to_detail") ?? GetProperty(edge, "from_detail")));
        }

        return new ReplayGraphCausalChainResult(
            path.FailureNodeId,
            path.Summary,
            hops,
            $"luotsi replay graph --artifacts {Quote(artifactRoot)} --node {Quote(path.FailureNodeId)} --depth 2 --write-markdown");
    }

    private static string? GetProperty(ReplayGraphEdgeResult edge, string name) =>
        edge.Properties.TryGetValue(name, out var value) ? value : null;

    private static string EdgeId(ReplayGraphEdgeResult edge) =>
        edge.From + " -> " + edge.Kind + " -> " + edge.To;

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;
}
