using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Routing;

internal sealed class ReplayCommandDispatcher(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public Task<ReplaySummarizeResult> ExecuteAsync(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var args = options.Arguments;
        if (args.Count == 0)
        {
            throw new UsageException("replay requires subcommand summarize.");
        }

        return args[0].ToLowerInvariant() switch
        {
            "summarize" => Task.FromResult(Summarize(options)),
            _ => throw new UsageException($"Unknown replay subcommand '{string.Join(" ", args)}'.")
        };
    }

    private ReplaySummarizeResult Summarize(CliOptions options)
    {
        var artifactRoot = options.Get("artifacts") ?? throw new UsageException("replay summarize requires --artifacts <directory> pointing to an existing artifact root.");
        if (!_fileSystem.DirectoryExists(artifactRoot))
        {
            throw new UsageException($"Artifact root '{artifactRoot}' does not exist.");
        }

        var files = _fileSystem.GetFiles(artifactRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(artifactRoot, path))
            .ToArray();
        var summaries = new SessionReplaySummaryReader(artifactRoot, _fileSystem).ReadSummaries(files);
        if (summaries.Count == 0)
        {
            throw new UsageException($"No session replay metadata was found under artifact root '{artifactRoot}'.");
        }

        return new ReplaySummarizeResult(
            ResultSchemas.SessionReplaySummary,
            artifactRoot,
            summaries.Count,
            summaries.Count(static summary => summary.HasFailureSignals),
            CreateCommandHints(artifactRoot, summaries),
            summaries.Select(static summary => new ReplaySessionSummaryResult(
                summary.MetadataPath,
                summary.TimelinePath,
                summary.FailureCapsulePath,
                ToFailureCapsuleResult(summary.FailureCapsulePath, summary.FailureCapsule),
                summary.SessionKind,
                summary.SessionId,
                summary.StartedAt,
                summary.EndedAt,
                (long)(summary.EndedAt - summary.StartedAt).TotalMilliseconds,
                summary.Reason,
                summary.ExitCode,
                summary.Target,
                summary.EventCount,
                summary.EventTypes,
                summary.HasTimeline,
                summary.HasFailureSignals,
                summary.TimelineHighlights.Select(static entry => new ReplayTimelineHighlightResult(
                    entry.Timestamp,
                    entry.Type,
                    entry.Detail,
                    entry.IsFailureRelevant)).ToArray())).ToArray());
    }

    private static IReadOnlyList<ReplaySummaryCommandHintResult> CreateCommandHints(
        string artifactRoot,
        IReadOnlyCollection<SessionReplaySummary> summaries)
    {
        var commands = new List<ReplaySummaryCommandHintResult>
        {
            new(
                "write_replay_packet",
                "Write the durable first-minute packet for the artifact root.",
                $"luotsi replay packet --artifacts {Quote(artifactRoot)}"),
            new(
                "check_replay_packet",
                "Validate the durable packet before handoff or deeper replay.",
                $"luotsi replay packet --artifacts {Quote(artifactRoot)} --check"),
            new(
                "open_replay_front_door",
                "Open the replay front door for the artifact root.",
                $"luotsi replay open --artifacts {Quote(artifactRoot)}"),
            new(
                "write_replay_capsule",
                "Write the replay capsule README and JSON summary.",
                $"luotsi replay capsule --artifacts {Quote(artifactRoot)} --write-readme --write-json")
        };

        if (summaries.Any(static summary => summary.HasFailureSignals))
        {
            commands.Add(new ReplaySummaryCommandHintResult(
                "scrub_failures",
                "Scrub failure-relevant timeline events with nearby context.",
                $"luotsi replay scrub --artifacts {Quote(artifactRoot)} --failures --context 3 --write-markdown"));
            commands.Add(new ReplaySummaryCommandHintResult(
                "graph_failures",
                "Build a focused semantic graph over failed replay evidence.",
                $"luotsi replay graph --artifacts {Quote(artifactRoot)} --failed --write-json --write-markdown"));
            commands.Add(new ReplaySummaryCommandHintResult(
                "cluster_failures",
                "Compare this artifact root with nearby replay bundles for repeated failure shapes.",
                $"luotsi replay cluster --artifacts {Quote(ResolveClusterRoot(artifactRoot))} --min-count 2 --write-markdown"));
        }

        return commands;
    }

    private static string ResolveClusterRoot(string artifactRoot)
    {
        var parent = Path.GetDirectoryName(artifactRoot);
        return string.IsNullOrWhiteSpace(parent) ? artifactRoot : parent;
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static ReplayFailureCapsuleResult? ToFailureCapsuleResult(string? failureCapsulePath, FailureCapsuleManifest? failureCapsule)
    {
        if (string.IsNullOrWhiteSpace(failureCapsulePath) || failureCapsule is null)
        {
            return null;
        }

        return new ReplayFailureCapsuleResult(
            failureCapsulePath,
            new ReplayFailureCapsuleReportLinksResult(
                failureCapsule.Reports.JsonPath,
                failureCapsule.Reports.JunitPath),
            failureCapsule.Scenarios.Select(static scenario => new ReplayFailureCapsuleScenarioResult(
                scenario.Scenario,
                scenario.ScenarioId,
                scenario.Status,
                scenario.File,
                ToFailedStepResult(scenario.FailedStep),
                scenario.Artifacts.Select(static artifact => new ReplayFailureCapsuleArtifactResult(
                    artifact.Kind,
                    artifact.Path,
                    artifact.StepIndex,
                    artifact.StepName)).ToArray(),
                scenario.Error)).ToArray(),
            failureCapsule.Screenshots.Select(static artifact => new ReplayFailureCapsuleArtifactResult(
                artifact.Kind,
                artifact.Path,
                artifact.StepIndex,
                artifact.StepName)).ToArray(),
            failureCapsule.Logcat.Select(static artifact => new ReplayFailureCapsuleArtifactResult(
                artifact.Kind,
                artifact.Path,
                artifact.StepIndex,
                artifact.StepName)).ToArray(),
            failureCapsule.Hierarchies.Select(static artifact => new ReplayFailureCapsuleArtifactResult(
                artifact.Kind,
                artifact.Path,
                artifact.StepIndex,
                artifact.StepName)).ToArray(),
            failureCapsule.ScreenStates.Select(static artifact => new ReplayFailureCapsuleArtifactResult(
                artifact.Kind,
                artifact.Path,
                artifact.StepIndex,
                artifact.StepName)).ToArray(),
            failureCapsule.FailureBundles.Select(static bundle => new ReplayFailureCapsuleBundleResult(
                bundle.Path,
                bundle.Scenario,
                bundle.ScenarioId,
                bundle.File,
                ToFailedStepResult(bundle.FailedStep),
                bundle.Artifacts.Select(static artifact => new ReplayFailureCapsuleArtifactResult(
                    artifact.Kind,
                    artifact.Path,
                    artifact.StepIndex,
                    artifact.StepName)).ToArray(),
                bundle.Error)).ToArray());
    }

    private static ReplayFailureCapsuleFailedStepResult? ToFailedStepResult(FailureCapsuleFailedStep? failedStep) =>
        failedStep is null
            ? null
            : new ReplayFailureCapsuleFailedStepResult(
                failedStep.Index,
                failedStep.Name,
                failedStep.Action,
                failedStep.Phase);
}
