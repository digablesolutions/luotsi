using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal static class ReplayGraphInsightBuilder
{
    public static IReadOnlyList<ReplayGraphInsightResult> Build(
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges)
    {
        var insights = new List<ReplayGraphInsightResult>();
        AddFailureInsight(nodes, insights);
        AddSelectorInsight(nodes, edges, insights);
        AddTelemetryInsight(nodes, insights);
        AddScenarioDraftInsight(nodes, insights);
        return insights;
    }

    public static IReadOnlyList<ReplayGraphActionResult> BuildActions(
        string artifactRoot,
        ReplayGraphQueryResult query,
        IReadOnlyList<ReplayGraphNodeResult> nodes)
    {
        var actions = new List<ReplayGraphActionResult>
        {
            new("open_artifacts", "Open the browser index for screenshots, logs, reports, graph, and replay files.", $"luotsi replay open --artifacts {Quote(artifactRoot)} --dry-run"),
            new("scrub_failures", "Review the failure timeline with previous/focused/next context.", $"luotsi replay scrub --artifacts {Quote(artifactRoot)} --failures --context 3 --write-markdown")
        };

        if (!query.FailedOnly && nodes.Any(ReplayGraphPredicates.IsFailureNode))
        {
            actions.Add(new ReplayGraphActionResult("filter_failures", "Narrow graph output to failure-relevant nodes and their local context.", $"luotsi replay graph --artifacts {Quote(artifactRoot)} --failed --write-markdown"));
        }

        if (nodes.Any(static node => string.Equals(node.Kind, "selector", StringComparison.Ordinal)))
        {
            actions.Add(new ReplayGraphActionResult("filter_selectors", "List promoted selector nodes and the actions/events that mention them.", $"luotsi replay graph --artifacts {Quote(artifactRoot)} --node-kind selector --write-markdown"));
        }

        if (nodes.Any(static node => string.Equals(node.Kind, "scenario_draft", StringComparison.Ordinal)))
        {
            actions.Add(new ReplayGraphActionResult("audit_draft", "Inspect generated scenario draft provenance.", $"luotsi replay graph --artifacts {Quote(artifactRoot)} --node-kind generated_step --write-markdown"));
        }

        return actions;
    }

    private static void AddFailureInsight(IReadOnlyList<ReplayGraphNodeResult> nodes, ICollection<ReplayGraphInsightResult> insights)
    {
        var failures = nodes.Where(ReplayGraphPredicates.IsFailureNode).Take(8).ToArray();
        if (failures.Length == 0)
        {
            return;
        }

        insights.Add(new ReplayGraphInsightResult(
            "failure",
            "error",
            $"Graph contains {failures.Length} failure-relevant node(s). Start with `replay graph --failed --write-markdown` or `replay scrub --failures`.",
            failures.Select(static node => node.Id).ToArray(),
            []));
    }

    private static void AddSelectorInsight(
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges,
        ICollection<ReplayGraphInsightResult> insights)
    {
        var selectors = nodes.Where(static node => string.Equals(node.Kind, "selector", StringComparison.Ordinal)).Take(8).ToArray();
        if (selectors.Length == 0)
        {
            return;
        }

        insights.Add(new ReplayGraphInsightResult(
            "selector",
            "info",
            $"Graph promotes {selectors.Length} selector node(s) that agents can map back to actions and failures.",
            selectors.Select(static node => node.Id).ToArray(),
            edges.Where(edge => selectors.Any(selector => edge.From == selector.Id || edge.To == selector.Id)).Select(EdgeId).Take(8).ToArray()));
    }

    private static void AddTelemetryInsight(IReadOnlyList<ReplayGraphNodeResult> nodes, ICollection<ReplayGraphInsightResult> insights)
    {
        var telemetry = nodes.Where(static node => string.Equals(node.Kind, "telemetry_signal", StringComparison.Ordinal)).Take(8).ToArray();
        if (telemetry.Length == 0)
        {
            return;
        }

        insights.Add(new ReplayGraphInsightResult(
            "telemetry",
            "info",
            $"Graph includes {telemetry.Length} telemetry signal node(s) for semantic assertions and waits.",
            telemetry.Select(static node => node.Id).ToArray(),
            []));
    }

    private static void AddScenarioDraftInsight(IReadOnlyList<ReplayGraphNodeResult> nodes, ICollection<ReplayGraphInsightResult> insights)
    {
        var drafts = nodes.Where(static node => string.Equals(node.Kind, "scenario_draft", StringComparison.Ordinal)).Take(8).ToArray();
        if (drafts.Length == 0)
        {
            return;
        }

        insights.Add(new ReplayGraphInsightResult(
            "scenario_draft",
            "info",
            "Scenario draft provenance is present; use generated_step and draft_source edges to audit where each step came from.",
            drafts.Select(static node => node.Id).ToArray(),
            []));
    }

    private static string EdgeId(ReplayGraphEdgeResult edge) => edge.From + " -> " + edge.Kind + " -> " + edge.To;

    private static string Quote(string value) => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
