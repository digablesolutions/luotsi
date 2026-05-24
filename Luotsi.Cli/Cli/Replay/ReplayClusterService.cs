using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal sealed class ReplayClusterService(IFileSystem fileSystem)
{
    private const string ClusterJsonFileName = "replay-clusters.json";
    private const string ClusterMarkdownFileName = "replay-clusters.md";
    private static readonly Regex VolatileNumber = new(@"\b\d+\b", RegexOptions.Compiled);
    private static readonly Regex VolatileHex = new(@"\b0x[0-9a-fA-F]+\b", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<ReplayClustersResult> ClusterAsync(CliOptions options, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);
        var query = CreateQuery(options);

        var allFiles = _fileSystem.GetFiles(artifacts.Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(artifacts.Root, path))
            .ToArray();
        var summaries = new SessionReplaySummaryReader(artifacts.Root, _fileSystem).ReadSummaries(allFiles);
        if (summaries.Count == 0)
        {
            throw new UsageException($"No session replay metadata was found under artifact root '{artifacts.Root}'.");
        }

        var failures = summaries
            .SelectMany(CreateFailureInstances)
            .ToArray();
        var clusters = failures
            .GroupBy(static failure => failure.Signature, StringComparer.Ordinal)
            .Select(group => CreateCluster(artifacts.Root, group.Key, group.ToArray()))
            .Where(cluster => MatchesCluster(cluster, query))
            .OrderByDescending(static cluster => cluster.Count)
            .ThenBy(static cluster => cluster.Signature, StringComparer.Ordinal)
            .ToArray();

        var jsonPath = options.HasFlag("write-json")
            ? Path.Join(artifacts.Root, ClusterJsonFileName)
            : null;
        var markdownPath = options.HasFlag("write-markdown")
            ? Path.Join(artifacts.Root, ClusterMarkdownFileName)
            : null;
        var result = new ReplayClustersResult(
            ResultSchemas.ReplayClusters,
            artifacts.Root,
            summaries.Count,
            failures.Length,
            clusters.Length,
            query,
            jsonPath,
            markdownPath,
            clusters);

        if (jsonPath is not null)
        {
            await artifacts.WriteJsonAsync(ClusterJsonFileName, result).ConfigureAwait(false);
        }

        if (markdownPath is not null)
        {
            await artifacts.WriteTextAsync(ClusterMarkdownFileName, BuildMarkdown(result)).ConfigureAwait(false);
        }

        return result;
    }

    private static ReplayClusterQueryResult CreateQuery(CliOptions options)
    {
        var minCount = options.Int("min-count", 1);
        if (minCount <= 0)
        {
            throw new UsageException("replay cluster requires --min-count greater than zero.");
        }

        var similarity = NormalizeBlank(options.Get("similarity"));
        if (similarity is not null &&
            !string.Equals(similarity, "same_failure_shape", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(similarity, "likely_same_cause", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(similarity, "same_bucket", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("replay cluster --similarity must be same_failure_shape, likely_same_cause, or same_bucket.");
        }

        return new ReplayClusterQueryResult(minCount, similarity, NormalizeBlank(options.Get("contains")));
    }

    private static bool MatchesCluster(ReplayFailureClusterResult cluster, ReplayClusterQueryResult query)
    {
        if (cluster.Count < query.MinCount)
        {
            return false;
        }

        if (query.Similarity is not null &&
            !string.Equals(cluster.Intelligence.Similarity, query.Similarity, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.Contains is not null && !ClusterContains(cluster, query.Contains))
        {
            return false;
        }

        return true;
    }

    private static bool ClusterContains(ReplayFailureClusterResult cluster, string value) =>
        Contains(cluster.Id, value) ||
        Contains(cluster.Signature, value) ||
        Contains(cluster.Category, value) ||
        Contains(cluster.Message, value) ||
        Contains(cluster.Action, value) ||
        Contains(cluster.Step, value) ||
        Contains(cluster.Intelligence.LikelyCause, value) ||
        cluster.Intelligence.SupportingSignals.Any(signal => Contains(signal, value)) ||
        cluster.Intelligence.SignalComparisons.Any(signal =>
            Contains(signal.Name, value) ||
            Contains(signal.Stability, value) ||
            signal.Values.Any(signalValue => Contains(signalValue, value)));

    private static IEnumerable<FailureInstance> CreateFailureInstances(SessionReplaySummary summary)
    {
        var failureCapsule = summary.FailureCapsule;
        if (failureCapsule is not null && failureCapsule.Scenarios.Count > 0)
        {
            foreach (var scenario in failureCapsule.Scenarios.Where(static scenario => scenario.Error is not null || string.Equals(scenario.Status, "failed", StringComparison.OrdinalIgnoreCase)))
            {
                var category = scenario.Error?.Category;
                var message = scenario.Error?.Message;
                var action = scenario.FailedStep?.Action;
                var step = scenario.FailedStep?.Name;
                yield return new FailureInstance(
                    BuildSignature(category, message, action, step),
                    ToResult(summary, scenario, category, message, action, step));
            }

            yield break;
        }

        foreach (var highlight in summary.TimelineHighlights.Where(static entry => entry.IsFailureRelevant))
        {
            yield return new FailureInstance(
                BuildSignature(null, highlight.Detail, null, null),
                new ReplayFailureClusterInstanceResult(
                    summary.SessionId,
                    summary.SessionKind,
                    summary.StartedAt,
                    summary.Target,
                    summary.MetadataPath,
                    summary.FailureCapsulePath,
                    highlight.ScenarioId,
                    highlight.Scenario,
                    null,
                    highlight.StepIndex,
                    null,
                    null,
                    null,
                    highlight.Detail));
        }
    }

    private static ReplayFailureClusterResult CreateCluster(string artifactRoot, string signature, IReadOnlyList<FailureInstance> failures)
    {
        var instances = failures
            .Select(static failure => failure.Instance)
            .OrderByDescending(static instance => instance.StartedAt)
            .ThenBy(static instance => instance.MetadataPath, StringComparer.Ordinal)
            .ToArray();
        var representative = instances[0];
        return new ReplayFailureClusterResult(
            "cluster:" + ShortHash(signature),
            signature,
            instances.Length,
            representative.ErrorCategory,
            representative.ErrorMessage,
            representative.Action,
            representative.Step,
            CreateIntelligence(artifactRoot, instances),
            CreateHints(artifactRoot, instances),
            instances);
    }

    private static ReplayFailureClusterIntelligenceResult CreateIntelligence(
        string artifactRoot,
        IReadOnlyList<ReplayFailureClusterInstanceResult> instances)
    {
        var representative = SelectBestReplayInstance(instances);
        var representativeRoot = ResolveInstanceArtifactRoot(artifactRoot, representative.MetadataPath);
        var score = CalculateSimilarityScore(instances);
        return new ReplayFailureClusterIntelligenceResult(
            ClassifySimilarity(score),
            score,
            BuildLikelyCause(instances),
            representativeRoot,
            $"luotsi replay graph --artifacts {Quote(representativeRoot)} --failed --write-json --write-markdown",
            $"luotsi replay scrub --artifacts {Quote(representativeRoot)} --failures --context 3 --write-markdown",
            BuildSupportingSignals(instances, representative),
            BuildSignalComparisons(instances));
    }

    private static IReadOnlyList<ReplayFailureClusterHintResult> CreateHints(string artifactRoot, IReadOnlyList<ReplayFailureClusterInstanceResult> instances)
    {
        var hints = new List<ReplayFailureClusterHintResult>();
        var representative = SelectBestReplayInstance(instances);
        var representativeRoot = ResolveInstanceArtifactRoot(artifactRoot, representative.MetadataPath);
        if (instances.Count > 1)
        {
            hints.Add(new ReplayFailureClusterHintResult(
                "same_failure_shape",
                $"This failure shape appears in {instances.Count} replay sessions.",
                null));
        }

        if (instances.Count > 1 && string.Equals(representative.ErrorCategory, "selector_or_screen_state", StringComparison.OrdinalIgnoreCase))
        {
            hints.Add(new ReplayFailureClusterHintResult(
                "likely_repeated_selector_or_screen_state_failure",
                "Repeated selector or screen-state failures usually deserve a stronger wait, alternate selector, or visual fallback.",
                $"luotsi replay graph --artifacts {Quote(representativeRoot)} --failed --write-json --write-markdown"));
        }

        hints.Add(new ReplayFailureClusterHintResult(
            "inspect_best_failure_graph",
            "Open the semantic graph for the best representative failure in this cluster.",
            $"luotsi replay graph --artifacts {Quote(representativeRoot)} --failed --write-json --write-markdown"));

        hints.Add(new ReplayFailureClusterHintResult(
            "scrub_best_failure",
            "Scrub the best representative failure timeline with local context.",
            $"luotsi replay scrub --artifacts {Quote(representativeRoot)} --failures --context 3 --write-markdown"));

        hints.Add(new ReplayFailureClusterHintResult(
            "describe_best_replay_capsule",
            "Open the replay front door for the best representative failure.",
            $"luotsi replay capsule --artifacts {Quote(representativeRoot)} --write-readme --write-json"));

        hints.Add(new ReplayFailureClusterHintResult(
            "open_best_replay",
            "Open the best matching replay bundle locally.",
            $"luotsi replay open --artifacts {Quote(representativeRoot)}"));

        if (!string.IsNullOrWhiteSpace(representative.ErrorMessage))
        {
            hints.Add(new ReplayFailureClusterHintResult(
                "search_best_failure_text",
                "Search the best replay bundle for the representative failure text.",
                $"luotsi replay search --artifacts {Quote(representativeRoot)} --contains {Quote(representative.ErrorMessage)}"));
        }

        return hints;
    }

    private static ReplayFailureClusterInstanceResult SelectBestReplayInstance(IReadOnlyList<ReplayFailureClusterInstanceResult> instances) =>
        instances
            .OrderByDescending(ReplayEvidenceScore)
            .ThenByDescending(static instance => instance.StartedAt)
            .ThenBy(static instance => instance.MetadataPath, StringComparer.Ordinal)
            .First();

    private static int ReplayEvidenceScore(ReplayFailureClusterInstanceResult instance)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(instance.FailureCapsulePath))
        {
            score += 5;
        }

        if (!string.IsNullOrWhiteSpace(instance.ErrorMessage))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(instance.Action))
        {
            score++;
        }

        if (!string.IsNullOrWhiteSpace(instance.Step))
        {
            score++;
        }

        if (!string.IsNullOrWhiteSpace(instance.Scenario))
        {
            score++;
        }

        return score;
    }

    private static double CalculateSimilarityScore(IReadOnlyList<ReplayFailureClusterInstanceResult> instances)
    {
        if (instances.Count <= 1)
        {
            return 1.0;
        }

        var categoryScore = DistinctNormalized(instances.Select(static instance => instance.ErrorCategory)) == 1 ? 0.25 : 0.0;
        var actionScore = DistinctNormalized(instances.Select(static instance => instance.Action)) == 1 ? 0.25 : 0.0;
        var stepScore = DistinctNormalized(instances.Select(static instance => instance.Step)) == 1 ? 0.2 : 0.0;
        var messageScore = DistinctNormalized(instances.Select(static instance => instance.ErrorMessage)) == 1 ? 0.3 : 0.15;
        return Math.Round(categoryScore + actionScore + stepScore + messageScore, 2);
    }

    private static string ClassifySimilarity(double score)
    {
        if (score >= 0.9)
        {
            return "same_failure_shape";
        }

        if (score >= 0.7)
        {
            return "likely_same_cause";
        }

        return "same_bucket";
    }

    private static string BuildLikelyCause(IReadOnlyList<ReplayFailureClusterInstanceResult> instances)
    {
        var latest = instances[0];
        if (string.Equals(latest.ErrorCategory, "selector_or_screen_state", StringComparison.OrdinalIgnoreCase))
        {
            return "Repeated selector or screen-state failure; inspect the semantic graph for selector drift, missing wait condition, or visual fallback need.";
        }

        if (!string.IsNullOrWhiteSpace(latest.Action))
        {
            return $"Repeated failure around action '{latest.Action}'; inspect causal graph and timeline around that action.";
        }

        return "Repeated failure shape; inspect the best representative replay graph and search the representative error text.";
    }

    private static IReadOnlyList<string> BuildSupportingSignals(
        IReadOnlyList<ReplayFailureClusterInstanceResult> instances,
        ReplayFailureClusterInstanceResult representative)
    {
        var signals = new List<string>
        {
            "instances=" + instances.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "best_replay_evidence_score=" + ReplayEvidenceScore(representative).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        AddSignal(signals, "category", representative.ErrorCategory);
        AddSignal(signals, "action", representative.Action);
        AddSignal(signals, "step", representative.Step);
        AddSignal(signals, "message", representative.ErrorMessage);
        return signals;
    }

    private static IReadOnlyList<ReplayFailureClusterSignalComparisonResult> BuildSignalComparisons(
        IReadOnlyList<ReplayFailureClusterInstanceResult> instances)
    {
        return new[]
        {
            CompareSignal("category", instances.Select(static instance => instance.ErrorCategory)),
            CompareSignal("action", instances.Select(static instance => instance.Action)),
            CompareSignal("step", instances.Select(static instance => instance.Step)),
            CompareSignal("message", instances.Select(static instance => instance.ErrorMessage)),
            CompareSignal("target", instances.Select(static instance => instance.Target))
        };
    }

    private static ReplayFailureClusterSignalComparisonResult CompareSignal(string name, IEnumerable<string?> values)
    {
        var normalized = values
            .Select(static value => string.IsNullOrWhiteSpace(value) ? "" : value.Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        var stability = normalized.Length switch
        {
            0 => "missing",
            1 => "stable",
            _ => "variable"
        };
        return new ReplayFailureClusterSignalComparisonResult(name, stability, normalized);
    }

    private static int DistinctNormalized(IEnumerable<string?> values) =>
        values.Select(Normalize).Where(static value => value.Length > 0).Distinct(StringComparer.Ordinal).Count();

    private static void AddSignal(List<string> signals, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            signals.Add(name + "=" + value);
        }
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ReplayFailureClusterInstanceResult ToResult(
        SessionReplaySummary summary,
        FailureCapsuleScenario scenario,
        string? category,
        string? message,
        string? action,
        string? step) =>
        new(
            summary.SessionId,
            summary.SessionKind,
            summary.StartedAt,
            summary.Target,
            summary.MetadataPath,
            summary.FailureCapsulePath,
            scenario.ScenarioId,
            scenario.Scenario,
            scenario.File,
            scenario.FailedStep?.Index,
            step,
            action,
            category,
            message);

    private static string BuildSignature(string? category, string? message, string? action, string? step)
    {
        var parts = new[]
        {
            Normalize(category),
            Normalize(action),
            Normalize(step),
            Normalize(message)
        };
        return string.Join("|", parts);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = value.Trim().ToLowerInvariant();
        normalized = VolatileHex.Replace(normalized, "<hex>");
        normalized = VolatileNumber.Replace(normalized, "<n>");
        normalized = Whitespace.Replace(normalized, " ");
        return normalized;
    }

    private static string ShortHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
    }

    private static string ResolveInstanceArtifactRoot(string artifactRoot, string metadataPath)
    {
        var directory = Path.GetDirectoryName(metadataPath);
        return string.IsNullOrWhiteSpace(directory)
            ? artifactRoot
            : Path.Join(artifactRoot, directory);
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;

    private static string BuildMarkdown(ReplayClustersResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Replay Failure Clusters");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{result.ArtifactRoot}`");
        builder.AppendLine($"Sessions: `{result.SessionCount}`");
        builder.AppendLine($"Failures: `{result.FailureCount}`");
        builder.AppendLine($"Clusters: `{result.ClusterCount}`");
        builder.AppendLine($"Query: `min-count={result.Query.MinCount}, similarity={result.Query.Similarity ?? "*"}, contains={result.Query.Contains ?? "*"}`");
        builder.AppendLine();
        AppendStartHere(builder, result.Clusters.FirstOrDefault());
        builder.AppendLine();
        builder.AppendLine("| Cluster | Count | Category | Action | Step | Message |");
        builder.AppendLine("|---|---:|---|---|---|---|");
        foreach (var cluster in result.Clusters)
        {
            builder.AppendLine($"| {EscapeMarkdown(cluster.Id)} | {cluster.Count} | {EscapeMarkdown(cluster.Category)} | {EscapeMarkdown(cluster.Action)} | {EscapeMarkdown(cluster.Step)} | {EscapeMarkdown(cluster.Message)} |");
        }

        foreach (var cluster in result.Clusters)
        {
            builder.AppendLine();
            builder.AppendLine($"## {cluster.Id}");
            builder.AppendLine();
            builder.AppendLine($"Signature: `{EscapeMarkdown(cluster.Signature)}`");
            builder.AppendLine();
            builder.AppendLine("### Intelligence");
            builder.AppendLine();
            builder.AppendLine($"- Similarity: `{EscapeMarkdown(cluster.Intelligence.Similarity)}` (`{cluster.Intelligence.SimilarityScore:0.##}`)");
            builder.AppendLine($"- Likely cause: {EscapeMarkdown(cluster.Intelligence.LikelyCause)}");
            builder.AppendLine($"- Best replay: `{EscapeMarkdown(cluster.Intelligence.BestReplayArtifactRoot)}`");
            if (!string.IsNullOrWhiteSpace(cluster.Intelligence.BestGraphCommand))
            {
                builder.AppendLine($"- Graph: `{EscapeMarkdown(cluster.Intelligence.BestGraphCommand)}`");
            }

            if (!string.IsNullOrWhiteSpace(cluster.Intelligence.BestScrubCommand))
            {
                builder.AppendLine($"- Scrub: `{EscapeMarkdown(cluster.Intelligence.BestScrubCommand)}`");
            }

            if (cluster.Intelligence.SupportingSignals.Count > 0)
            {
                builder.AppendLine("- Signals: " + EscapeMarkdown(string.Join(", ", cluster.Intelligence.SupportingSignals)));
            }

            if (cluster.Intelligence.SignalComparisons.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("| Signal | Stability | Values |");
                builder.AppendLine("|---|---|---|");
                foreach (var signal in cluster.Intelligence.SignalComparisons)
                {
                    builder.AppendLine($"| {EscapeMarkdown(signal.Name)} | {EscapeMarkdown(signal.Stability)} | {EscapeMarkdown(string.Join(", ", signal.Values))} |");
                }
            }

            builder.AppendLine();
            builder.AppendLine("### Hints");
            builder.AppendLine();
            foreach (var hint in cluster.Hints)
            {
                builder.Append("- ");
                builder.Append(EscapeMarkdown(hint.Message));
                if (!string.IsNullOrWhiteSpace(hint.Command))
                {
                    builder.Append(" `");
                    builder.Append(EscapeMarkdown(hint.Command));
                    builder.Append('`');
                }

                builder.AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine("| Started | Session | Target | Scenario | Metadata | Failure Capsule |");
            builder.AppendLine("|---|---|---|---|---|---|");
            foreach (var instance in cluster.Instances)
            {
                builder.AppendLine($"| {EscapeMarkdown(instance.StartedAt.ToString("O"))} | {EscapeMarkdown(instance.SessionId)} | {EscapeMarkdown(instance.Target)} | {EscapeMarkdown(instance.Scenario)} | {MarkdownLink(instance.MetadataPath)} | {MarkdownLink(instance.FailureCapsulePath)} |");
            }
        }

        return builder.ToString();
    }

    private static void AppendStartHere(StringBuilder builder, ReplayFailureClusterResult? cluster)
    {
        builder.AppendLine("## Start Here");
        builder.AppendLine();
        if (cluster is null)
        {
            builder.AppendLine("No repeated failure clusters matched the query.");
            return;
        }

        builder.AppendLine($"- Top cluster: `{EscapeMarkdown(cluster.Id)}` ({cluster.Count} failures)");
        builder.AppendLine($"- Similarity: `{EscapeMarkdown(cluster.Intelligence.Similarity)}` (`{cluster.Intelligence.SimilarityScore:0.##}`)");
        builder.AppendLine($"- Likely cause: {EscapeMarkdown(cluster.Intelligence.LikelyCause)}");
        builder.AppendLine($"- Best replay bundle: `{EscapeMarkdown(cluster.Intelligence.BestReplayArtifactRoot)}`");
        var capsuleHint = cluster.Hints.FirstOrDefault(static hint => string.Equals(hint.Kind, "describe_best_replay_capsule", StringComparison.Ordinal));
        if (capsuleHint is not null)
        {
            builder.AppendLine($"- Open capsule: `{EscapeMarkdown(capsuleHint.Command)}`");
        }

        if (!string.IsNullOrWhiteSpace(cluster.Intelligence.BestScrubCommand))
        {
            builder.AppendLine($"- Scrub failure: `{EscapeMarkdown(cluster.Intelligence.BestScrubCommand)}`");
        }

        if (!string.IsNullOrWhiteSpace(cluster.Intelligence.BestGraphCommand))
        {
            builder.AppendLine($"- Inspect graph: `{EscapeMarkdown(cluster.Intelligence.BestGraphCommand)}`");
        }
    }

    private static string MarkdownLink(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "" : $"[{EscapeMarkdown(path)}]({path.Replace(" ", "%20", StringComparison.Ordinal)})";

    private static string EscapeMarkdown(string? value) =>
        (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private sealed record FailureInstance(string Signature, ReplayFailureClusterInstanceResult Instance);
}
