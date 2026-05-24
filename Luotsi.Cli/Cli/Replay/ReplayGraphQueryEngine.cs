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

        var severity = NormalizeBlank(options.Get("severity"));
        if (severity is not null &&
            !string.Equals(severity, "info", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(severity, "warning", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("replay graph --severity must be info, warning, or error.");
        }

        return new ReplayGraphQueryResult(
            NormalizeBlank(options.Get("node-kind")),
            NormalizeBlank(options.Get("edge-kind")),
            NormalizeBlank(options.Get("action")),
            NormalizeBlank(options.Get("selector")),
            NormalizeBlank(options.Get("contains")),
            NormalizeBlank(options.Get("insight")),
            severity,
            NormalizeBlank(options.Get("evidence")),
            NormalizeBlank(options.Get("node")),
            options.Int("depth", 1),
            options.HasFlag("failed"),
            limit);
    }

    public static ReplayGraphView Apply(
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges,
        ReplayGraphQueryResult query)
    {
        if (query.Depth < 0)
        {
            throw new UsageException("replay graph requires --depth greater than or equal to zero.");
        }

        var filteredNodes = query.Node is null
            ? nodes.Where(node => MatchesNode(node, query)).ToArray()
            : SelectNeighborhood(nodes, edges, query.Node, query.Depth);
        var nodeIds = filteredNodes.Select(static node => node.Id).ToHashSet(StringComparer.Ordinal);
        var filteredEdges = edges
            .Where(edge => MatchesEdge(edge, query))
            .Where(edge => nodeIds.Contains(edge.From) || nodeIds.Contains(edge.To))
            .ToArray();

        if ((query.EdgeKind is not null || query.Contains is not null) &&
            query.NodeKind is null &&
            query.Action is null &&
            query.Selector is null &&
            query.Node is null &&
            !query.FailedOnly)
        {
            var edgeNodeIds = filteredEdges.SelectMany(static edge => new[] { edge.From, edge.To }).ToHashSet(StringComparer.Ordinal);
            foreach (var node in filteredNodes)
            {
                edgeNodeIds.Add(node.Id);
            }

            filteredNodes = nodes.Where(node => edgeNodeIds.Contains(node.Id)).ToArray();
            nodeIds = filteredNodes.Select(static node => node.Id).ToHashSet(StringComparer.Ordinal);
        }

        filteredNodes = query.Node is null
            ? ExpandOneHopContext(nodes, edges, filteredNodes, nodeIds, query)
            : filteredNodes;
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
        AddQueryPart(parts, "contains", query.Contains);
        AddQueryPart(parts, "insight", query.Insight);
        AddQueryPart(parts, "severity", query.Severity);
        AddQueryPart(parts, "evidence", query.Evidence);
        AddQueryPart(parts, "node", query.Node);
        if (query.Node is not null)
        {
            parts.Add("depth=" + query.Depth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

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
        if (query.NodeKind is null && query.Action is null && query.Selector is null && query.Node is null && !query.FailedOnly)
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

    private static ReplayGraphNodeResult[] SelectNeighborhood(
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges,
        string nodeId,
        int depth)
    {
        var nodeById = nodes.ToDictionary(static node => node.Id, StringComparer.Ordinal);
        if (!nodeById.ContainsKey(nodeId))
        {
            throw new UsageException($"replay graph --node '{nodeId}' did not match any graph node.");
        }

        var selected = new HashSet<string>(StringComparer.Ordinal) { nodeId };
        var frontier = new HashSet<string>(StringComparer.Ordinal) { nodeId };
        for (var currentDepth = 0; currentDepth < depth; currentDepth++)
        {
            var next = new HashSet<string>(StringComparer.Ordinal);
            foreach (var edge in edges)
            {
                if (frontier.Contains(edge.From) && selected.Add(edge.To))
                {
                    next.Add(edge.To);
                }

                if (frontier.Contains(edge.To) && selected.Add(edge.From))
                {
                    next.Add(edge.From);
                }
            }

            if (next.Count == 0)
            {
                break;
            }

            frontier = next;
        }

        return nodes.Where(node => selected.Contains(node.Id)).ToArray();
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

        if (query.Contains is not null && !NodeContains(node, query.Contains))
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
        (query.EdgeKind is null || string.Equals(edge.Kind, query.EdgeKind, StringComparison.OrdinalIgnoreCase)) &&
        (query.Contains is null || EdgeContains(edge, query.Contains));

    private static bool NodeContains(ReplayGraphNodeResult node, string value) =>
        ReplayGraphPredicates.Contains(node.Id, value) ||
        ReplayGraphPredicates.Contains(node.Kind, value) ||
        ReplayGraphPredicates.Contains(node.Label, value) ||
        node.Properties.Any(property =>
            ReplayGraphPredicates.Contains(property.Key, value) ||
            ReplayGraphPredicates.Contains(property.Value, value));

    private static bool EdgeContains(ReplayGraphEdgeResult edge, string value) =>
        ReplayGraphPredicates.Contains(edge.From, value) ||
        ReplayGraphPredicates.Contains(edge.To, value) ||
        ReplayGraphPredicates.Contains(edge.Kind, value) ||
        edge.Properties.Any(property =>
            ReplayGraphPredicates.Contains(property.Key, value) ||
            ReplayGraphPredicates.Contains(property.Value, value));

    private static bool PropertyContains(ReplayGraphNodeResult node, string propertyName, string value) =>
        node.Properties.Any(property =>
            property.Key.EndsWith(propertyName, StringComparison.OrdinalIgnoreCase) &&
            ReplayGraphPredicates.Contains(property.Value, value));

    public static IReadOnlyList<ReplayGraphInsightResult> ApplyInsightFilters(
        IReadOnlyList<ReplayGraphInsightResult> insights,
        ReplayGraphQueryResult query)
    {
        if (query.Insight is null && query.Severity is null)
        {
            return insights;
        }

        return insights
            .Where(insight => query.Insight is null || ReplayGraphPredicates.Contains(insight.Kind, query.Insight))
            .Where(insight => query.Severity is null || string.Equals(insight.Severity, query.Severity, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static IReadOnlyList<ReplayGraphEvidenceResult> ApplyEvidenceFilters(
        IReadOnlyList<ReplayGraphEvidenceResult> evidence,
        ReplayGraphQueryResult query)
    {
        if (query.Evidence is null)
        {
            return evidence;
        }

        return evidence
            .Where(item => ReplayGraphPredicates.Contains(item.Kind, query.Evidence))
            .ToArray();
    }

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
