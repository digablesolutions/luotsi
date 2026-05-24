using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal static class ReplayGraphAgentSummaryBuilder
{
    public static ReplayGraphAgentSummaryResult Build(
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges,
        IReadOnlyList<ReplayGraphFailurePathResult> failurePaths,
        IReadOnlyList<ReplayGraphEvidenceResult> evidence,
        IReadOnlyList<ReplayGraphActionResult> actions)
    {
        var failures = nodes.Where(ReplayGraphPredicates.IsFailureNode).Take(5).ToArray();
        var transitions = edges
            .Where(static edge => string.Equals(edge.Kind, "transitions_to", StringComparison.Ordinal))
            .Take(5)
            .ToArray();
        var actionToFailureTransitions = transitions
            .Where(static edge => edge.Properties.TryGetValue("category", out var category) &&
                string.Equals(category, "action_to_failure", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new ReplayGraphAgentSummaryResult(
            BuildFailureSummary(failures, failurePaths),
            BuildChangeSummary(transitions, actionToFailureTransitions),
            BuildActionSummary(actions),
            failures.Select(static failure => failure.Id).ToArray(),
            transitions.Select(EdgeId).ToArray(),
            evidence.Select(static item => item.NodeId).Distinct(StringComparer.Ordinal).Take(8).ToArray(),
            actions.Select(static action => action.Command).Where(static command => !string.IsNullOrWhiteSpace(command)).Cast<string>().Take(5).ToArray());
    }

    private static string BuildFailureSummary(
        IReadOnlyList<ReplayGraphNodeResult> failures,
        IReadOnlyList<ReplayGraphFailurePathResult> failurePaths)
    {
        if (failures.Count == 0)
        {
            return "No failure-relevant graph nodes were found.";
        }

        var primary = failurePaths.FirstOrDefault()?.Summary;
        return string.IsNullOrWhiteSpace(primary)
            ? $"Found {failures.Count} failure-relevant graph node(s)."
            : primary;
    }

    private static string BuildChangeSummary(
        IReadOnlyList<ReplayGraphEdgeResult> transitions,
        IReadOnlyList<ReplayGraphEdgeResult> actionToFailureTransitions)
    {
        if (transitions.Count == 0)
        {
            return "No semantic timeline transitions were available in this graph view.";
        }

        if (actionToFailureTransitions.Count > 0)
        {
            return $"Found {actionToFailureTransitions.Count} action-to-failure transition(s); inspect `failure_paths` or query `--edge-kind transitions_to`.";
        }

        return $"Found {transitions.Count} semantic transition(s) across timeline events.";
    }

    private static string BuildActionSummary(IReadOnlyList<ReplayGraphActionResult> actions)
    {
        var firstCommand = actions.Select(static action => action.Command).FirstOrDefault(static command => !string.IsNullOrWhiteSpace(command));
        return string.IsNullOrWhiteSpace(firstCommand)
            ? "No follow-up graph commands were suggested."
            : "Start with: " + firstCommand;
    }

    private static string EdgeId(ReplayGraphEdgeResult edge) => edge.From + " -> " + edge.Kind + " -> " + edge.To;
}
