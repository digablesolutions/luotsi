using System.Text;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal sealed class ReplayCapsuleService(IFileSystem fileSystem)
{
    private const string CapsuleReadmeFileName = "replay-capsule.md";
    private const string CapsuleSummaryFileName = "replay-capsule-summary.json";
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<ReplayCapsuleResult> DescribeAsync(CliOptions options, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var files = _fileSystem.GetFiles(artifacts.Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(artifacts.Root, path).Replace('\\', '/'))
            .ToArray();
        var summaries = new SessionReplaySummaryReader(artifacts.Root, _fileSystem).ReadSummaries(files);
        if (summaries.Count == 0)
        {
            throw new UsageException($"No session replay metadata was found under artifact root '{artifacts.Root}'.");
        }

        var failureSessions = summaries
            .Where(static summary => summary.HasFailureSignals)
            .ToArray();
        var primaryFailureSession = failureSessions.FirstOrDefault();
        var primaryFailure = primaryFailureSession is null
            ? null
            : CreatePrimaryFailure(primaryFailureSession);
        var artifactCounts = CountArtifacts(files);
        var commandHints = BuildCommandHints(artifacts.Root, primaryFailure).ToArray();
        var readmePath = options.HasFlag("write-readme")
            ? Path.Join(artifacts.Root, CapsuleReadmeFileName)
            : null;
        var jsonPath = options.HasFlag("write-json")
            ? Path.Join(artifacts.Root, CapsuleSummaryFileName)
            : null;
        var result = new ReplayCapsuleResult(
            ResultSchemas.ReplayCapsule,
            artifacts.Root,
            summaries.Count,
            failureSessions.Length,
            summaries.Any(static summary => summary.FailureCapsule is not null),
            readmePath,
            jsonPath,
            primaryFailure,
            artifactCounts,
            commandHints);

        if (jsonPath is not null)
        {
            await artifacts.WriteJsonAsync(CapsuleSummaryFileName, result).ConfigureAwait(false);
        }

        if (readmePath is not null)
        {
            await artifacts.WriteTextAsync(CapsuleReadmeFileName, BuildReadme(artifacts.Root, summaries.Count, failureSessions.Length, primaryFailure, artifactCounts, commandHints)).ConfigureAwait(false);
        }

        return result;
    }

    private static ReplayCapsulePrimaryFailureResult CreatePrimaryFailure(SessionReplaySummary summary)
    {
        var failedScenario = summary.FailureCapsule?.Scenarios.FirstOrDefault();
        var failedStep = failedScenario?.FailedStep;
        var message = failedScenario?.Error?.Message ??
            summary.TimelineHighlights.FirstOrDefault(static entry => entry.IsFailureRelevant)?.Detail;
        return new ReplayCapsulePrimaryFailureResult(
            failedScenario?.Scenario,
            failedStep?.Name,
            failedStep?.Action,
            message,
            summary.FailureCapsulePath,
            summary.TimelinePath);
    }

    private static ReplayCapsuleArtifactCounts CountArtifacts(IReadOnlyList<string> files) =>
        new(
            Count(files, IsScreenshot),
            Count(files, IsVideo),
            Count(files, IsLog),
            Count(files, IsHierarchy),
            Count(files, IsScreenState),
            Count(files, IsReport),
            Count(files, static path => Path.GetFileName(path).Equals(SessionReplayArtifacts.TimelineFileName, StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<ReplayCapsuleCommandHint> BuildCommandHints(string artifactRoot, ReplayCapsulePrimaryFailureResult? primaryFailure)
    {
        yield return new ReplayCapsuleCommandHint($"luotsi replay open --artifacts {Quote(artifactRoot)}", "Open the local artifact browser.");
        yield return new ReplayCapsuleCommandHint($"luotsi replay summarize --artifacts {Quote(artifactRoot)}", "Read session summaries and failure capsule links.");

        if (!string.IsNullOrWhiteSpace(primaryFailure?.Message))
        {
            yield return new ReplayCapsuleCommandHint(
                $"luotsi replay search --artifacts {Quote(artifactRoot)} --contains {Quote(primaryFailure.Message)}",
                "Find the primary failure message across timeline, reports, and logs.");
        }

        yield return new ReplayCapsuleCommandHint(
            $"luotsi replay scenario-draft --artifacts {Quote(artifactRoot)} --output draft-scenario.json",
            "Create a starter scenario from captured inspect/action history when available.");
    }

    private static int Count(IEnumerable<string> files, Func<string, bool> predicate) =>
        files.Count(predicate);

    private static string BuildReadme(
        string artifactRoot,
        int sessionCount,
        int failureCount,
        ReplayCapsulePrimaryFailureResult? primaryFailure,
        ReplayCapsuleArtifactCounts artifactCounts,
        IReadOnlyList<ReplayCapsuleCommandHint> commandHints)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Replay Capsule");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{artifactRoot}`");
        builder.AppendLine($"Sessions: `{sessionCount}`");
        builder.AppendLine($"Failures: `{failureCount}`");
        builder.AppendLine();
        builder.AppendLine("## Primary Failure");
        builder.AppendLine();
        if (primaryFailure is null)
        {
            builder.AppendLine("No failure signal was found in the replay summaries.");
        }
        else
        {
            AppendField(builder, "Scenario", primaryFailure.Scenario);
            AppendField(builder, "Step", primaryFailure.Step);
            AppendField(builder, "Action", primaryFailure.Action);
            AppendField(builder, "Message", primaryFailure.Message);
            AppendField(builder, "Failure capsule", primaryFailure.FailureCapsulePath);
            AppendField(builder, "Timeline", primaryFailure.TimelinePath);
        }

        builder.AppendLine();
        builder.AppendLine("## Artifact Counts");
        builder.AppendLine();
        builder.AppendLine($"- Screenshots: `{artifactCounts.Screenshots}`");
        builder.AppendLine($"- Videos: `{artifactCounts.Videos}`");
        builder.AppendLine($"- Logs: `{artifactCounts.Logs}`");
        builder.AppendLine($"- Hierarchies: `{artifactCounts.Hierarchies}`");
        builder.AppendLine($"- Screen states: `{artifactCounts.ScreenStates}`");
        builder.AppendLine($"- Reports: `{artifactCounts.Reports}`");
        builder.AppendLine($"- Timelines: `{artifactCounts.Timelines}`");
        builder.AppendLine();
        builder.AppendLine("## Next Commands");
        builder.AppendLine();
        foreach (var hint in commandHints)
        {
            builder.AppendLine($"- `{hint.Command}`");
            builder.AppendLine($"  {hint.Purpose}");
        }

        return builder.ToString();
    }

    private static void AppendField(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"- {label}: `{value}`");
        }
    }

    private static bool IsScreenshot(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        return fileName.Contains("screenshot", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideo(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".h264", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLog(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        return fileName.Contains("logcat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".log", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHierarchy(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Contains("hierarchy", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScreenState(string path) =>
        Path.GetFileName(path).Contains("screen-state", StringComparison.OrdinalIgnoreCase);

    private static bool IsReport(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Contains("report", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("junit.xml", StringComparison.OrdinalIgnoreCase);
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;
}
