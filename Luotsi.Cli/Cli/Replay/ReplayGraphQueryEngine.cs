using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal static class ReplayGraphQueryEngine
{
    public const int DefaultLimit = 200;

    public static ReplayGraphQueryResult Create(CliOptions options)
    {
        var limit = options.Int("limit", DefaultLimit);
        if (limit <= 0)
        {
            throw new UsageException("replay graph requires --limit greater than zero.");
        }

        return new ReplayGraphQueryResult(
            NormalizeBlank(options.Get("node-kind")),
            NormalizeBlank(options.Get("edge-kind")),
            NormalizeBlank(options.Get("action")),
            NormalizeBlank(options.Get("selector")),
            options.HasFlag("failed"),
            limit);
    }

    public static ReplayGraphView Apply(
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges,
        ReplayGraphQueryResult query)
    {
        var filteredNodes = nodes.Where(node => MatchesNode(node, query)).ToArray();
        var nodeIds = filteredNodes.Select(static node => node.Id).ToHashSet(StringComparer.Ordinal);
        var filteredEdges = edges
            .Where(edge => MatchesEdge(edge, query))
            .Where(edge => nodeIds.Contains(edge.From) || nodeIds.Contains(edge.To))
            .ToArray();

        if (query.EdgeKind is not null && query.NodeKind is null && query.Action is null && query.Selector is null && !query.FailedOnly)
        {
            var edgeNodeIds = filteredEdges.SelectMany(static edge => new[] { edge.From, edge.To }).ToHashSet(StringComparer.Ordinal);
            filteredNodes = nodes.Where(node => edgeNodeIds.Contains(node.Id)).ToArray();
            nodeIds = filteredNodes.Select(static node => node.Id).ToHashSet(StringComparer.Ordinal);
        }

        filteredNodes = ExpandOneHopContext(nodes, edges, filteredNodes, nodeIds, query);
        nodeIds = filteredNodes.Select(static node => node.Id).ToHashSet(StringComparer.Ordinal);
        filteredEdges = edges
            .Where(edge => MatchesEdge(edge, query) || query.EdgeKind is null)
            .Where(edge => nodeIds.Contains(edge.From) && nodeIds.Contains(edge.To))
            .ToArray();

        var matchedNodeCount = filteredNodes.Length;
        var matchedEdgeCount = filteredEdges.Length;
        return new ReplayGraphView(
            filteredNodes.Take(query.Limit).ToArray(),
            filteredEdges.Take(query.Limit).ToArray(),
            matchedNodeCount,
            matchedEdgeCount,
            matchedNodeCount > query.Limit || matchedEdgeCount > query.Limit);
    }

    public static string Describe(ReplayGraphQueryResult query)
    {
        var parts = new List<string>();
        AddQueryPart(parts, "node-kind", query.NodeKind);
        AddQueryPart(parts, "edge-kind", query.EdgeKind);
        AddQueryPart(parts, "action", query.Action);
        AddQueryPart(parts, "selector", query.Selector);
        if (query.FailedOnly)
        {
            parts.Add("failed=true");
        }

        parts.Add("limit=" + query.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return string.Join(", ", parts);
    }

    private static ReplayGraphNodeResult[] ExpandOneHopContext(
        IReadOnlyList<ReplayGraphNodeResult> allNodes,
        IReadOnlyList<ReplayGraphEdgeResult> allEdges,
        IReadOnlyList<ReplayGraphNodeResult> filteredNodes,
        HashSet<string> nodeIds,
        ReplayGraphQueryResult query)
    {
        if (query.NodeKind is null && query.Action is null && query.Selector is null && !query.FailedOnly)
        {
            return filteredNodes.ToArray();
        }

        var contextIds = new HashSet<string>(nodeIds, StringComparer.Ordinal);
        foreach (var edge in allEdges)
        {
            if (nodeIds.Contains(edge.From))
            {
                contextIds.Add(edge.To);
            }

            if (nodeIds.Contains(edge.To))
            {
                contextIds.Add(edge.From);
            }
        }

        return allNodes.Where(node => contextIds.Contains(node.Id)).ToArray();
    }

    private static bool MatchesNode(ReplayGraphNodeResult node, ReplayGraphQueryResult query)
    {
        if (query.NodeKind is not null && !string.Equals(node.Kind, query.NodeKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.Action is not null && !ReplayGraphPredicates.Contains(node.Label, query.Action) && !PropertyContains(node, "action", query.Action))
        {
            return false;
        }

        if (query.Selector is not null && !ReplayGraphPredicates.Contains(node.Label, query.Selector) && !PropertyContains(node, "value", query.Selector))
        {
            return false;
        }

        if (query.FailedOnly && !ReplayGraphPredicates.IsFailureNode(node))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesEdge(ReplayGraphEdgeResult edge, ReplayGraphQueryResult query) =>
        query.EdgeKind is null || string.Equals(edge.Kind, query.EdgeKind, StringComparison.OrdinalIgnoreCase);

    private static bool PropertyContains(ReplayGraphNodeResult node, string propertyName, string value) =>
        node.Properties.Any(property =>
            property.Key.EndsWith(propertyName, StringComparison.OrdinalIgnoreCase) &&
            ReplayGraphPredicates.Contains(property.Value, value));

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddQueryPart(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(name + "=" + value);
        }
    }
}

internal sealed record ReplayGraphView(
    IReadOnlyList<ReplayGraphNodeResult> Nodes,
    IReadOnlyList<ReplayGraphEdgeResult> Edges,
    int MatchedNodeCount,
    int MatchedEdgeCount,
    bool Truncated);
