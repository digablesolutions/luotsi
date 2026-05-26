using System.Runtime.InteropServices;
using System.Text;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal sealed class ReplayCommandHost(ReplayCommandHostDependencies dependencies)
{
    private const string ReplayOpenSummaryJsonFileName = "replay-open-summary.json";
    private const string ReplayOpenSummaryMarkdownFileName = "replay-open.md";
    private readonly ReplayCommandHostDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public async Task<int> RunAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        if (IsOpenCommand(options))
        {
            var openResult = await OpenAsync(options, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, openResult, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
            return 0;
        }

        if (IsScenarioDraftCommand(options))
        {
            var draftResult = await _dependencies.ScenarioDraftService.CreateAsync(options, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, draftResult, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
            return 0;
        }

        if (IsSearchCommand(options))
        {
            var searchResult = await _dependencies.SearchService.SearchAsync(options, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, searchResult, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
            return 0;
        }

        if (IsCapsuleCommand(options))
        {
            var capsuleResult = await _dependencies.CapsuleService.DescribeAsync(options, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, capsuleResult, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
            return 0;
        }

        if (IsScrubCommand(options))
        {
            var scrubResult = await _dependencies.ScrubService.CreateAsync(options, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, scrubResult, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
            return 0;
        }

        if (IsTimelineCommand(options))
        {
            var timelineResult = await _dependencies.TimelineService.ReadAsync(options, artifacts).ConfigureAwait(false);
            switch (ParseOutputMode(options, "replay timeline"))
            {
                case ReplayOutputMode.Json:
                    _dependencies.JsonWriter.Write(timelineResult);
                    break;
                case ReplayOutputMode.Jsonl:
                    _dependencies.JsonWriter.WriteLines(ReplayTimelineService.ToJsonLineObjects(timelineResult));
                    break;
                default:
                    _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, timelineResult, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
                    break;
            }

            return 0;
        }

        if (IsGraphCommand(options))
        {
            var graphResult = await _dependencies.GraphService.CreateAsync(options, artifacts).ConfigureAwait(false);
            switch (ParseOutputMode(options, "replay graph"))
            {
                case ReplayOutputMode.Json:
                    _dependencies.JsonWriter.Write(graphResult);
                    break;
                case ReplayOutputMode.Jsonl:
                    _dependencies.JsonWriter.WriteLines(ReplayGraphService.ToJsonLineObjects(graphResult));
                    break;
                default:
                    _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, graphResult, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
                    break;
            }

            return 0;
        }

        if (IsClusterCommand(options))
        {
            var clusterResult = await _dependencies.ClusterService.ClusterAsync(options, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, clusterResult, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
            return 0;
        }

        var outputMode = ParseOutputMode(options, "replay summarize");
        var result = await _dependencies.CommandDispatcher.ExecuteAsync(options).ConfigureAwait(false);

        switch (outputMode)
        {
            case ReplayOutputMode.Json:
                _dependencies.JsonWriter.Write(result);
                break;
            case ReplayOutputMode.Jsonl:
                _dependencies.JsonWriter.WriteLines(CreateJsonLines(result));
                break;
            default:
                _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, result, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
                break;
        }

        return 0;
    }

    private async Task<ReplayOpenResult> OpenAsync(CliOptions options, ArtifactSession artifacts)
    {
        var snapshot = await artifacts.RefreshIndexWithSnapshotAsync().ConfigureAwait(false);

        var indexHtmlPath = Path.Join(artifacts.Root, ArtifactSession.ArtifactHtmlIndexFileName);
        var indexMarkdownPath = Path.Join(artifacts.Root, ArtifactSession.ArtifactIndexFileName);
        var jsonPath = options.HasFlag("write-json")
            ? Path.Join(artifacts.Root, ReplayOpenSummaryJsonFileName)
            : null;
        var markdownPath = options.HasFlag("write-markdown")
            ? Path.Join(artifacts.Root, ReplayOpenSummaryMarkdownFileName)
            : null;
        var summaries = snapshot.ReplaySummaries;
        var primaryFailure = CreatePrimaryFailure(summaries);
        var commands = BuildOpenCommandHints(artifacts.Root, summaries, primaryFailure).ToArray();
        var nextAction = BuildRecommendedNextAction(artifacts.Root, summaries, primaryFailure, commands);
        var command = BuildOpenCommand(indexHtmlPath);
        var opened = false;
        if (!options.HasFlag("dry-run"))
        {
            var process = await _dependencies.ProcessRunner.RunAsync(command.FileName, command.Args).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(process.Stderr) ? process.Stdout : process.Stderr;
                throw new InvalidOperationException($"Failed to open replay artifact index. {message}".Trim());
            }

            opened = true;
        }

        var result = new ReplayOpenResult(
            ResultSchemas.ReplayOpen,
            artifacts.Root,
            indexHtmlPath,
            indexMarkdownPath,
            jsonPath,
            markdownPath,
            summaries.Count,
            summaries.Count(static summary => summary.HasFailureSignals),
            primaryFailure,
            nextAction,
            commands,
            opened,
            command.FileName,
            command.Args);

        if (jsonPath is not null)
        {
            await artifacts.WriteJsonAsync(ReplayOpenSummaryJsonFileName, result).ConfigureAwait(false);
        }

        if (markdownPath is not null)
        {
            await artifacts.WriteTextAsync(ReplayOpenSummaryMarkdownFileName, BuildOpenMarkdown(result)).ConfigureAwait(false);
        }

        return result;
    }

    private static string BuildOpenMarkdown(ReplayOpenResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Replay Front Door");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{EscapeMarkdown(result.ArtifactRoot)}`");
        builder.AppendLine($"Sessions: `{result.SessionCount}`");
        builder.AppendLine($"Failures: `{result.FailureCount}`");
        builder.AppendLine($"Opened: `{result.Opened}`");
        AppendField(builder, "Index HTML", result.IndexHtmlPath);
        AppendField(builder, "Index Markdown", result.IndexMarkdownPath);
        AppendField(builder, "JSON summary", result.JsonPath);
        AppendField(builder, "Markdown summary", result.MarkdownPath);
        builder.AppendLine();
        builder.AppendLine("## Recommended Next Action");
        builder.AppendLine();
        builder.AppendLine($"- **{EscapeMarkdown(result.RecommendedNextAction.Title)}** (`{EscapeMarkdown(result.RecommendedNextAction.Kind)}`)");
        builder.AppendLine($"  {EscapeMarkdown(result.RecommendedNextAction.Reason)}");
        builder.AppendLine($"  `{EscapeMarkdown(result.RecommendedNextAction.Command)}`");
        builder.AppendLine();
        builder.AppendLine("## Primary Failure");
        builder.AppendLine();
        if (result.PrimaryFailure is null)
        {
            builder.AppendLine("No failure signal was found.");
        }
        else
        {
            AppendField(builder, "Scenario", result.PrimaryFailure.Scenario);
            AppendField(builder, "Step", result.PrimaryFailure.Step);
            AppendField(builder, "Action", result.PrimaryFailure.Action);
            AppendField(builder, "Message", result.PrimaryFailure.Message);
            AppendField(builder, "Timeline", result.PrimaryFailure.TimelinePath);
            AppendField(builder, "Failure capsule", result.PrimaryFailure.FailureCapsulePath);
        }

        builder.AppendLine();
        builder.AppendLine("## Commands");
        builder.AppendLine();
        foreach (var hint in result.Commands)
        {
            builder.AppendLine($"- `{EscapeMarkdown(hint.Command)}`");
            builder.AppendLine($"  {EscapeMarkdown(hint.Description)}");
        }

        return builder.ToString();
    }

    private static void AppendField(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"- {label}: `{EscapeMarkdown(value)}`");
        }
    }

    private static string EscapeMarkdown(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static ReplayOpenPrimaryFailureResult? CreatePrimaryFailure(IReadOnlyList<SessionReplaySummary> summaries)
    {
        var summary = summaries.FirstOrDefault(static item => item.HasFailureSignals);
        if (summary is null)
        {
            return null;
        }

        var failedScenario = summary.FailureCapsule?.Scenarios.FirstOrDefault();
        var failedStep = failedScenario?.FailedStep;
        var failureHighlight = summary.TimelineHighlights.FirstOrDefault(static entry => entry.IsFailureRelevant);
        return new ReplayOpenPrimaryFailureResult(
            failedScenario?.Scenario,
            failedStep?.Name,
            failedStep?.Action,
            failedScenario?.Error?.Message ?? failureHighlight?.Detail,
            summary.TimelinePath,
            summary.FailureCapsulePath);
    }

    private static IEnumerable<ReplayOpenCommandHintResult> BuildOpenCommandHints(
        string artifactRoot,
        IReadOnlyList<SessionReplaySummary> summaries,
        ReplayOpenPrimaryFailureResult? primaryFailure)
    {
        yield return new ReplayOpenCommandHintResult(
            "capsule",
            "Write the replay capsule summary and README for this bundle.",
            $"luotsi replay capsule --artifacts {Quote(artifactRoot)} --write-readme --write-json");
        if (summaries.Any(static summary => summary.HasTimeline))
        {
            yield return new ReplayOpenCommandHintResult(
                "timeline",
                "Read the ordered session timeline.",
                $"luotsi replay timeline --artifacts {Quote(artifactRoot)} --context 3 --write-markdown");
        }

        if (summaries.Any(static summary => summary.HasFailureSignals))
        {
            yield return new ReplayOpenCommandHintResult(
                "scrub",
                "Scrub the focused failure window with previous/current/next events.",
                $"luotsi replay scrub --artifacts {Quote(artifactRoot)} --failures --context 3 --write-markdown");
            yield return new ReplayOpenCommandHintResult(
                "graph",
                "Build semantic failure context for agents and reviewers.",
                $"luotsi replay graph --artifacts {Quote(artifactRoot)} --failed --write-json --write-markdown");

            if (!string.IsNullOrWhiteSpace(primaryFailure?.Message))
            {
                yield return new ReplayOpenCommandHintResult(
                    "search",
                    "Search the bundle for the primary failure text.",
                    $"luotsi replay search --artifacts {Quote(artifactRoot)} --contains {Quote(primaryFailure.Message)}");
            }

            yield return new ReplayOpenCommandHintResult(
                "cluster",
                "Look for matching failure shapes across sibling replay bundles.",
                $"luotsi replay cluster --artifacts {Quote(ResolveClusterRoot(artifactRoot))} --min-count 2 --write-markdown");
        }

        if (summaries.Count > 0)
        {
            yield return new ReplayOpenCommandHintResult(
                "scenario_draft",
                "Draft a scenario from captured inspect, view, action, and telemetry events.",
                $"luotsi replay scenario-draft --artifacts {Quote(artifactRoot)} --output draft-scenario.json --write-json --write-markdown");
        }
    }

    private static ReplayOpenNextActionResult BuildRecommendedNextAction(
        string artifactRoot,
        IReadOnlyList<SessionReplaySummary> summaries,
        ReplayOpenPrimaryFailureResult? primaryFailure,
        IReadOnlyList<ReplayOpenCommandHintResult> commands)
    {
        if (primaryFailure is not null)
        {
            var scrub = commands.First(static command => string.Equals(command.Kind, "scrub", StringComparison.Ordinal));
            return new ReplayOpenNextActionResult(
                "scrub_failure",
                "Scrub the failure window",
                "A failure signal was found; start with the smallest timeline window before opening broader artifacts.",
                scrub.Command);
        }

        if (summaries.Count > 0)
        {
            var capsule = commands.First(static command => string.Equals(command.Kind, "capsule", StringComparison.Ordinal));
            return new ReplayOpenNextActionResult(
                "write_capsule",
                "Write the replay capsule",
                "No failure signal was found; create the capsule summary before deeper inspection.",
                capsule.Command);
        }

        return new ReplayOpenNextActionResult(
            "inspect_artifacts",
            "Inspect the artifact index",
            "No replay metadata was found; use the refreshed index to inspect available artifacts.",
            $"luotsi replay open --artifacts {Quote(artifactRoot)}");
    }

    private static ReplayOutputMode ParseOutputMode(CliOptions options, string commandName)
    {
        var format = options.Get("format");
        if (string.IsNullOrWhiteSpace(format))
        {
            return ReplayOutputMode.Envelope;
        }

        if (options.HasFlag("human") || options.HasFlag("quiet") || options.HasFlag("json") || !string.IsNullOrWhiteSpace(options.Get("console-output")))
        {
            throw new UsageException($"{commandName} --format is a raw output mode; do not combine it with --human, --quiet, --json, or --console-output.");
        }

        return format.Trim().ToLowerInvariant() switch
        {
            "json" => ReplayOutputMode.Json,
            "jsonl" => ReplayOutputMode.Jsonl,
            _ => throw new UsageException($"{commandName} --format must be json or jsonl.")
        };
    }

    private static bool IsOpenCommand(CliOptions options) =>
        options.Arguments.Count > 0 &&
        string.Equals(options.Arguments[0], "open", StringComparison.OrdinalIgnoreCase);

    private static bool IsScenarioDraftCommand(CliOptions options) =>
        options.Arguments.Count > 0 &&
        (string.Equals(options.Arguments[0], "scenario-draft", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.Arguments[0], "draft-scenario", StringComparison.OrdinalIgnoreCase));

    private static bool IsSearchCommand(CliOptions options) =>
        options.Arguments.Count > 0 &&
        string.Equals(options.Arguments[0], "search", StringComparison.OrdinalIgnoreCase);

    private static bool IsCapsuleCommand(CliOptions options) =>
        options.Arguments.Count > 0 &&
        string.Equals(options.Arguments[0], "capsule", StringComparison.OrdinalIgnoreCase);

    private static bool IsTimelineCommand(CliOptions options) =>
        options.Arguments.Count > 0 &&
        string.Equals(options.Arguments[0], "timeline", StringComparison.OrdinalIgnoreCase);

    private static bool IsScrubCommand(CliOptions options) =>
        options.Arguments.Count > 0 &&
        string.Equals(options.Arguments[0], "scrub", StringComparison.OrdinalIgnoreCase);

    private static bool IsGraphCommand(CliOptions options) =>
        options.Arguments.Count > 0 &&
        string.Equals(options.Arguments[0], "graph", StringComparison.OrdinalIgnoreCase);

    private static bool IsClusterCommand(CliOptions options) =>
        options.Arguments.Count > 0 &&
        string.Equals(options.Arguments[0], "cluster", StringComparison.OrdinalIgnoreCase);

    private static ReplayOpenCommand BuildOpenCommand(string indexHtmlPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ReplayOpenCommand("cmd", ["/c", "start", "", indexHtmlPath]);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new ReplayOpenCommand("open", [indexHtmlPath]);
        }

        return new ReplayOpenCommand("xdg-open", [indexHtmlPath]);
    }

    private static string ResolveClusterRoot(string artifactRoot)
    {
        var parent = Path.GetDirectoryName(artifactRoot);
        return string.IsNullOrWhiteSpace(parent) ? artifactRoot : parent;
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;

    private static IEnumerable<object> CreateJsonLines(ReplaySummarizeResult result)
    {
        yield return new ReplaySummaryJsonLine(
            ResultSchemas.SessionReplaySummary,
            "summary",
            result.ArtifactRoot,
            result.SessionCount,
            result.FailureCount,
            result.Commands,
            null);

        foreach (var session in result.Sessions)
        {
            yield return new ReplaySummaryJsonLine(
                ResultSchemas.SessionReplaySummary,
                "session",
                result.ArtifactRoot,
                null,
                null,
                null,
                session);
        }
    }

    private enum ReplayOutputMode
    {
        Envelope,
        Json,
        Jsonl
    }

    private sealed record ReplaySummaryJsonLine(
        string Schema,
        string Type,
        string ArtifactRoot,
        int? SessionCount,
        int? FailureCount,
        IReadOnlyList<ReplaySummaryCommandHintResult>? Commands,
        ReplaySessionSummaryResult? Session);

    private sealed record ReplayOpenCommand(string FileName, IReadOnlyList<string> Args);
}

internal sealed record ReplayCommandHostDependencies(
    AppCommandEnvelopeWriter EnvelopeWriter,
    AppCommandJsonWriter JsonWriter,
    Routing.ReplayCommandDispatcher CommandDispatcher,
    IProcessRunner ProcessRunner,
    ReplayScenarioDraftService ScenarioDraftService,
    ReplaySearchService SearchService,
    ReplayCapsuleService CapsuleService,
    ReplayTimelineService TimelineService,
    ReplayScrubService ScrubService,
    ReplayGraphService GraphService,
    ReplayClusterService ClusterService);
