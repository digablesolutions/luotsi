using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal sealed class ReplayCommandHost(ReplayCommandHostDependencies dependencies)
{
    private readonly ReplayCommandHostDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public async Task<int> RunAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var outputMode = ParseOutputMode(options);
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

    private static ReplayOutputMode ParseOutputMode(CliOptions options)
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
            _ => throw new UsageException("replay summarize --format must be json or jsonl.")
        };
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
}

internal sealed record ReplayCommandHostDependencies(
    AppCommandEnvelopeWriter EnvelopeWriter,
    AppCommandJsonWriter JsonWriter,
    Routing.ReplayCommandDispatcher CommandDispatcher);