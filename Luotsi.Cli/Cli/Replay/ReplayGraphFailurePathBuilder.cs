using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal static class ReplayGraphFailurePathBuilder
{
    private const int MaxTransitionDepth = 5;

    public static IReadOnlyList<ReplayGraphFailurePathResult> Build(
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges)
    {
        var nodeById = nodes.ToDictionary(static node => node.Id, StringComparer.Ordinal);
        var incomingByTo = edges
            .GroupBy(static edge => edge.To, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var paths = new List<ReplayGraphFailurePathResult>();

        foreach (var failure in nodes.Where(ReplayGraphPredicates.IsFailureNode).OrderBy(static node => node.Id, StringComparer.Ordinal))
        {
            var indicates = incomingByTo.TryGetValue(failure.Id, out var incoming)
                ? incoming.FirstOrDefault(static edge => string.Equals(edge.Kind, "indicates", StringComparison.Ordinal))
                : null;
            if (indicates is null)
            {
                continue;
            }

            var pathNodeIds = new List<string> { failure.Id };
            var pathEdgeIds = new List<string> { EdgeId(indicates) };
            var current = indicates.From;
            pathNodeIds.Add(current);

            for (var depth = 0; depth < MaxTransitionDepth; depth++)
            {
                if (!incomingByTo.TryGetValue(current, out var currentIncoming))
                {
                    break;
                }

                var transition = currentIncoming.FirstOrDefault(static edge => string.Equals(edge.Kind, "transitions_to", StringComparison.Ordinal));
                if (transition is null)
                {
                    break;
                }

                pathEdgeIds.Add(EdgeId(transition));
                current = transition.From;
                pathNodeIds.Add(current);
            }

            pathNodeIds.Reverse();
            pathEdgeIds.Reverse();
            paths.Add(new ReplayGraphFailurePathResult(
                failure.Id,
                indicates.From,
                BuildSummary(pathNodeIds, nodeById),
                pathNodeIds,
                pathEdgeIds));
        }

        return paths;
    }

    private static string BuildSummary(IReadOnlyList<string> pathNodeIds, IReadOnlyDictionary<string, ReplayGraphNodeResult> nodeById)
    {
        var labels = pathNodeIds
            .Select(id => nodeById.TryGetValue(id, out var node) ? Label(node) : id)
            .Where(static label => !string.IsNullOrWhiteSpace(label));
        return string.Join(" -> ", labels);
    }

    private static string Label(ReplayGraphNodeResult node)
    {
        if (string.Equals(node.Kind, "event", StringComparison.Ordinal))
        {
            return node.Label;
        }

        if (string.Equals(node.Kind, "failure", StringComparison.Ordinal))
        {
            return "failure:" + node.Label;
        }

        return node.Kind + ":" + node.Label;
    }

    private static string EdgeId(ReplayGraphEdgeResult edge) => edge.From + " -> " + edge.Kind + " -> " + edge.To;
}
