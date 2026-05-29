using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal static class ReplayGraphHypothesisBuilder
{
    public static IReadOnlyList<ReplayGraphHypothesisResult> Build(
        string artifactRoot,
        IReadOnlyList<ReplayGraphCausalChainResult> causalChains,
        IReadOnlyList<ReplayGraphEvidenceResult> evidence)
    {
        var hypotheses = new List<ReplayGraphHypothesisResult>();
        AddActionFailureHypotheses(artifactRoot, causalChains, hypotheses);
        AddFailureEvidenceHypotheses(artifactRoot, evidence, hypotheses);

        return hypotheses
            .OrderByDescending(static hypothesis => hypothesis.Confidence)
            .ThenBy(static hypothesis => hypothesis.Kind, StringComparer.Ordinal)
            .ThenBy(static hypothesis => hypothesis.Summary, StringComparer.Ordinal)
            .Take(20)
            .ToArray();
    }

    private static void AddActionFailureHypotheses(
        string artifactRoot,
        IReadOnlyList<ReplayGraphCausalChainResult> causalChains,
        List<ReplayGraphHypothesisResult> hypotheses)
    {
        foreach (var chain in causalChains)
        {
            var actionToFailure = chain.Hops.FirstOrDefault(static hop =>
                string.Equals(hop.Category, "action_to_failure", StringComparison.OrdinalIgnoreCase));
            if (actionToFailure is null)
            {
                continue;
            }

            hypotheses.Add(new ReplayGraphHypothesisResult(
                "action_to_failure",
                "warning",
                $"The transition into {chain.FailureNodeId} followed an action event; inspect that action and its selector before changing waits.",
                0.88,
                [chain.FailureNodeId, actionToFailure.From, actionToFailure.To],
                [actionToFailure.From + " -> " + actionToFailure.Relation + " -> " + actionToFailure.To],
                chain.Command ?? $"luotsi replay graph --artifacts {Quote(artifactRoot)} --node {Quote(chain.FailureNodeId)} --depth 2 --write-markdown"));
        }
    }

    private static void AddFailureEvidenceHypotheses(
        string artifactRoot,
        IReadOnlyList<ReplayGraphEvidenceResult> evidence,
        List<ReplayGraphHypothesisResult> hypotheses)
    {
        var failures = evidence
            .Where(static item => string.Equals(item.Kind, "failure", StringComparison.Ordinal))
            .Take(10);
        foreach (var failure in failures)
        {
            hypotheses.Add(new ReplayGraphHypothesisResult(
                "failure_evidence",
                "error",
                string.IsNullOrWhiteSpace(failure.Detail)
                    ? $"Failure evidence is available at {failure.NodeId}."
                    : failure.Detail!,
                0.82,
                [failure.NodeId],
                failure.EdgeIds,
                failure.Command ?? $"luotsi replay graph --artifacts {Quote(artifactRoot)} --node {Quote(failure.NodeId)} --depth 2 --write-markdown"));
        }
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;
}
