namespace Luotsi.Cli.Artifacts;

internal sealed class ArtifactReplayWorkflowCommands(string root)
{
    private readonly string _root = root ?? throw new ArgumentNullException(nameof(root));

    public IReadOnlyList<ReplayWorkflowCommandModel> Build(IReadOnlyList<SessionReplaySummary> replaySummaries)
    {
        ArgumentNullException.ThrowIfNull(replaySummaries);

        var commands = new List<ReplayWorkflowCommandModel>
        {
            new(
                "PACKET",
                $"luotsi replay packet --artifacts {ArtifactIndexPaths.Quote(_root)}",
                "Start here: write run-summary.json and run-summary.md with the first-minute triage packet."),
            new(
                "CHECK",
                $"luotsi replay packet --artifacts {ArtifactIndexPaths.Quote(_root)} --check",
                "Validate the durable packet before handing it to CI, support, or another agent."),
            new(
                "CAPSULE",
                $"luotsi replay capsule --artifacts {ArtifactIndexPaths.Quote(_root)} --write-readme --write-json",
                "Write the bundle summary, primary failure, artifact manifest, and recommended replay next steps."),
            new(
                "OPEN",
                $"luotsi replay open --artifacts {ArtifactIndexPaths.Quote(_root)}",
                "Open the replay front door when a human also needs the browser-backed workbench.")
        };

        if (replaySummaries.Any(static summary => summary.HasFailureSignals))
        {
            commands.Add(new ReplayWorkflowCommandModel(
                "SCRUB",
                $"luotsi replay scrub --artifacts {ArtifactIndexPaths.Quote(_root)} --failures --context 3 --write-markdown",
                "Review the focused failure window with previous/current/next timeline events."));
            commands.Add(new ReplayWorkflowCommandModel(
                "GRAPH",
                $"luotsi replay graph --artifacts {ArtifactIndexPaths.Quote(_root)} --failed --write-json --write-markdown",
                "Open semantic failure context with evidence, facts, causal chains, and hypotheses."));
            commands.Add(new ReplayWorkflowCommandModel(
                "CLUSTER",
                $"luotsi replay cluster --artifacts {ArtifactIndexPaths.Quote(ArtifactIndexPaths.ResolveClusterRoot(_root))} --min-count 2 --write-markdown",
                "Look for matching failure shapes across sibling replay bundles."));
        }

        return commands;
    }
}
