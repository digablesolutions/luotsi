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
        AddTransitionInsight(edges, insights);
        return insights;
    }

    public static IReadOnlyList<ReplayGraphActionResult> BuildActions(
        string artifactRoot,
        ReplayGraphQueryResult query,
        IReadOnlyList<ReplayGraphNodeResult> nodes)
    {
        var actions = new List<ReplayGraphActionResult>
        {
            new("write_replay_packet", "Write run-summary.json and run-summary.md for the durable first-minute packet.", $"luotsi replay packet --artifacts {Quote(artifactRoot)}"),
            new("check_replay_packet", "Validate the durable first-minute packet before handoff.", $"luotsi replay packet --artifacts {Quote(artifactRoot)} --check"),
            new("open_replay_front_door", "Open the replay front door with primary failure, artifacts, and recommended next steps.", $"luotsi replay open --artifacts {Quote(artifactRoot)} --dry-run"),
            new("write_replay_capsule", "Write the replay capsule README and JSON summary.", $"luotsi replay capsule --artifacts {Quote(artifactRoot)} --write-readme --write-json"),
            new("scrub_failures", "Review the failure timeline with previous/focused/next context.", $"luotsi replay scrub --artifacts {Quote(artifactRoot)} --failures --context 3 --write-markdown"),
            new("stream_graph", "Emit line-oriented graph output for CI and agent consumers.", $"luotsi replay graph --artifacts {Quote(artifactRoot)} --format jsonl")
        };

        if (!query.FailedOnly && nodes.Any(ReplayGraphPredicates.IsFailureNode))
        {
            actions.Add(new ReplayGraphActionResult("filter_failures", "Narrow graph output to failure-relevant nodes and their local context.", $"luotsi replay graph --artifacts {Quote(artifactRoot)} --failed --write-markdown"));
        }

        if (nodes.Any(static node => string.Equals(node.Kind, "selector", StringComparison.Ordinal)))
        {
            actions.Add(new ReplayGraphActionResult("filter_selectors", "List promoted selector nodes and the actions/events that mention them.", $"luotsi replay graph --artifacts {Quote(artifactRoot)} --node-kind selector --write-markdown"));
        }

        if (nodes.Any(static node => string.Equals(node.Kind, "artifact", StringComparison.Ordinal)))
        {
            actions.Add(new ReplayGraphActionResult("filter_artifact_evidence", "List promoted artifact evidence records without hiding graph context.", $"luotsi replay graph --artifacts {Quote(artifactRoot)} --evidence artifact --format jsonl"));
        }

        if (nodes.Any(static node => string.Equals(node.Kind, "scenario_draft", StringComparison.Ordinal)))
        {
            actions.Add(new ReplayGraphActionResult("audit_draft", "Inspect generated scenario draft provenance.", $"luotsi replay graph --artifacts {Quote(artifactRoot)} --node-kind scenario_draft --write-markdown"));
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
            "Scenario draft provenance is present; use generated_step, draft_normalization, and draft_source edges to audit where draft changes came from.",
            drafts.Select(static node => node.Id).ToArray(),
            []));
    }

    private static void AddTransitionInsight(IReadOnlyList<ReplayGraphEdgeResult> edges, ICollection<ReplayGraphInsightResult> insights)
    {
        var failureTransitions = edges
            .Where(static edge => string.Equals(edge.Kind, "transitions_to", StringComparison.Ordinal) &&
                edge.Properties.TryGetValue("category", out var category) &&
                string.Equals(category, "action_to_failure", StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToArray();
        if (failureTransitions.Length == 0)
        {
            return;
        }

        insights.Add(new ReplayGraphInsightResult(
            "transition",
            "warning",
            $"Graph contains {failureTransitions.Length} action-to-failure transition(s) that connect preceding actions to failure events.",
            [],
            failureTransitions.Select(EdgeId).ToArray()));
    }

    private static string EdgeId(ReplayGraphEdgeResult edge) => edge.From + " -> " + edge.Kind + " -> " + edge.To;

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;
}
