using System.Text.Json;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Envelope;

internal sealed class AppCommandHumanFormatter(IConsoleIo console)
{
    private const int MaxScalarLines = 16;
    private const int MaxArrayItems = 5;

    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));

    public void Write(CommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.Ok)
        {
            WriteSuccess(envelope);
            return;
        }

        WriteFailure(envelope);
    }

    private void WriteSuccess(CommandEnvelope envelope)
    {
        _console.WriteLine(FormatSuccessHeading(envelope));
        WriteDataSummary(envelope.Data, envelope.Artifacts);
        WriteArtifactSummary(envelope.Artifacts);
    }

    private void WriteFailure(CommandEnvelope envelope)
    {
        var error = envelope.Error;
        var category = string.IsNullOrWhiteSpace(error?.Category) ? "error" : error.Category;
        var message = string.IsNullOrWhiteSpace(error?.Message) ? "Command failed." : error.Message;
        _console.WriteLine($"FAIL {FormatCommand(envelope.Command)} failed in {envelope.DurationMs} ms.");
        _console.WriteLine($"  {category}: {message}");
        WriteDataSummary(envelope.Data, envelope.Artifacts);
        WriteArtifactSummary(envelope.Artifacts);
    }

    private static string FormatSuccessHeading(CommandEnvelope envelope)
    {
        if (TryResolveFailureStatus(envelope.Data))
        {
            return $"FAIL {FormatCommand(envelope.Command)} finished in {envelope.DurationMs} ms.";
        }

        return $"OK  {FormatCommand(envelope.Command)} completed in {envelope.DurationMs} ms.";
    }

    private static bool TryResolveFailureStatus(object? data)
    {
        if (data is null)
        {
            return false;
        }

        var json = JsonSerializer.SerializeToElement(data, AppCommandJson.Options);
        return json.ValueKind == JsonValueKind.Object &&
            TryGetScalarProperty(json, "status", out var status) &&
            string.Equals(status.GetString(), "failed", StringComparison.OrdinalIgnoreCase);
    }

    private void WriteDataSummary(object? data, ArtifactData artifacts)
    {
        if (data is null)
        {
            return;
        }

        var json = JsonSerializer.SerializeToElement(data, AppCommandJson.Options);
        if (json.ValueKind != JsonValueKind.Object)
        {
            _console.WriteLine($"  result: {FormatScalar(json)}");
            return;
        }

        foreach (var line in SummarizeObject(json, artifacts).Take(MaxScalarLines))
        {
            _console.WriteLine($"  {line}");
        }
    }

    private void WriteArtifactSummary(ArtifactData artifacts)
    {
        if (!string.IsNullOrWhiteSpace(artifacts.ArtifactRoot))
        {
            _console.WriteLine($"  artifacts: {artifacts.ArtifactRoot}");
        }
    }

    private static IEnumerable<string> SummarizeObject(JsonElement value, ArtifactData artifacts)
    {
        if (TryBuildCapsuleSummary(value, artifacts, out var capsuleLines))
        {
            return capsuleLines;
        }

        var lines = new List<string>();

        AddScalar(lines, value, "status");
        AddScalar(lines, value, "ready");
        AddScalar(lines, value, "fix");
        AddScalar(lines, value, "serial");
        AddScalar(lines, value, "device_selector");
        AddScalar(lines, value, "model");
        AddScalar(lines, value, "android_release");
        AddScalar(lines, value, "sdk");
        AddScalar(lines, value, "package");
        AddScalar(lines, value, "activity");
        AddScalar(lines, value, "uri");
        AddScalar(lines, value, "output");
        AddScalar(lines, value, "path");
        AddScalar(lines, value, "file");
        AddScalar(lines, value, "total");
        AddScalar(lines, value, "passed");
        AddScalar(lines, value, "failed");
        AddScalar(lines, value, "skipped");
        AddScalar(lines, value, "selected_count");
        AddScalar(lines, value, "passed_count");
        AddScalar(lines, value, "failed_count");
        AddScalar(lines, value, "session_count");
        AddScalar(lines, value, "failure_count");
        AddScalar(lines, value, "scenario_count");
        AddScalar(lines, value, "matched_line");
        AddScalar(lines, value, "line_count");
        AddScalar(lines, value, "runtime_version");
        AddScalar(lines, value, "installed_tag");
        AddScalar(lines, value, "view_extras");

        AddArtifactIntakeSummary(lines, value);
        AddRecommendedCommandWithGenericFallback(lines, value, artifacts);
        AddArraySummary(lines, value, "artifact_commands");
        AddArraySummary(lines, value, "recommended_commands");

        AddArraySummary(lines, value, "devices");
        AddArraySummary(lines, value, "scenarios");
        AddArraySummary(lines, value, "checks");
        AddArraySummary(lines, value, "repairs");
        AddArraySummary(lines, value, "commands");
        AddArraySummary(lines, value, "artifacts");
        AddArraySummary(lines, value, "entries");
        AddArraySummary(lines, value, "packages");
        AddArraySummary(lines, value, "services");
        AddArraySummary(lines, value, "profiles");
        AddArraySummary(lines, value, "recommended_next_steps");
        AddArraySummary(lines, value, "next_actions");
        AddArraySummary(lines, value, "suggested_commands");

        if (lines.Count == 0)
        {
            lines.Add("result: available with --json");
        }

        return lines;
    }

    private static bool TryBuildCapsuleSummary(JsonElement value, ArtifactData artifacts, out IReadOnlyList<string> lines)
    {
        if (IsSchema(value, ResultSchemas.Quickstart))
        {
            lines = BuildQuickstartSummary(value);
            return true;
        }

        if (IsSchema(value, ResultSchemas.ReplayOpen))
        {
            lines = BuildReplayOpenSummary(value, artifacts);
            return true;
        }

        if (IsSchema(value, ResultSchemas.ReplayCapsule))
        {
            lines = BuildReplayCapsuleSummary(value, artifacts);
            return true;
        }

        if (IsSchema(value, ResultSchemas.RunSummary) ||
            IsSchema(value, ResultSchemas.RunSummaryCheck))
        {
            lines = BuildRunSummaryPacketSummary(value, artifacts);
            return true;
        }

        if (LooksLikeScenarioRunBatch(value))
        {
            lines = BuildScenarioRunBatchSummary(value, artifacts);
            return true;
        }

        if (LooksLikeScenarioRunFailure(value))
        {
            lines = BuildScenarioRunFailureSummary(value, artifacts);
            return true;
        }

        if (IsSchema(value, ResultSchemas.FailureBundle))
        {
            lines = BuildFailureBundleSummary(value, artifacts);
            return true;
        }

        lines = Array.Empty<string>();
        return false;
    }

    private static IReadOnlyList<string> BuildQuickstartSummary(JsonElement value)
    {
        var lines = new List<string>();
        AddScalar(lines, value, "status");
        AddScalar(lines, value, "time_budget");
        AddScalar(lines, value, "goal");
        AddScalar(lines, value, "first_command");

        if (value.TryGetProperty("inputs", out var inputs) && inputs.ValueKind == JsonValueKind.Object)
        {
            var inputParts = new List<string>();
            AddPart(inputParts, inputs, "device");
            AddPart(inputParts, inputs, "package");
            AddPart(inputParts, inputs, "artifacts");
            AddPart(inputParts, inputs, "scenario_path");
            if (inputParts.Count > 0)
            {
                lines.Add("inputs: " + string.Join("; ", inputParts));
            }
        }

        AddQuickstartStepSummary(lines, value);
        AddRecommendedCommand(lines, value);
        AddMultilineScalar(lines, value, "agent_prompt");
        return lines;
    }

    private static void AddQuickstartStepSummary(List<string> lines, JsonElement value)
    {
        if (!value.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        lines.Add($"steps: {steps.GetArrayLength()}");
        foreach (var step in steps.EnumerateArray().Take(MaxArrayItems))
        {
            if (step.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var minute = TryGetInt32(step, "minute", out var valueMinute) ? $"minute {valueMinute}" : null;
            var title = TryGetString(step, "title");
            var command = TryGetString(step, "command");
            var summary = string.Join("; ", new[] { minute, title, command is null ? null : $"command={command}" }
                .Where(static part => !string.IsNullOrWhiteSpace(part)));
            if (!string.IsNullOrWhiteSpace(summary))
            {
                lines.Add($"  - {summary}");
            }
        }
    }

    private static IReadOnlyList<string> BuildReplayOpenSummary(JsonElement value, ArtifactData artifacts)
    {
        var lines = new List<string>();
        AddTriageSummary(lines, value);
        AddPrimaryFailureSummaryFromProperty(lines, value, "primary_failure");
        AddNextStepTitle(lines, value);
        AddRecommendedCommandWithFallback(lines, value, artifacts);
        AddArraySummary(lines, value, "commands");
        return lines;
    }

    private static IReadOnlyList<string> BuildReplayCapsuleSummary(JsonElement value, ArtifactData artifacts)
    {
        var lines = new List<string>();
        AddTriageSummary(lines, value);
        AddPrimaryFailureSummaryFromProperty(lines, value, "primary_failure");
        if (TryBuildArtifactCountsSummary(value, out var evidence))
        {
            lines.Add($"evidence: {evidence}");
        }

        AddArtifactIntakeSummary(lines, value);
        AddNextStepTitle(lines, value);
        AddRecommendedCommandWithFallback(lines, value, artifacts);
        AddArraySummary(lines, value, "recommended_next_steps");
        AddArraySummary(lines, value, "next_actions");
        AddArraySummary(lines, value, "suggested_commands");
        return lines;
    }

    private static IReadOnlyList<string> BuildRunSummaryPacketSummary(JsonElement value, ArtifactData artifacts)
    {
        var lines = new List<string>();
        AddScalar(lines, value, "status");
        AddScalar(lines, value, "packet_status");
        AddTriageSummary(lines, value);
        AddPrimaryFailureSummaryFromProperty(lines, value, "primary_failure");
        AddNextStepTitle(lines, value);
        AddRecommendedCommandWithFallback(lines, value, artifacts);
        AddTriageChecklistSummary(lines, value);
        AddRunSummaryEntryPointSummary(lines, value);
        return lines;
    }

    private static void AddTriageChecklistSummary(List<string> lines, JsonElement value)
    {
        if (!value.TryGetProperty("triage_checklist", out var checklist) ||
            checklist.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var items = checklist.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object).ToArray();
        lines.Add($"triage_checklist: {items.Length}");
        foreach (var item in items.Take(3))
        {
            var parts = new List<string>();
            AddPart(parts, item, "step");
            AddPart(parts, item, "action");
            AddPart(parts, item, "command");
            if (parts.Count > 0)
            {
                lines.Add("  - " + string.Join("; ", parts));
            }
        }
    }

    private static void AddRunSummaryEntryPointSummary(List<string> lines, JsonElement value)
    {
        var packetPath = TryGetString(value, "packet_path");
        var markdownPath = TryGetString(value, "run_summary_markdown_path");

        if (string.IsNullOrWhiteSpace(packetPath) &&
            value.TryGetProperty("entry_points", out var entryPoints) &&
            entryPoints.ValueKind == JsonValueKind.Object)
        {
            packetPath = TryGetString(entryPoints, "run_summary_json_path");
            markdownPath = TryGetString(entryPoints, "run_summary_markdown_path") ?? markdownPath;
        }

        if (!string.IsNullOrWhiteSpace(packetPath))
        {
            lines.Add($"packet: {packetPath}");
        }

        if (!string.IsNullOrWhiteSpace(markdownPath))
        {
            lines.Add($"markdown: {markdownPath}");
        }
    }

    private static IReadOnlyList<string> BuildScenarioRunBatchSummary(JsonElement value, ArtifactData artifacts)
    {
        var lines = new List<string>();
        AddScalar(lines, value, "status");

        var summaryParts = new List<string>();
        AddCountSummaryPart(summaryParts, value, "selected_count", "selected");
        AddCountSummaryPart(summaryParts, value, "passed_count", "passed");
        AddCountSummaryPart(summaryParts, value, "failed_count", "failed");
        AddCountSummaryPart(summaryParts, value, "sharded_out_count", "sharded out");
        if (summaryParts.Count > 0)
        {
            lines.Add("summary: " + string.Join("; ", summaryParts));
        }

        if (TryGetPrimaryFailedScenario(value, out var primaryFailure))
        {
            AddPrimaryFailureSummary(lines, primaryFailure, GetScenarioFailureDetailSource(primaryFailure));
            if (TryBuildScenarioEvidenceSummary(primaryFailure, out var evidence))
            {
                lines.Add($"evidence: {evidence}");
            }
        }

        AddRecommendedCommandWithFallback(lines, value, artifacts);
        AddArraySummary(lines, value, "artifact_commands");
        return lines;
    }

    private static IReadOnlyList<string> BuildScenarioRunFailureSummary(JsonElement value, ArtifactData artifacts)
    {
        var lines = new List<string>();
        AddScalar(lines, value, "status");
        AddScalar(lines, value, "scenario");
        AddFailedStepSummary(lines, value);
        if (TryBuildFailureArtifactsSummary(value, out var evidence))
        {
            lines.Add($"evidence: {evidence}");
        }

        AddRecommendedCommandWithFallback(lines, value, artifacts);
        return lines;
    }

    private static IReadOnlyList<string> BuildFailureBundleSummary(JsonElement value, ArtifactData artifacts)
    {
        var lines = new List<string>();

        var scope = TryGetString(value, "scope");
        var name = TryGetString(value, "name");
        if (!string.IsNullOrWhiteSpace(scope) || !string.IsNullOrWhiteSpace(name))
        {
            lines.Add($"scope: {string.Join(" ", new[] { scope, name }.Where(static item => !string.IsNullOrWhiteSpace(item)))}");
        }

        AddFailedStepSummary(lines, value);
        if (TryBuildFailureBundleArtifactSummary(value, out var evidence))
        {
            lines.Add($"evidence: {evidence}");
        }

        AddRecommendedCommandWithFallback(lines, value, artifacts);
        return lines;
    }

    private static void AddTriageSummary(List<string> lines, JsonElement value)
    {
        if (!TryGetInt32(value, "session_count", out var sessionCount) ||
            !TryGetInt32(value, "failure_count", out var failureCount))
        {
            return;
        }

        var failureLabel = failureCount == 1 ? "failure signal" : "failure signals";
        var sessionLabel = sessionCount == 1 ? "session" : "sessions";
        lines.Add($"triage: {failureCount} {failureLabel} across {sessionCount} {sessionLabel}");
    }

    private static void AddPrimaryFailureSummaryFromProperty(List<string> lines, JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var primaryFailure) || primaryFailure.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        AddPrimaryFailureSummary(lines, primaryFailure, primaryFailure);
    }

    private static void AddPrimaryFailureSummary(List<string> lines, JsonElement value, JsonElement detailSource)
    {
        var summary = BuildPrimaryFailureSummary(value, detailSource);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            lines.Add($"primary_failure: {summary}");
        }

        var sourceCommand = TryGetString(value, "source_command", "sourceCommand") ?? TryGetString(detailSource, "source_command", "sourceCommand");
        if (!string.IsNullOrWhiteSpace(sourceCommand))
        {
            lines.Add($"evidence: {sourceCommand}");
        }
    }

    private static string? BuildPrimaryFailureSummary(JsonElement value, JsonElement detailSource)
    {
        var scenario = TryGetString(value, "scenario") ?? TryGetString(detailSource, "scenario");
        var step = FormatFailureStep(detailSource);
        var message = TryGetNestedString(value, "error", "message")
            ?? TryGetNestedString(detailSource, "error", "message")
            ?? TryGetNestedString(detailSource, "failure_artifacts", "error_message")
            ?? TryGetString(value, "message")
            ?? TryGetString(detailSource, "message");
        var parts = new[] { scenario, step, message }
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Take(3)
            .ToArray();
        return parts.Length == 0 ? null : string.Join(" / ", parts);
    }

    private static void AddNextStepTitle(List<string> lines, JsonElement value)
    {
        if (value.TryGetProperty("recommended_next_action", out var nextAction) &&
            nextAction.ValueKind == JsonValueKind.Object &&
            TryGetScalarProperty(nextAction, "title", out var title))
        {
            lines.Add($"next_step: {FormatScalar(title)}");
            return;
        }

        if (value.TryGetProperty("recommended_next_steps", out var nextSteps) &&
            nextSteps.ValueKind == JsonValueKind.Array)
        {
            var first = nextSteps.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object &&
                TryGetScalarProperty(first, "title", out var firstTitle))
            {
                lines.Add($"next_step: {FormatScalar(firstTitle)}");
            }
        }
    }

    private static void AddCountSummaryPart(List<string> parts, JsonElement value, string propertyName, string label)
    {
        if (TryGetInt32(value, propertyName, out var count))
        {
            parts.Add($"{count} {label}");
        }
    }

    private static void AddScalar(List<string> lines, JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        lines.Add($"{propertyName}: {FormatScalar(property)}");
    }

    private static void AddMultilineScalar(List<string> lines, JsonElement value, string propertyName)
    {
        var scalar = TryGetString(value, propertyName);
        if (string.IsNullOrWhiteSpace(scalar))
        {
            return;
        }

        foreach (var scalarLine in scalar.Split(["\r\n", "\n"], StringSplitOptions.None).Where(static line => !string.IsNullOrWhiteSpace(line)))
        {
            lines.Add($"{propertyName}: {scalarLine.Trim()}");
        }
    }

    private static void AddArraySummary(List<string> lines, JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var items = property.EnumerateArray().ToArray();
        lines.Add($"{propertyName}: {items.Length}");
        foreach (var itemSummary in items
            .Take(MaxArrayItems)
            .Select(static item => SummarizeArrayItem(item))
            .Where(static itemSummary => !string.IsNullOrWhiteSpace(itemSummary)))
        {
            lines.Add($"  - {itemSummary}");
        }
    }

    private static void AddRecommendedCommand(List<string> lines, JsonElement value)
    {
        if (TryGetRecommendedCommand(value, includeGenericCommandArrays: true, out var command))
        {
            lines.Add($"next: {command}");
        }
    }

    private static void AddRecommendedCommandWithFallback(List<string> lines, JsonElement value, ArtifactData artifacts)
    {
        if (TryGetRecommendedCommand(value, includeGenericCommandArrays: false, out var command))
        {
            lines.Add($"next: {command}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(artifacts.ArtifactRoot))
        {
            lines.Add($"next: luotsi replay packet --artifacts {Quote(artifacts.ArtifactRoot)}");
            return;
        }

        if (TryGetGenericCommand(value, out command))
        {
            lines.Add($"next: {command}");
        }
    }

    private static void AddRecommendedCommandWithGenericFallback(List<string> lines, JsonElement value, ArtifactData artifacts)
    {
        if (TryGetRecommendedCommand(value, includeGenericCommandArrays: false, out var command))
        {
            lines.Add($"next: {command}");
            return;
        }

        if (HasGenericCommand(value) && !string.IsNullOrWhiteSpace(artifacts.ArtifactRoot))
        {
            lines.Add($"next: luotsi replay packet --artifacts {Quote(artifacts.ArtifactRoot)}");
            return;
        }

        if (TryGetGenericCommand(value, out command))
        {
            lines.Add($"next: {command}");
        }
    }

    private static string? SummarizeArrayItem(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return FormatScalar(item);
        }

        var parts = new List<string>();
        AddPart(parts, item, "serial");
        AddPart(parts, item, "kind");
        AddPart(parts, item, "status");
        AddPart(parts, item, "model");
        AddPart(parts, item, "name");
        AddPart(parts, item, "title");
        AddSummaryPart(parts, item);
        AddPart(parts, item, "command");
        AddPart(parts, item, "path");
        AddPart(parts, item, "file");
        AddPart(parts, item, "package");
        AddPart(parts, item, "permission");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static void AddSummaryPart(List<string> parts, JsonElement item)
    {
        if (TryGetScalarProperty(item, "summary", out var summary))
        {
            parts.Add($"summary={FormatScalar(summary)}");
            return;
        }

        if (TryGetScalarProperty(item, "description", out var description))
        {
            parts.Add($"summary={FormatScalar(description)}");
            return;
        }

        if (TryGetScalarProperty(item, "reason", out var reason))
        {
            parts.Add($"summary={FormatScalar(reason)}");
            return;
        }

        if (TryGetScalarProperty(item, "purpose", out var purpose))
        {
            parts.Add($"summary={FormatScalar(purpose)}");
        }
    }

    private static void AddArtifactIntakeSummary(List<string> lines, JsonElement value)
    {
        if (!value.TryGetProperty("artifact_intake_summary", out var summary) ||
            summary.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var parts = new List<string>();
        AddIntakePart(parts, summary, "status");
        AddIntakePart(parts, summary, "share_safety");
        AddIntakePart(parts, summary, "sha_verified");
        AddIntakePart(parts, summary, "package");
        if (parts.Count > 0)
        {
            lines.Add("intake: " + string.Join("; ", parts));
        }
    }

    private static void AddIntakePart(List<string> parts, JsonElement summary, string propertyName)
    {
        if (TryGetScalarProperty(summary, propertyName, out var property))
        {
            parts.Add($"{propertyName}={FormatScalar(property)}");
        }
    }

    private static void AddPart(List<string> parts, JsonElement item, string propertyName)
    {
        if (TryGetScalarProperty(item, propertyName, out var property))
        {
            parts.Add($"{propertyName}={FormatScalar(property)}");
        }
    }

    private static bool TryGetScalarProperty(JsonElement item, string propertyName, out JsonElement property)
    {
        if (item.TryGetProperty(propertyName, out property) &&
            property.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return true;
        }

        property = default;
        return false;
    }

    private static bool TryGetRecommendedCommand(JsonElement value, bool includeGenericCommandArrays, out string? command)
    {
        command = TryGetString(value, "recommended_next_action_command", "recommendedNextActionCommand");
        if (!string.IsNullOrWhiteSpace(command))
        {
            return true;
        }

        if (value.TryGetProperty("recommended_next_action", out var nextAction) &&
            nextAction.ValueKind == JsonValueKind.Object &&
            nextAction.TryGetProperty("command", out var nextCommand) &&
            nextCommand.ValueKind == JsonValueKind.String)
        {
            command = nextCommand.GetString();
            return !string.IsNullOrWhiteSpace(command);
        }

        foreach (var propertyName in new[] { "recommended_next_steps", "next_actions", "suggested_commands" })
        {
            if (TryGetFirstCommandFromArray(value, propertyName, out command))
            {
                return true;
            }
        }

        if (includeGenericCommandArrays && TryGetGenericCommand(value, out command))
        {
            return true;
        }

        command = null;
        return false;
    }

    private static bool TryGetGenericCommand(JsonElement value, out string? command)
    {
        foreach (var propertyName in new[] { "artifact_commands", "recommended_commands", "commands" })
        {
            if (TryGetFirstCommandFromArray(value, propertyName, out command))
            {
                return true;
            }
        }

        command = null;
        return false;
    }

    private static bool HasGenericCommand(JsonElement value) =>
        TryGetGenericCommand(value, out _);

    private static bool TryGetFirstCommandFromArray(JsonElement value, string propertyName, out string? command)
    {
        if (!value.TryGetProperty(propertyName, out var commands) || commands.ValueKind != JsonValueKind.Array)
        {
            command = null;
            return false;
        }

        command = commands.EnumerateArray()
            .Select(static item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("command", out var value) ? value.GetString() : null)
            .FirstOrDefault(static item => !string.IsNullOrWhiteSpace(item));
        return !string.IsNullOrWhiteSpace(command);
    }

    private static bool TryGetPrimaryFailedScenario(JsonElement value, out JsonElement primaryFailure)
    {
        primaryFailure = default;
        if (!value.TryGetProperty("scenarios", out var scenarios) || scenarios.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        primaryFailure = scenarios.EnumerateArray()
            .FirstOrDefault(static item =>
                item.ValueKind == JsonValueKind.Object &&
                TryGetScalarProperty(item, "status", out var status) &&
                string.Equals(status.GetString(), "failed", StringComparison.OrdinalIgnoreCase));
        return primaryFailure.ValueKind == JsonValueKind.Object;
    }

    private static JsonElement GetScenarioFailureDetailSource(JsonElement scenario)
    {
        if (scenario.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            return data;
        }

        return scenario;
    }

    private static bool TryBuildScenarioEvidenceSummary(JsonElement scenario, out string? evidence)
    {
        var detailSource = GetScenarioFailureDetailSource(scenario);
        return TryBuildFailureArtifactsSummary(detailSource, out evidence);
    }

    private static bool TryBuildFailureArtifactsSummary(JsonElement value, out string? evidence)
    {
        if (!value.TryGetProperty("failure_artifacts", out var failureArtifacts) || failureArtifacts.ValueKind != JsonValueKind.Object)
        {
            evidence = null;
            return false;
        }

        return TryBuildArtifactArraySummary(failureArtifacts, "artifacts", out evidence);
    }

    private static bool TryBuildFailureBundleArtifactSummary(JsonElement value, out string? evidence) =>
        TryBuildArtifactArraySummary(value, "artifacts", out evidence);

    private static bool TryBuildArtifactArraySummary(JsonElement value, string propertyName, out string? evidence)
    {
        if (!value.TryGetProperty(propertyName, out var artifacts) || artifacts.ValueKind != JsonValueKind.Array)
        {
            evidence = null;
            return false;
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in artifacts.EnumerateArray())
        {
            if (!TryGetScalarProperty(artifact, "kind", out var kind))
            {
                continue;
            }

            var normalized = NormalizeArtifactKind(kind.GetString());
            if (normalized is null)
            {
                continue;
            }

            counts[normalized] = counts.GetValueOrDefault(normalized) + 1;
        }

        evidence = FormatEvidenceCounts(counts);
        return !string.IsNullOrWhiteSpace(evidence);
    }

    private static bool TryBuildArtifactCountsSummary(JsonElement value, out string? evidence)
    {
        if (!value.TryGetProperty("artifact_counts", out var counts) || counts.ValueKind != JsonValueKind.Object)
        {
            evidence = null;
            return false;
        }

        var evidenceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        AddEvidenceCount(evidenceCounts, counts, "screenshots", "screenshots");
        AddEvidenceCount(evidenceCounts, counts, "videos", "videos");
        AddEvidenceCount(evidenceCounts, counts, "logs", "logs");
        AddEvidenceCount(evidenceCounts, counts, "hierarchies", "hierarchies");
        AddEvidenceCount(evidenceCounts, counts, "screen_states", "screen_states");
        AddEvidenceCount(evidenceCounts, counts, "reports", "reports");
        AddEvidenceCount(evidenceCounts, counts, "timelines", "timelines");
        evidence = FormatEvidenceCounts(evidenceCounts);
        return !string.IsNullOrWhiteSpace(evidence);
    }

    private static void AddEvidenceCount(Dictionary<string, int> counts, JsonElement value, string propertyName, string label)
    {
        if (TryGetInt32(value, propertyName, out var count) && count > 0)
        {
            counts[label] = count;
        }
    }

    private static string? FormatEvidenceCounts(IReadOnlyDictionary<string, int> counts)
    {
        var orderedKeys = new[]
        {
            "screenshots",
            "videos",
            "logs",
            "hierarchies",
            "screen_states",
            "reports",
            "timelines",
            "logcat",
            "metadata"
        };
        var parts = orderedKeys
            .Where(counts.ContainsKey)
            .Select(key => $"{key}={counts[key]}")
            .ToArray();
        return parts.Length == 0 ? null : string.Join("; ", parts);
    }

    private static string? NormalizeArtifactKind(string? kind) =>
        kind?.Trim().ToLowerInvariant() switch
        {
            "screenshot" => "screenshots",
            "video" => "videos",
            "log" => "logs",
            "logcat" => "logcat",
            "hierarchy" => "hierarchies",
            "screen_state" => "screen_states",
            "metadata" => "metadata",
            _ => null
        };

    private static void AddFailedStepSummary(List<string> lines, JsonElement value)
    {
        var summary = FormatFailureStep(value);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            lines.Add($"failed_step: {summary}");
        }
    }

    private static string? FormatFailureStep(JsonElement value)
    {
        if (value.TryGetProperty("failed_step", out var failedStep) && failedStep.ValueKind == JsonValueKind.Object)
        {
            return FormatFailureStepParts(failedStep);
        }

        return FormatFailureStepParts(value);
    }

    private static string? FormatFailureStepParts(JsonElement value)
    {
        var step = TryGetString(value, "step_name") ?? TryGetString(value, "name") ?? TryGetString(value, "step");
        var action = TryGetString(value, "action");
        if (string.IsNullOrWhiteSpace(step) && string.IsNullOrWhiteSpace(action))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            return step;
        }

        return string.IsNullOrWhiteSpace(step) ? action : $"{step} ({action})";
    }

    private static string? TryGetString(JsonElement value, string propertyName) =>
        TryGetScalarProperty(value, propertyName, out var property) ? property.GetString() : null;

    private static string? TryGetString(JsonElement value, string firstPropertyName, string secondPropertyName) =>
        TryGetString(value, firstPropertyName) ?? TryGetString(value, secondPropertyName);

    private static string? TryGetNestedString(JsonElement value, string objectPropertyName, string nestedPropertyName)
    {
        if (!value.TryGetProperty(objectPropertyName, out var nested) || nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetString(nested, nestedPropertyName);
    }

    private static bool TryGetInt32(JsonElement value, string propertyName, out int count)
    {
        if (TryGetScalarProperty(value, propertyName, out var property) && property.TryGetInt32(out count))
        {
            return true;
        }

        count = 0;
        return false;
    }

    private static bool LooksLikeScenarioRunBatch(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("scenarios", out var scenarios) &&
        scenarios.ValueKind == JsonValueKind.Array &&
        value.TryGetProperty("selected_count", out _);

    private static bool LooksLikeScenarioRunFailure(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("failure_artifacts", out var failureArtifacts) &&
        failureArtifacts.ValueKind == JsonValueKind.Object;

    private static bool IsSchema(JsonElement value, string schema) =>
        TryGetScalarProperty(value, "schema", out var schemaProperty) &&
        string.Equals(schemaProperty.GetString(), schema, StringComparison.Ordinal);

    private static string FormatScalar(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Null => "null",
            JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText()
        };

    private static string FormatCommand(string? command) =>
        string.IsNullOrWhiteSpace(command) ? "command" : command;

    private static string Quote(string value) =>
        value.Any(static ch => char.IsWhiteSpace(ch) || ch == '"')
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
}
