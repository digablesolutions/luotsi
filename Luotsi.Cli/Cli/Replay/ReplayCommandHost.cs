using System.Runtime.InteropServices;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal sealed class ReplayCommandHost(ReplayCommandHostDependencies dependencies)
{
    private readonly ReplayCommandHostDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public async Task<int> RunAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        if (IsOpenCommand(options))
        {
            var openResult = await OpenAsync(options, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, openResult, artifacts.ToData());
            return 0;
        }

        if (IsScenarioDraftCommand(options))
        {
            var draftResult = await _dependencies.ScenarioDraftService.CreateAsync(options, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, draftResult, artifacts.ToData());
            return 0;
        }

        if (IsSearchCommand(options))
        {
            var searchResult = await _dependencies.SearchService.SearchAsync(options, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, searchResult, artifacts.ToData());
            return 0;
        }

        if (IsCapsuleCommand(options))
        {
            var capsuleResult = await _dependencies.CapsuleService.DescribeAsync(options, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, capsuleResult, artifacts.ToData());
            return 0;
        }

        if (IsScrubCommand(options))
        {
            var scrubResult = await _dependencies.ScrubService.CreateAsync(options, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, scrubResult, artifacts.ToData());
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
                    _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, timelineResult, artifacts.ToData());
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
                    _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, graphResult, artifacts.ToData());
                    break;
            }

            return 0;
        }

        if (IsClusterCommand(options))
        {
            var clusterResult = await _dependencies.ClusterService.ClusterAsync(options, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, clusterResult, artifacts.ToData());
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
                _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, result, artifacts.ToData());
                break;
        }

        return 0;
    }

    private async Task<ReplayOpenResult> OpenAsync(CliOptions options, ArtifactSession artifacts)
    {
        await artifacts.RefreshIndexAsync().ConfigureAwait(false);

        var indexHtmlPath = Path.Join(artifacts.Root, ArtifactSession.ArtifactHtmlIndexFileName);
        var indexMarkdownPath = Path.Join(artifacts.Root, ArtifactSession.ArtifactIndexFileName);
        var command = BuildOpenCommand(indexHtmlPath);
        if (options.HasFlag("dry-run"))
        {
            return new ReplayOpenResult(
                ResultSchemas.ReplayOpen,
                artifacts.Root,
                indexHtmlPath,
                indexMarkdownPath,
                false,
                command.FileName,
                command.Args);
        }

        var process = await _dependencies.ProcessRunner.RunAsync(command.FileName, command.Args).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(process.Stderr) ? process.Stdout : process.Stderr;
            throw new InvalidOperationException($"Failed to open replay artifact index. {message}".Trim());
        }

        return new ReplayOpenResult(
            ResultSchemas.ReplayOpen,
            artifacts.Root,
            indexHtmlPath,
            indexMarkdownPath,
            true,
            command.FileName,
            command.Args);
    }

    private static ReplayOutputMode ParseOutputMode(CliOptions options, string commandName)
    {
        var format = options.Get("format");
        if (string.IsNullOrWhiteSpace(format))
        {
            return ReplayOutputMode.Envelope;
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

    private static IEnumerable<object> CreateJsonLines(ReplaySummarizeResult result)
    {
        yield return new ReplaySummaryJsonLine(
            ResultSchemas.SessionReplaySummary,
            "summary",
            result.ArtifactRoot,
            result.SessionCount,
            result.FailureCount,
            null);

        foreach (var session in result.Sessions)
        {
            yield return new ReplaySummaryJsonLine(
                ResultSchemas.SessionReplaySummary,
                "session",
                result.ArtifactRoot,
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
