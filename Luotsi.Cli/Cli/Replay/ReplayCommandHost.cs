using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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
    private const string RunSummaryJsonFileName = "run-summary.json";
    private const string RunSummaryMarkdownFileName = "run-summary.md";
    private readonly ReplayCommandHostDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public async Task<int> RunAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        if (IsOpenCommand(options))
        {
            var openResult = await OpenAsync(options, started, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, openResult, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
            return 0;
        }

        if (IsPacketCommand(options))
        {
            object packetResult = options.HasFlag("check")
                ? await CheckPacketAsync(started, artifacts).ConfigureAwait(false)
                : await PacketAsync(started, artifacts).ConfigureAwait(false);
            _dependencies.EnvelopeWriter.WriteSuccess(options.Command ?? "replay", started, packetResult, artifacts.ToData(), AppCommandConsoleOutputModeResolver.Resolve(options));
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

    private async Task<ReplayOpenResult> OpenAsync(CliOptions options, DateTimeOffset started, ArtifactSession artifacts)
    {
        var jsonPath = options.HasFlag("write-json")
            ? Path.Join(artifacts.Root, ReplayOpenSummaryJsonFileName)
            : null;
        var markdownPath = options.HasFlag("write-markdown")
            ? Path.Join(artifacts.Root, ReplayOpenSummaryMarkdownFileName)
            : null;
        var writeRunSummaryPacket = options.HasFlag("write-json") || options.HasFlag("write-markdown");
        var packet = await CreateRunSummaryAsync(
            started,
            artifacts,
            writeJson: writeRunSummaryPacket,
            writeMarkdown: writeRunSummaryPacket,
            replayOpenJsonPath: jsonPath,
            replayOpenMarkdownPath: markdownPath).ConfigureAwait(false);
        var command = BuildOpenCommand(packet.EntryPoints.IndexHtmlPath);
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
            packet.EntryPoints.IndexHtmlPath,
            packet.EntryPoints.IndexMarkdownPath,
            jsonPath,
            markdownPath,
            packet.EntryPoints.RunSummaryJsonPath,
            packet.EntryPoints.RunSummaryMarkdownPath,
            packet.SessionCount,
            packet.FailureCount,
            packet.PrimaryFailure,
            packet.RecommendedNextAction,
            packet.Commands,
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

    private Task<RunSummaryResult> PacketAsync(DateTimeOffset started, ArtifactSession artifacts) =>
        CreateRunSummaryAsync(started, artifacts, writeJson: true, writeMarkdown: true);

    private async Task<RunSummaryCheckResult> CheckPacketAsync(DateTimeOffset started, ArtifactSession artifacts)
    {
        var packetPath = Path.Join(artifacts.Root, RunSummaryJsonFileName);
        if (!_dependencies.FileSystem.FileExists(packetPath))
        {
            throw new UsageException($"replay packet --check requires {RunSummaryJsonFileName} in the artifact root. Run `luotsi replay packet --artifacts {Quote(artifacts.Root)}` first.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(await _dependencies.FileSystem.ReadAllTextAsync(packetPath).ConfigureAwait(false));
        }
        catch (JsonException ex)
        {
            throw new UsageException($"{RunSummaryJsonFileName} is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            var schema = RequireString(root, "schema", packetPath);
            if (!string.Equals(schema, ResultSchemas.RunSummary, StringComparison.Ordinal))
            {
                throw new UsageException($"{RunSummaryJsonFileName} has unsupported schema '{schema}'. Expected '{ResultSchemas.RunSummary}'.");
            }

            var artifactRoot = RequireString(root, "artifactRoot", packetPath);
            if (!string.Equals(artifactRoot, artifacts.Root, StringComparison.Ordinal))
            {
                throw new UsageException($"{RunSummaryJsonFileName} points at artifact root '{artifactRoot}', but the checked root is '{artifacts.Root}'. Re-run `luotsi replay packet --artifacts {Quote(artifacts.Root)}`.");
            }

            var packetStatus = RequireString(root, "status", packetPath);
            var verdict = RequireString(root, "verdict", packetPath);
            var generatedAt = RequireDateTimeOffsetString(root, "generatedAt", packetPath);
            var sessionCount = RequireInt32(root, "sessionCount", packetPath);
            var failureCount = RequireInt32(root, "failureCount", packetPath);
            var recommendedNextAction = RequireObject(root, "recommendedNextAction", packetPath);
            var recommendedCommand = RequireString(recommendedNextAction, "command", packetPath);
            var recommendedNextActionResult = ReadRecommendedNextAction(recommendedNextAction, packetPath);
            var triageChecklist = ReadTriageChecklist(root, recommendedCommand, packetPath);
            var commands = ReadCommandHints(root, packetPath);
            var primaryFailure = ReadPrimaryFailure(root, packetPath);
            var evidenceFiles = ReadEvidenceFiles(root, packetPath);
            ValidatePrimaryFailureEvidenceFiles(primaryFailure, evidenceFiles, packetPath, artifacts.Root);
            ValidateEvidenceFiles(evidenceFiles, packetPath, artifacts.Root);
            var failureSnapshot = ReadFailureSnapshot(root, primaryFailure, packetPath);
            var entryPoints = RequireObject(root, "entryPoints", packetPath);
            var indexHtmlPath = RequireString(entryPoints, "indexHtmlPath", packetPath);
            if (!_dependencies.FileSystem.FileExists(indexHtmlPath))
            {
                throw new UsageException($"{RunSummaryJsonFileName} points at missing index HTML '{indexHtmlPath}'. Re-run `luotsi replay packet --artifacts {Quote(artifacts.Root)}`.");
            }

            var indexMarkdownPath = RequireString(entryPoints, "indexMarkdownPath", packetPath);
            if (!_dependencies.FileSystem.FileExists(indexMarkdownPath))
            {
                throw new UsageException($"{RunSummaryJsonFileName} points at missing index Markdown '{indexMarkdownPath}'. Re-run `luotsi replay packet --artifacts {Quote(artifacts.Root)}`.");
            }

            var runSummaryJsonPath = RequireString(entryPoints, "runSummaryJsonPath", packetPath);
            if (!string.Equals(runSummaryJsonPath, packetPath, StringComparison.Ordinal))
            {
                throw new UsageException($"{RunSummaryJsonFileName} entryPoints.runSummaryJsonPath points at '{runSummaryJsonPath}', but expected '{packetPath}'. Re-run `luotsi replay packet --artifacts {Quote(artifacts.Root)}`.");
            }

            var runSummaryMarkdownPath = RequireNullableString(entryPoints, "runSummaryMarkdownPath", packetPath);
            if (string.IsNullOrWhiteSpace(runSummaryMarkdownPath))
            {
                throw new UsageException($"{RunSummaryJsonFileName} is missing entryPoints.runSummaryMarkdownPath. Re-run `luotsi replay packet --artifacts {Quote(artifacts.Root)}`.");
            }

            if (!_dependencies.FileSystem.FileExists(runSummaryMarkdownPath))
            {
                throw new UsageException($"{RunSummaryJsonFileName} points at missing Markdown packet '{runSummaryMarkdownPath}'. Re-run `luotsi replay packet --artifacts {Quote(artifacts.Root)}`.");
            }

            var runSummaryMarkdown = await _dependencies.FileSystem.ReadAllTextAsync(runSummaryMarkdownPath).ConfigureAwait(false);
            ValidateAtAGlanceSection(primaryFailure, recommendedCommand, runSummaryMarkdown, artifacts.Root);
            var checkCommand = BuildPacketCheckCommand(artifacts.Root);
            if (!runSummaryMarkdown.Contains("## Packet Gate", StringComparison.Ordinal) ||
                !runSummaryMarkdown.Contains(checkCommand, StringComparison.Ordinal))
            {
                throw new UsageException($"{RunSummaryMarkdownFileName} is missing the packet validation gate command. Re-run `luotsi replay packet --artifacts {Quote(artifacts.Root)}`.");
            }

            if (!runSummaryMarkdown.Contains("## Copy-Paste Triage Commands", StringComparison.Ordinal))
            {
                throw new UsageException($"{RunSummaryMarkdownFileName} is missing the copy-paste triage command block. Re-run `luotsi replay packet --artifacts {Quote(artifacts.Root)}`.");
            }

            ValidateCopyPasteTriageCommands(triageChecklist, runSummaryMarkdown, checkCommand, artifacts.Root);

            ValidateFailureSnapshot(primaryFailure, runSummaryMarkdown, artifacts.Root);
            ValidateMarkdownTriageChecklist(triageChecklist, runSummaryMarkdown, artifacts.Root);
            ValidateFirstActionSection(recommendedNextActionResult, runSummaryMarkdown, artifacts.Root);

            if (!runSummaryMarkdown.Contains(recommendedCommand, StringComparison.Ordinal))
            {
                throw new UsageException($"{RunSummaryMarkdownFileName} does not include the recommended next action command from {RunSummaryJsonFileName}. Re-run `luotsi replay packet --artifacts {Quote(artifacts.Root)}`.");
            }

            ValidatePrimaryFailureHandoff(primaryFailure, triageChecklist, runSummaryMarkdown, packetPath, artifacts.Root);
            ValidatePrimaryFailureSection(primaryFailure, runSummaryMarkdown, artifacts.Root);
            ValidateMarkdownEvidenceFiles(evidenceFiles, runSummaryMarkdown, artifacts.Root);
            ValidateMarkdownCommands(commands, runSummaryMarkdown, artifacts.Root);
            ValidateMarkdownPacketIdentity(generatedAt, packetStatus, verdict, sessionCount, failureCount, runSummaryMarkdown, artifacts.Root);

            return new RunSummaryCheckResult(
                ResultSchemas.RunSummaryCheck,
                started,
                artifacts.Root,
                packetPath,
                "valid",
                packetStatus,
                sessionCount,
                failureCount,
                recommendedCommand,
                failureSnapshot,
                evidenceFiles,
                recommendedNextActionResult,
                triageChecklist,
                primaryFailure,
                commands,
                runSummaryMarkdownPath);
        }
    }

    private static void ValidateCopyPasteTriageCommands(
        IReadOnlyList<RunSummaryChecklistItemResult> triageChecklist,
        string runSummaryMarkdown,
        string checkCommand,
        string artifactRoot)
    {
        var commandBlock = ExtractCopyPasteTriageCommandBlock(runSummaryMarkdown);
        if (string.IsNullOrWhiteSpace(commandBlock))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} is missing the copy-paste triage command block. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        var firstCommand = commandBlock
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (!string.Equals(firstCommand, checkCommand, StringComparison.Ordinal))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} copy-paste triage command block must start with the packet validation gate command. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        foreach (var command in triageChecklist
                     .OrderBy(static item => item.Step)
                     .Select(static item => item.Command)
                     .Where(static command => !string.IsNullOrWhiteSpace(command))
                     .Select(static command => command!)
                     .Distinct(StringComparer.Ordinal)
                     .Where(command => !commandBlock.Contains(command, StringComparison.Ordinal)))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} copy-paste triage command block is missing checklist command '{command}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }
    }

    private static void ValidateMarkdownCommands(
        IReadOnlyList<ReplayOpenCommandHintResult> commands,
        string runSummaryMarkdown,
        string artifactRoot)
    {
        var commandsSection = ExtractMarkdownSection(runSummaryMarkdown, "## Commands");
        if (string.IsNullOrWhiteSpace(commandsSection))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} is missing the commands section. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        foreach (var command in commands
                     .Where(command => !commandsSection.Contains(EscapeMarkdown(command.Command), StringComparison.Ordinal)))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} commands section is missing command '{command.Command}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        foreach (var command in commands
                     .Where(command => !commandsSection.Contains(EscapeMarkdown(command.Description), StringComparison.Ordinal)))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} commands section is missing command description '{command.Description}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }
    }

    private static void ValidateMarkdownEvidenceFiles(
        IReadOnlyList<RunSummaryEvidenceFileResult> evidenceFiles,
        string runSummaryMarkdown,
        string artifactRoot)
    {
        var evidenceSection = ExtractMarkdownSection(runSummaryMarkdown, "## Evidence Files");
        if (evidenceFiles.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(evidenceSection) ||
                !evidenceSection.Contains("No focused evidence files", StringComparison.Ordinal))
            {
                throw new UsageException($"{RunSummaryMarkdownFileName} evidence files section must state that no focused evidence files were found. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(evidenceSection))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} is missing the evidence files section. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        foreach (var evidenceFile in evidenceFiles
                     .Where(evidenceFile => !evidenceSection.Contains(EscapeMarkdown(evidenceFile.Kind), StringComparison.Ordinal)))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} evidence files section is missing evidence kind '{evidenceFile.Kind}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        foreach (var evidenceFile in evidenceFiles
                     .Where(evidenceFile => !evidenceSection.Contains(EscapeMarkdown(evidenceFile.Path), StringComparison.Ordinal)))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} evidence files section is missing evidence path '{evidenceFile.Path}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        foreach (var evidenceFile in evidenceFiles
                     .Where(evidenceFile => !evidenceSection.Contains(EscapeMarkdown(evidenceFile.Description), StringComparison.Ordinal)))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} evidence files section is missing evidence description '{evidenceFile.Description}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }
    }

    private static void ValidateMarkdownTriageChecklist(
        IReadOnlyList<RunSummaryChecklistItemResult> triageChecklist,
        string runSummaryMarkdown,
        string artifactRoot)
    {
        var checklistSection = ExtractMarkdownSection(runSummaryMarkdown, "## 60-Second Triage Checklist");
        if (string.IsNullOrWhiteSpace(checklistSection))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} is missing the 60-second triage checklist. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        foreach (var item in triageChecklist
                     .OrderBy(static item => item.Step)
                     .Where(item => !checklistSection.Contains(EscapeMarkdown(item.Action), StringComparison.Ordinal)))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} 60-second triage checklist is missing checklist action '{item.Action}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        foreach (var item in triageChecklist
                     .OrderBy(static item => item.Step)
                     .Where(item => !string.IsNullOrWhiteSpace(item.Rationale))
                     .Where(item => !checklistSection.Contains(EscapeMarkdown(item.Rationale), StringComparison.Ordinal)))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} 60-second triage checklist is missing checklist rationale '{item.Rationale}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }
    }

    private static void ValidateFirstActionSection(
        ReplayOpenNextActionResult recommendedNextAction,
        string runSummaryMarkdown,
        string artifactRoot)
    {
        var firstActionSection = ExtractMarkdownSection(runSummaryMarkdown, "## First Action");
        if (string.IsNullOrWhiteSpace(firstActionSection))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} is missing the first action explanation. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        foreach (var value in new[]
                 {
                     recommendedNextAction.Title,
                     recommendedNextAction.Kind,
                     recommendedNextAction.Reason,
                     recommendedNextAction.Command
                 }
                 .Where(static value => !string.IsNullOrWhiteSpace(value))
                 .Select(static value => value!)
                 .Where(value => !firstActionSection.Contains(EscapeMarkdown(value), StringComparison.Ordinal)))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} first action explanation is missing recommended action value '{value}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }
    }

    private static void ValidateAtAGlanceSection(
        ReplayOpenPrimaryFailureResult? primaryFailure,
        string recommendedCommand,
        string runSummaryMarkdown,
        string artifactRoot)
    {
        if (!runSummaryMarkdown.Contains("## At a Glance", StringComparison.Ordinal))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} is missing the at-a-glance triage summary. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        if (!runSummaryMarkdown.Contains("Next command", StringComparison.Ordinal) ||
            !runSummaryMarkdown.Contains(recommendedCommand, StringComparison.Ordinal))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} at-a-glance summary is missing the recommended next command. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        if (primaryFailure is not null &&
            (!runSummaryMarkdown.Contains("Evidence command", StringComparison.Ordinal) ||
             (!string.IsNullOrWhiteSpace(primaryFailure.SourceCommand) &&
              !runSummaryMarkdown.Contains(primaryFailure.SourceCommand, StringComparison.Ordinal))))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} at-a-glance summary is missing primaryFailure.sourceCommand. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }
    }

    private static void ValidateMarkdownPacketIdentity(
        string generatedAt,
        string packetStatus,
        string verdict,
        int sessionCount,
        int failureCount,
        string runSummaryMarkdown,
        string artifactRoot)
    {
        foreach (var value in new[]
                 {
                     $"Generated: `{EscapeMarkdown(generatedAt)}`",
                     $"Artifact root: `{EscapeMarkdown(artifactRoot)}`",
                     $"Status: `{EscapeMarkdown(packetStatus)}`",
                     $"Verdict: {EscapeMarkdown(verdict)}",
                     $"Sessions: `{sessionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}`",
                     $"Failures: `{failureCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}`"
                 }
                 .Where(value => !runSummaryMarkdown.Contains(value, StringComparison.Ordinal)))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} is missing packet identity value '{value}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }
    }

    private static string? ExtractCopyPasteTriageCommandBlock(string runSummaryMarkdown)
    {
        const string heading = "## Copy-Paste Triage Commands";
        var headingIndex = runSummaryMarkdown.IndexOf(heading, StringComparison.Ordinal);
        if (headingIndex < 0)
        {
            return null;
        }

        var blockStart = runSummaryMarkdown.IndexOf("```bash", headingIndex + heading.Length, StringComparison.Ordinal);
        if (blockStart < 0)
        {
            return null;
        }

        blockStart += "```bash".Length;
        var blockEnd = runSummaryMarkdown.IndexOf("```", blockStart, StringComparison.Ordinal);
        return blockEnd < 0 ? null : runSummaryMarkdown[blockStart..blockEnd];
    }

    private static string? ExtractMarkdownSection(string markdown, string heading)
    {
        var headingIndex = markdown.IndexOf(heading, StringComparison.Ordinal);
        if (headingIndex < 0)
        {
            return null;
        }

        var contentStart = headingIndex + heading.Length;
        var nextSectionIndex = markdown.IndexOf("\n## ", contentStart, StringComparison.Ordinal);
        return nextSectionIndex < 0
            ? markdown[contentStart..]
            : markdown[contentStart..nextSectionIndex];
    }

    private static void ValidatePrimaryFailureHandoff(
        ReplayOpenPrimaryFailureResult? primaryFailure,
        IReadOnlyList<RunSummaryChecklistItemResult> triageChecklist,
        string runSummaryMarkdown,
        string packetPath,
        string artifactRoot)
    {
        if (primaryFailure is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(primaryFailure.SourceCommand))
        {
            throw new UsageException($"{Path.GetFileName(packetPath)} primaryFailure.sourceCommand is required when primaryFailure is present. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        if (!triageChecklist.Any(item => string.Equals(item.Command, primaryFailure.SourceCommand, StringComparison.Ordinal)))
        {
            throw new UsageException($"{Path.GetFileName(packetPath)} triageChecklist must include primaryFailure.sourceCommand. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        if (!runSummaryMarkdown.Contains(primaryFailure.SourceCommand, StringComparison.Ordinal))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} does not include primaryFailure.sourceCommand from {RunSummaryJsonFileName}. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }
    }

    private void ValidatePrimaryFailureEvidenceFiles(
        ReplayOpenPrimaryFailureResult? primaryFailure,
        IReadOnlyList<RunSummaryEvidenceFileResult> evidenceFiles,
        string packetPath,
        string artifactRoot)
    {
        if (primaryFailure is null)
        {
            if (evidenceFiles.Count > 0)
            {
                throw new UsageException($"{Path.GetFileName(packetPath)} evidenceFiles must be empty when primaryFailure is null. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
            }

            return;
        }

        ValidatePrimaryFailureEvidenceFile(primaryFailure.TimelinePath, "primaryFailure.timelinePath", packetPath, artifactRoot);
        ValidatePrimaryFailureEvidenceFile(primaryFailure.FailureCapsulePath, "primaryFailure.failureCapsulePath", packetPath, artifactRoot);
        ValidatePrimaryFailureEvidenceFileHandoff(primaryFailure.TimelinePath, "timeline", evidenceFiles, packetPath, artifactRoot);
        ValidatePrimaryFailureEvidenceFileHandoff(primaryFailure.FailureCapsulePath, "failure_capsule", evidenceFiles, packetPath, artifactRoot);
    }

    private void ValidatePrimaryFailureEvidenceFile(
        string? evidencePath,
        string fieldName,
        string packetPath,
        string artifactRoot)
    {
        if (string.IsNullOrWhiteSpace(evidencePath))
        {
            return;
        }

        if (!FileExistsInArtifactRoot(evidencePath, artifactRoot))
        {
            throw new UsageException($"{Path.GetFileName(packetPath)} {fieldName} points at missing evidence file '{evidencePath}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }
    }

    private static void ValidatePrimaryFailureEvidenceFileHandoff(
        string? evidencePath,
        string kind,
        IReadOnlyList<RunSummaryEvidenceFileResult> evidenceFiles,
        string packetPath,
        string artifactRoot)
    {
        if (string.IsNullOrWhiteSpace(evidencePath))
        {
            return;
        }

        if (!evidenceFiles.Any(file =>
                string.Equals(file.Kind, kind, StringComparison.Ordinal) &&
                string.Equals(file.Path, evidencePath, StringComparison.Ordinal)))
        {
            throw new UsageException($"{Path.GetFileName(packetPath)} evidenceFiles must include {kind} path '{evidencePath}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }
    }

    private static void ValidatePrimaryFailureSection(
        ReplayOpenPrimaryFailureResult? primaryFailure,
        string runSummaryMarkdown,
        string artifactRoot)
    {
        var primaryFailureSection = ExtractMarkdownSection(runSummaryMarkdown, "## Primary Failure");
        if (string.IsNullOrWhiteSpace(primaryFailureSection))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} is missing the primary failure detail section. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        if (primaryFailure is null)
        {
            if (!primaryFailureSection.Contains("No primary failure", StringComparison.Ordinal))
            {
                throw new UsageException($"{RunSummaryMarkdownFileName} primary failure detail section must state that no primary failure was found. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
            }

            return;
        }

        foreach (var value in new[]
                 {
                     primaryFailure.SessionKind,
                     primaryFailure.SessionId,
                     primaryFailure.Reason,
                     primaryFailure.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     primaryFailure.Target,
                     primaryFailure.Scenario,
                     primaryFailure.Step,
                     primaryFailure.Action,
                     primaryFailure.Message,
                     primaryFailure.TimelinePath,
                     primaryFailure.FailureCapsulePath,
                     primaryFailure.SourceCommand
                 }
                 .Where(static value => !string.IsNullOrWhiteSpace(value))
                 .Select(static value => value!)
                 .Where(value => !primaryFailureSection.Contains(EscapeMarkdown(value), StringComparison.Ordinal)))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} primary failure detail section is missing primary failure value '{value}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }
    }

    private static void ValidateFailureSnapshot(
        ReplayOpenPrimaryFailureResult? primaryFailure,
        string runSummaryMarkdown,
        string artifactRoot)
    {
        if (primaryFailure is null)
        {
            return;
        }

        if (!runSummaryMarkdown.Contains("## Failure Snapshot", StringComparison.Ordinal))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} is missing the failure snapshot. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }

        foreach (var value in new[]
                 {
                     primaryFailure.SessionId,
                     primaryFailure.Reason,
                     primaryFailure.Target,
                     primaryFailure.Scenario,
                     primaryFailure.Step,
                     primaryFailure.Action,
                     primaryFailure.Message
                 }
                 .Where(static value => !string.IsNullOrWhiteSpace(value))
                 .Select(static value => value!)
                 .Where(value => !runSummaryMarkdown.Contains(EscapeMarkdown(value), StringComparison.Ordinal)))
        {
            throw new UsageException($"{RunSummaryMarkdownFileName} failure snapshot is missing primary failure value '{value}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
        }
    }

    private async Task<RunSummaryResult> CreateRunSummaryAsync(
        DateTimeOffset started,
        ArtifactSession artifacts,
        bool writeJson,
        bool writeMarkdown,
        string? replayOpenJsonPath = null,
        string? replayOpenMarkdownPath = null)
    {
        var snapshot = await artifacts.RefreshIndexWithSnapshotAsync().ConfigureAwait(false);
        var indexHtmlPath = Path.Join(artifacts.Root, ArtifactSession.ArtifactHtmlIndexFileName);
        var indexMarkdownPath = Path.Join(artifacts.Root, ArtifactSession.ArtifactIndexFileName);
        var runSummaryJsonPath = writeJson
            ? Path.Join(artifacts.Root, RunSummaryJsonFileName)
            : null;
        var runSummaryMarkdownPath = writeMarkdown
            ? Path.Join(artifacts.Root, RunSummaryMarkdownFileName)
            : null;
        var summaries = snapshot.ReplaySummaries;
        var primaryFailure = CreatePrimaryFailure(artifacts.Root, summaries);
        var commands = BuildOpenCommandHints(artifacts.Root, summaries, primaryFailure).ToArray();
        var nextAction = BuildRecommendedNextAction(artifacts.Root, summaries, primaryFailure, commands);
        var runSummary = BuildRunSummary(
            started,
            artifacts.Root,
            indexHtmlPath,
            indexMarkdownPath,
            replayOpenJsonPath,
            replayOpenMarkdownPath,
            runSummaryJsonPath,
            runSummaryMarkdownPath,
            summaries.Count,
            summaries.Count(static summary => summary.HasFailureSignals),
            primaryFailure,
            nextAction,
            commands);

        if (runSummaryJsonPath is not null)
        {
            await artifacts.WriteJsonAsync(RunSummaryJsonFileName, runSummary).ConfigureAwait(false);
        }

        if (runSummaryMarkdownPath is not null)
        {
            await artifacts.WriteTextAsync(RunSummaryMarkdownFileName, BuildRunSummaryMarkdown(runSummary)).ConfigureAwait(false);
        }

        return runSummary;
    }

    private static RunSummaryResult BuildRunSummary(
        DateTimeOffset generatedAt,
        string artifactRoot,
        string indexHtmlPath,
        string indexMarkdownPath,
        string? replayOpenJsonPath,
        string? replayOpenMarkdownPath,
        string? runSummaryJsonPath,
        string? runSummaryMarkdownPath,
        int sessionCount,
        int failureCount,
        ReplayOpenPrimaryFailureResult? primaryFailure,
        ReplayOpenNextActionResult recommendedNextAction,
        IReadOnlyList<ReplayOpenCommandHintResult> commands)
    {
        var status = primaryFailure is not null
            ? "needs_triage"
            : sessionCount > 0
                ? "passed_or_incomplete"
                : "no_replay_metadata";
        var verdict = primaryFailure is not null
            ? "Failure signals found. Start with the recommended next action before broad artifact browsing."
            : sessionCount > 0
                ? "No failure signals found in replay metadata. Write a capsule or inspect the timeline for context."
                : "No replay metadata found. Inspect the artifact index and verify the run wrote session replay files.";

        var triageChecklist = BuildTriageChecklist(primaryFailure, recommendedNextAction);

        return new RunSummaryResult(
            ResultSchemas.RunSummary,
            generatedAt,
            artifactRoot,
            status,
            verdict,
            sessionCount,
            failureCount,
            triageChecklist,
            CreateFailureSnapshot(primaryFailure),
            BuildEvidenceFiles(primaryFailure),
            primaryFailure,
            recommendedNextAction,
            new RunSummaryEntryPoints(
                indexHtmlPath,
                indexMarkdownPath,
                replayOpenJsonPath,
                replayOpenMarkdownPath,
                runSummaryJsonPath,
                runSummaryMarkdownPath),
            commands);
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
            AppendField(builder, "Session kind", result.PrimaryFailure.SessionKind);
            AppendField(builder, "Session ID", result.PrimaryFailure.SessionId);
            AppendField(builder, "Started", result.PrimaryFailure.StartedAt.ToString("O"));
            AppendField(builder, "Ended", result.PrimaryFailure.EndedAt.ToString("O"));
            AppendField(builder, "Reason", result.PrimaryFailure.Reason);
            AppendField(builder, "Exit code", result.PrimaryFailure.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendField(builder, "Target", result.PrimaryFailure.Target);
            AppendField(builder, "Scenario", result.PrimaryFailure.Scenario);
            AppendField(builder, "Step", result.PrimaryFailure.Step);
            AppendField(builder, "Action", result.PrimaryFailure.Action);
            AppendField(builder, "Message", result.PrimaryFailure.Message);
            AppendField(builder, "Timeline", result.PrimaryFailure.TimelinePath);
            AppendField(builder, "Failure capsule", result.PrimaryFailure.FailureCapsulePath);
            AppendField(builder, "Reopen", result.PrimaryFailure.SourceCommand);
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

    private static string BuildRunSummaryMarkdown(RunSummaryResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Run Summary");
        builder.AppendLine();
        builder.AppendLine($"Generated: `{result.GeneratedAt:O}`");
        builder.AppendLine($"Artifact root: `{EscapeMarkdown(result.ArtifactRoot)}`");
        builder.AppendLine($"Status: `{EscapeMarkdown(result.Status)}`");
        builder.AppendLine($"Verdict: {EscapeMarkdown(result.Verdict)}");
        builder.AppendLine($"Sessions: `{result.SessionCount}`");
        builder.AppendLine($"Failures: `{result.FailureCount}`");
        builder.AppendLine();
        builder.AppendLine("## At a Glance");
        builder.AppendLine();
        builder.AppendLine($"- Status: `{EscapeMarkdown(result.Status)}`");
        builder.AppendLine($"- Next command: `{EscapeMarkdown(result.RecommendedNextAction.Command)}`");
        if (!string.IsNullOrWhiteSpace(result.PrimaryFailure?.SourceCommand))
        {
            builder.AppendLine($"- Evidence command: `{EscapeMarkdown(result.PrimaryFailure.SourceCommand)}`");
        }
        builder.AppendLine();
        builder.AppendLine("## Failure Snapshot");
        builder.AppendLine();
        AppendFailureSnapshot(builder, result.PrimaryFailure);
        builder.AppendLine();
        builder.AppendLine("## Evidence Files");
        builder.AppendLine();
        AppendEvidenceFiles(builder, result.EvidenceFiles);
        builder.AppendLine();
        builder.AppendLine("## Packet Gate");
        builder.AppendLine();
        builder.AppendLine("Run this first when the packet came from CI, support, or another agent.");
        builder.AppendLine();
        builder.AppendLine("```bash");
        builder.AppendLine(BuildPacketCheckCommand(result.ArtifactRoot));
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## Copy-Paste Triage Commands");
        builder.AppendLine();
        var firstMinuteCommands = result.TriageChecklist
            .OrderBy(static item => item.Step)
            .Select(static item => item.Command)
            .Where(static command => !string.IsNullOrWhiteSpace(command))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (firstMinuteCommands.Length == 0)
        {
            builder.AppendLine("No command-only triage path is available in this packet.");
        }
        else
        {
            builder.AppendLine("```bash");
            builder.AppendLine(BuildPacketCheckCommand(result.ArtifactRoot));
            foreach (var command in firstMinuteCommands)
            {
                builder.AppendLine(command);
            }

            builder.AppendLine("```");
        }

        builder.AppendLine();
        builder.AppendLine("## 60-Second Triage Checklist");
        builder.AppendLine();
        foreach (var item in result.TriageChecklist.OrderBy(static item => item.Step))
        {
            if (string.IsNullOrWhiteSpace(item.Command))
            {
                builder.AppendLine($"{item.Step}. {EscapeMarkdown(item.Action)}");
            }
            else
            {
                builder.AppendLine($"{item.Step}. {EscapeMarkdown(item.Action)} `{EscapeMarkdown(item.Command)}`");
            }

            builder.AppendLine($"   - Why: {EscapeMarkdown(item.Rationale)}");
        }

        builder.AppendLine();
        builder.AppendLine("## First Action");
        builder.AppendLine();
        builder.AppendLine($"- **{EscapeMarkdown(result.RecommendedNextAction.Title)}** (`{EscapeMarkdown(result.RecommendedNextAction.Kind)}`)");
        builder.AppendLine($"  {EscapeMarkdown(result.RecommendedNextAction.Reason)}");
        builder.AppendLine($"  `{EscapeMarkdown(result.RecommendedNextAction.Command)}`");
        builder.AppendLine();
        builder.AppendLine("## Primary Failure");
        builder.AppendLine();
        if (result.PrimaryFailure is null)
        {
            builder.AppendLine("No primary failure was found in replay metadata.");
        }
        else
        {
            AppendField(builder, "Session kind", result.PrimaryFailure.SessionKind);
            AppendField(builder, "Session ID", result.PrimaryFailure.SessionId);
            AppendField(builder, "Started", result.PrimaryFailure.StartedAt.ToString("O"));
            AppendField(builder, "Ended", result.PrimaryFailure.EndedAt.ToString("O"));
            AppendField(builder, "Reason", result.PrimaryFailure.Reason);
            AppendField(builder, "Exit code", result.PrimaryFailure.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendField(builder, "Target", result.PrimaryFailure.Target);
            AppendField(builder, "Scenario", result.PrimaryFailure.Scenario);
            AppendField(builder, "Step", result.PrimaryFailure.Step);
            AppendField(builder, "Action", result.PrimaryFailure.Action);
            AppendField(builder, "Message", result.PrimaryFailure.Message);
            AppendField(builder, "Timeline", result.PrimaryFailure.TimelinePath);
            AppendField(builder, "Failure capsule", result.PrimaryFailure.FailureCapsulePath);
            AppendField(builder, "Reopen", result.PrimaryFailure.SourceCommand);
        }

        builder.AppendLine();
        builder.AppendLine("## Entry Points");
        builder.AppendLine();
        AppendField(builder, "Index HTML", result.EntryPoints.IndexHtmlPath);
        AppendField(builder, "Index Markdown", result.EntryPoints.IndexMarkdownPath);
        AppendField(builder, "Replay open JSON", result.EntryPoints.ReplayOpenJsonPath);
        AppendField(builder, "Replay open Markdown", result.EntryPoints.ReplayOpenMarkdownPath);
        AppendField(builder, "Run summary JSON", result.EntryPoints.RunSummaryJsonPath);
        AppendField(builder, "Run summary Markdown", result.EntryPoints.RunSummaryMarkdownPath);
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

    private static void AppendFailureSnapshot(StringBuilder builder, ReplayOpenPrimaryFailureResult? primaryFailure)
    {
        if (primaryFailure is null)
        {
            builder.AppendLine("No primary failure was found in replay metadata.");
            return;
        }

        AppendField(builder, "Session", primaryFailure.SessionId);
        AppendField(builder, "Reason", primaryFailure.Reason);
        AppendField(builder, "Target", primaryFailure.Target);
        AppendField(builder, "Scenario", primaryFailure.Scenario);
        AppendField(builder, "Step", primaryFailure.Step);
        AppendField(builder, "Action", primaryFailure.Action);
        AppendField(builder, "Message", primaryFailure.Message);
    }

    private static void AppendEvidenceFiles(StringBuilder builder, IReadOnlyList<RunSummaryEvidenceFileResult> evidenceFiles)
    {
        if (evidenceFiles.Count == 0)
        {
            builder.AppendLine("No focused evidence files were found in replay metadata.");
            return;
        }

        foreach (var evidenceFile in evidenceFiles)
        {
            builder.AppendLine($"- {EscapeMarkdown(evidenceFile.Kind)}: `{EscapeMarkdown(evidenceFile.Path)}`");
            builder.AppendLine($"  {EscapeMarkdown(evidenceFile.Description)}");
        }
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

    private static ReplayOpenPrimaryFailureResult? CreatePrimaryFailure(string artifactRoot, IReadOnlyList<SessionReplaySummary> summaries)
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
            summary.SessionKind,
            summary.SessionId,
            summary.StartedAt,
            summary.EndedAt,
            summary.Reason,
            summary.ExitCode,
            summary.Target,
            failedScenario?.Scenario,
            failedStep?.Name,
            failedStep?.Action,
            failedScenario?.Error?.Message ?? failureHighlight?.Detail,
            summary.TimelinePath,
            summary.FailureCapsulePath,
            BuildPrimaryFailureSourceCommand(artifactRoot, summary, failureHighlight));
    }

    private static IReadOnlyList<RunSummaryEvidenceFileResult> BuildEvidenceFiles(ReplayOpenPrimaryFailureResult? primaryFailure)
    {
        if (primaryFailure is null)
        {
            return [];
        }

        var evidenceFiles = new List<RunSummaryEvidenceFileResult>();
        if (!string.IsNullOrWhiteSpace(primaryFailure.TimelinePath))
        {
            evidenceFiles.Add(new RunSummaryEvidenceFileResult(
                "timeline",
                primaryFailure.TimelinePath,
                "Ordered replay events around the primary failure."));
        }

        if (!string.IsNullOrWhiteSpace(primaryFailure.FailureCapsulePath))
        {
            evidenceFiles.Add(new RunSummaryEvidenceFileResult(
                "failure_capsule",
                primaryFailure.FailureCapsulePath,
                "Focused failure capsule with failed scenario, step, artifacts, and error."));
        }

        return evidenceFiles;
    }

    private static string? BuildPrimaryFailureSourceCommand(
        string artifactRoot,
        SessionReplaySummary summary,
        SessionReplayTimelineEntry? failureHighlight)
    {
        if (failureHighlight is not null && !string.IsNullOrWhiteSpace(summary.TimelinePath))
        {
            return BuildTimelineSourceCommand(artifactRoot, summary.TimelinePath, failureHighlight.Sequence);
        }

        if (!string.IsNullOrWhiteSpace(summary.FailureCapsulePath))
        {
            return $"luotsi replay capsule --artifacts {Quote(artifactRoot)} --write-readme --write-json";
        }

        if (!string.IsNullOrWhiteSpace(summary.TimelinePath))
        {
            return $"luotsi replay timeline --artifacts {Quote(artifactRoot)} --context 3 --write-markdown";
        }

        return null;
    }

    private static IReadOnlyList<RunSummaryChecklistItemResult> BuildTriageChecklist(
        ReplayOpenPrimaryFailureResult? primaryFailure,
        ReplayOpenNextActionResult recommendedNextAction)
    {
        if (primaryFailure is null)
        {
            return [
                new RunSummaryChecklistItemResult(
                    1,
                    "Run the recommended packet command",
                    recommendedNextAction.Command,
                    "This is the highest-signal next command computed from replay metadata."),
                new RunSummaryChecklistItemResult(
                    2,
                    "Confirm whether the run passed, is incomplete, or lacks replay metadata",
                    null,
                    "A missing primary failure is not the same as a pass."),
                new RunSummaryChecklistItemResult(
                    3,
                    "Use the artifact index only after the packet command and replay metadata have been checked",
                    null,
                    "The index is broader and less focused than the packet.")
            ];
        }

        return [
            new RunSummaryChecklistItemResult(
                1,
                "Run the recommended packet command",
                recommendedNextAction.Command,
                "This is the highest-signal next command computed from replay metadata."),
            new RunSummaryChecklistItemResult(
                2,
                "Read the primary failure fields before opening broad artifacts",
                primaryFailure.SourceCommand,
                "Session identity and focused timeline evidence should be understood before broad artifact browsing."),
            new RunSummaryChecklistItemResult(
                3,
                "Use the commands section only after the focused failure window is understood",
                null,
                "Follow-up commands are useful after the first failure window is clear.")
        ];
    }

    private static IEnumerable<ReplayOpenCommandHintResult> BuildOpenCommandHints(
        string artifactRoot,
        IReadOnlyList<SessionReplaySummary> summaries,
        ReplayOpenPrimaryFailureResult? primaryFailure)
    {
        yield return new ReplayOpenCommandHintResult(
            "replay_packet",
            "Write run-summary.json and run-summary.md for the durable first-minute packet.",
            $"luotsi replay packet --artifacts {Quote(artifactRoot)}");
        yield return new ReplayOpenCommandHintResult(
            "replay_packet_check",
            "Validate the durable first-minute packet before handoff.",
            BuildPacketCheckCommand(artifactRoot));
        yield return new ReplayOpenCommandHintResult(
            "pack_artifacts",
            "Pack this artifact root for CI upload or replay handoff.",
            $"luotsi artifacts pack {Quote(artifactRoot)}");
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
            $"luotsi artifacts open {Quote(artifactRoot)}");
    }

    private static string RequireString(JsonElement element, string propertyName, string sourcePath)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} is missing string property '{propertyName}'.");
        }

        return property.GetString()!;
    }

    private static string? RequireNullableString(JsonElement element, string propertyName, string sourcePath)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} is missing string property '{propertyName}'.");
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} property '{propertyName}' must be a string or null.");
        }

        return property.GetString();
    }

    private static int RequireInt32(JsonElement element, string propertyName, string sourcePath)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value))
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} is missing integer property '{propertyName}'.");
        }

        return value;
    }

    private static JsonElement RequireObject(JsonElement element, string propertyName, string sourcePath)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} is missing object property '{propertyName}'.");
        }

        return property;
    }

    private static ReplayOpenNextActionResult ReadRecommendedNextAction(JsonElement element, string sourcePath) =>
        new(
            RequireString(element, "kind", sourcePath),
            RequireString(element, "title", sourcePath),
            RequireString(element, "reason", sourcePath),
            RequireString(element, "command", sourcePath));

    private static IReadOnlyList<ReplayOpenCommandHintResult> ReadCommandHints(JsonElement root, string sourcePath)
    {
        if (!root.TryGetProperty("commands", out var commands) ||
            commands.ValueKind != JsonValueKind.Array ||
            commands.GetArrayLength() == 0)
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} is missing non-empty array property 'commands'.");
        }

        return commands.EnumerateArray()
            .Select(command => new ReplayOpenCommandHintResult(
                RequireString(command, "kind", sourcePath),
                RequireString(command, "description", sourcePath),
                RequireString(command, "command", sourcePath)))
            .ToArray();
    }

    private static IReadOnlyList<RunSummaryEvidenceFileResult> ReadEvidenceFiles(JsonElement root, string sourcePath)
    {
        if (!root.TryGetProperty("evidenceFiles", out var evidenceFiles) ||
            evidenceFiles.ValueKind != JsonValueKind.Array)
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} is missing array property 'evidenceFiles'.");
        }

        return evidenceFiles.EnumerateArray()
            .Select(evidenceFile => new RunSummaryEvidenceFileResult(
                RequireString(evidenceFile, "kind", sourcePath),
                RequireString(evidenceFile, "path", sourcePath),
                RequireString(evidenceFile, "description", sourcePath)))
            .ToArray();
    }

    private void ValidateEvidenceFiles(
        IReadOnlyList<RunSummaryEvidenceFileResult> evidenceFiles,
        string packetPath,
        string artifactRoot)
    {
        foreach (var evidenceFile in evidenceFiles)
        {
            if (!FileExistsInArtifactRoot(evidenceFile.Path, artifactRoot))
            {
                throw new UsageException($"{Path.GetFileName(packetPath)} evidenceFiles entry '{evidenceFile.Kind}' points at missing evidence file '{evidenceFile.Path}'. Re-run `luotsi replay packet --artifacts {Quote(artifactRoot)}`.");
            }

            var evidencePath = ResolveArtifactEvidencePath(evidenceFile.Path, artifactRoot);
            Stream stream;
            try
            {
                stream = _dependencies.FileSystem.OpenRead(evidencePath);
            }
            catch (IOException ex)
            {
                throw new UsageException($"{Path.GetFileName(packetPath)} evidenceFiles entry '{evidenceFile.Kind}' could not open evidence file '{evidenceFile.Path}': {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new UsageException($"{Path.GetFileName(packetPath)} evidenceFiles entry '{evidenceFile.Kind}' could not open evidence file '{evidenceFile.Path}': {ex.Message}");
            }

            using (stream)
            {
                // Bounded non-empty check, not payload parsing or integrity verification.
                if (stream.ReadByte() == -1)
                {
                    throw new UsageException($"{Path.GetFileName(packetPath)} evidenceFiles entry '{evidenceFile.Kind}' points at empty evidence file '{evidenceFile.Path}'. Restore the evidence from the original package before checking the packet again.");
                }
            }
        }
    }

    private bool FileExistsInArtifactRoot(string path, string artifactRoot) =>
        _dependencies.FileSystem.FileExists(ResolveArtifactEvidencePath(path, artifactRoot));

    private static string ResolveArtifactEvidencePath(string path, string artifactRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new UsageException($"Evidence path '{path}' must be relative and stay inside artifact root '{artifactRoot}'.");
        }

        var fullRoot = Path.GetFullPath(artifactRoot);
        var fullPath = Path.GetFullPath(Path.Join(fullRoot, path));
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar) || fullRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException($"Evidence path '{path}' must stay inside artifact root '{artifactRoot}'.");
        }

        return fullPath;
    }

    private static IReadOnlyList<RunSummaryChecklistItemResult> ReadTriageChecklist(JsonElement root, string recommendedCommand, string sourcePath)
    {
        if (!root.TryGetProperty("triageChecklist", out var checklist) ||
            checklist.ValueKind != JsonValueKind.Array ||
            checklist.GetArrayLength() == 0)
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} is missing non-empty array property 'triageChecklist'.");
        }

        var items = checklist.EnumerateArray()
            .Select(item => new RunSummaryChecklistItemResult(
                RequireInt32(item, "step", sourcePath),
                RequireString(item, "action", sourcePath),
                RequireNullableString(item, "command", sourcePath),
                RequireString(item, "rationale", sourcePath)))
            .OrderBy(static item => item.Step)
            .ToArray();

        var firstStep = items[0];
        if (firstStep.Step != 1)
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} triageChecklist[0].step must be 1.");
        }

        if (!string.Equals(firstStep.Command, recommendedCommand, StringComparison.Ordinal))
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} triageChecklist[0].command must match recommendedNextAction.command.");
        }

        return items;
    }

    private static ReplayOpenPrimaryFailureResult? ReadPrimaryFailure(JsonElement root, string sourcePath)
    {
        if (!root.TryGetProperty("primaryFailure", out var primaryFailure) ||
            primaryFailure.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (primaryFailure.ValueKind != JsonValueKind.Object)
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} property 'primaryFailure' must be an object or null.");
        }

        return new ReplayOpenPrimaryFailureResult(
            RequireString(primaryFailure, "sessionKind", sourcePath),
            RequireString(primaryFailure, "sessionId", sourcePath),
            RequireDateTimeOffset(primaryFailure, "startedAt", sourcePath),
            RequireDateTimeOffset(primaryFailure, "endedAt", sourcePath),
            RequireString(primaryFailure, "reason", sourcePath),
            RequireInt32(primaryFailure, "exitCode", sourcePath),
            RequireNullableString(primaryFailure, "target", sourcePath),
            RequireNullableString(primaryFailure, "scenario", sourcePath),
            RequireNullableString(primaryFailure, "step", sourcePath),
            RequireNullableString(primaryFailure, "action", sourcePath),
            RequireNullableString(primaryFailure, "message", sourcePath),
            RequireNullableString(primaryFailure, "timelinePath", sourcePath),
            RequireNullableString(primaryFailure, "failureCapsulePath", sourcePath),
            RequireNullableString(primaryFailure, "sourceCommand", sourcePath));
    }

    private static RunSummaryFailureSnapshotResult? ReadFailureSnapshot(
        JsonElement root,
        ReplayOpenPrimaryFailureResult? primaryFailure,
        string sourcePath)
    {
        if (primaryFailure is null)
        {
            return null;
        }

        if (!root.TryGetProperty("failureSnapshot", out var snapshot) ||
            snapshot.ValueKind == JsonValueKind.Null)
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} is missing object property 'failureSnapshot' when primaryFailure is present.");
        }

        if (snapshot.ValueKind != JsonValueKind.Object)
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} property 'failureSnapshot' must be an object or null.");
        }

        var result = new RunSummaryFailureSnapshotResult(
            RequireString(snapshot, "sessionId", sourcePath),
            RequireString(snapshot, "reason", sourcePath),
            RequireNullableString(snapshot, "target", sourcePath),
            RequireNullableString(snapshot, "scenario", sourcePath),
            RequireNullableString(snapshot, "step", sourcePath),
            RequireNullableString(snapshot, "action", sourcePath),
            RequireNullableString(snapshot, "message", sourcePath));

        ValidateFailureSnapshotMatchesPrimaryFailure(result, primaryFailure, sourcePath);
        return result;
    }

    private static RunSummaryFailureSnapshotResult? CreateFailureSnapshot(ReplayOpenPrimaryFailureResult? primaryFailure) =>
        primaryFailure is null
            ? null
            : new RunSummaryFailureSnapshotResult(
                primaryFailure.SessionId,
                primaryFailure.Reason,
                primaryFailure.Target,
                primaryFailure.Scenario,
                primaryFailure.Step,
                primaryFailure.Action,
                primaryFailure.Message);

    private static void ValidateFailureSnapshotMatchesPrimaryFailure(
        RunSummaryFailureSnapshotResult snapshot,
        ReplayOpenPrimaryFailureResult primaryFailure,
        string sourcePath)
    {
        if (!string.Equals(snapshot.SessionId, primaryFailure.SessionId, StringComparison.Ordinal) ||
            !string.Equals(snapshot.Reason, primaryFailure.Reason, StringComparison.Ordinal) ||
            !string.Equals(snapshot.Target, primaryFailure.Target, StringComparison.Ordinal) ||
            !string.Equals(snapshot.Scenario, primaryFailure.Scenario, StringComparison.Ordinal) ||
            !string.Equals(snapshot.Step, primaryFailure.Step, StringComparison.Ordinal) ||
            !string.Equals(snapshot.Action, primaryFailure.Action, StringComparison.Ordinal) ||
            !string.Equals(snapshot.Message, primaryFailure.Message, StringComparison.Ordinal))
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} failureSnapshot must match primaryFailure.");
        }
    }

    private static DateTimeOffset RequireDateTimeOffset(JsonElement element, string propertyName, string sourcePath)
    {
        var value = RequireDateTimeOffsetString(element, propertyName, sourcePath);
        return DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    private static string RequireDateTimeOffsetString(JsonElement element, string propertyName, string sourcePath)
    {
        var value = RequireString(element, propertyName, sourcePath);
        if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new UsageException($"{Path.GetFileName(sourcePath)} property '{propertyName}' must be an RFC 3339 timestamp.");
        }

        return parsed.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
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

    private static bool IsPacketCommand(CliOptions options) =>
        options.Arguments.Count > 0 &&
        (string.Equals(options.Arguments[0], "packet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.Arguments[0], "triage", StringComparison.OrdinalIgnoreCase));

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

    private static string BuildTimelineSourceCommand(string artifactRoot, string timelinePath, int sequence)
    {
        var sourcePath = Path.IsPathRooted(timelinePath)
            ? timelinePath
            : Path.GetFullPath(Path.Join(artifactRoot, timelinePath));
        return $"luotsi replay scrub --source-path {Quote(sourcePath)} --sequence {sequence} --context 3";
    }

    private static string BuildPacketCheckCommand(string artifactRoot) =>
        $"luotsi replay packet --artifacts {Quote(artifactRoot)} --check";

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
    IFileSystem FileSystem,
    IProcessRunner ProcessRunner,
    ReplayScenarioDraftService ScenarioDraftService,
    ReplaySearchService SearchService,
    ReplayCapsuleService CapsuleService,
    ReplayTimelineService TimelineService,
    ReplayScrubService ScrubService,
    ReplayGraphService GraphService,
    ReplayClusterService ClusterService);
