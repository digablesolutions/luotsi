using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Artifacts;

internal sealed class ArtifactSummaryBuilder(string root, IFileSystem fileSystem)
{
    private const int MaxJsonlSummaryBytes = 256 * 1024;
    private const int MaxJsonlSummaryLines = 500;

    private readonly string _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<string?> TryBuildAsync(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            return await TryBuildJsonlSummaryAsync(path).ConfigureAwait(false);
        }

        if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
        {
            return await TryBuildJsonReportSummaryAsync(path).ConfigureAwait(false);
        }

        return string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase)
            ? await TryBuildXmlReportSummaryAsync(path).ConfigureAwait(false)
            : null;
    }

    private async Task<string?> TryBuildXmlReportSummaryAsync(string path)
    {
        if (!Path.GetFileName(path).Contains("junit", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var stream = _fileSystem.OpenRead(Path.Join(_root, path));
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None).ConfigureAwait(false);
            var root = document.Root;
            if (root is null)
            {
                return null;
            }

            var parts = new List<string> { "format=junit" };
            AddXmlAttribute(parts, root, "tests");
            AddXmlAttribute(parts, root, "failures");
            AddXmlAttribute(parts, root, "errors");
            AddXmlAttribute(parts, root, "skipped");
            AddXmlAttribute(parts, root, "time", "duration_sec");
            if (root.Name.LocalName.Equals("testsuite", StringComparison.OrdinalIgnoreCase))
            {
                AddXmlAttribute(parts, root, "name", "suite");
            }
            else
            {
                var firstSuite = root.Elements()
                    .FirstOrDefault(static element => element.Name.LocalName.Equals("testsuite", StringComparison.OrdinalIgnoreCase));
                if (firstSuite is not null)
                {
                    AddXmlAttribute(parts, firstSuite, "name", "suite");
                }
            }

            return parts.Count == 1 ? null : string.Join(" | ", parts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private async Task<string?> TryBuildJsonReportSummaryAsync(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(await _fileSystem.ReadAllTextAsync(Path.Join(_root, path)).ConfigureAwait(false));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.String)
            {
                return Path.GetFileName(path).Equals("artifact-intake-summary.json", StringComparison.OrdinalIgnoreCase)
                    ? BuildArtifactIntakeSummary(root)
                    : null;
            }

            var schemaName = schema.GetString();
            if (string.Equals(schemaName, ResultSchemas.ArtifactIntake, StringComparison.Ordinal))
            {
                return BuildArtifactIntakeSummary(root);
            }

            if (string.Equals(schemaName, ResultSchemas.ReplayCapsule, StringComparison.Ordinal))
            {
                return BuildReplayCapsuleSummary(root);
            }

            if (string.Equals(schemaName, ResultSchemas.ReplayOpen, StringComparison.Ordinal))
            {
                return BuildReplayOpenSummary(root);
            }

            if (string.Equals(schemaName, ResultSchemas.ScenarioDraft, StringComparison.Ordinal))
            {
                return BuildScenarioDraftSummary(root);
            }

            if (string.Equals(schemaName, ResultSchemas.ReplayScrub, StringComparison.Ordinal))
            {
                return BuildReplayScrubSummary(root);
            }

            if (string.Equals(schemaName, "luotsi-scenario-run-report.v1", StringComparison.Ordinal))
            {
                return BuildScenarioRunReportSummary(root);
            }

            return string.Equals(schemaName, ResultSchemas.FailureCapsule, StringComparison.Ordinal)
                ? BuildFailureCapsuleSummary(root)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? BuildScenarioRunReportSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "status");
        AddJsonProperty(parts, root, "total");
        AddJsonProperty(parts, root, "passed");
        AddJsonProperty(parts, root, "failed");
        AddJsonProperty(parts, root, "skipped");
        AddJsonProperty(parts, root, "durationMs", "duration_ms");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildArtifactIntakeSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "status");
        AddJsonProperty(parts, root, "entryCount", "entries");
        AddJsonProperty(parts, root, "shareSafety", "share_safety");
        AddJsonProperty(parts, root, "labSafeRequired", "lab_safe_required");
        AddJsonProperty(parts, root, "sha256");
        if (root.TryGetProperty("verification", out var verification) && verification.ValueKind == JsonValueKind.Object)
        {
            AddJsonProperty(parts, verification, "verified", "sha_verified");
        }

        AddArrayCount(parts, root, "recommendedCommands", "recommended_commands");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildReplayOpenSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "sessionCount", "session_count");
        AddJsonProperty(parts, root, "failureCount", "failure_count");
        AddJsonProperty(parts, root, "opened");
        AddReplayOpenNextActionSummary(parts, root);
        AddReplayOpenPrimaryFailureSummary(parts, root);
        AddArrayCount(parts, root, "commands");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static void AddReplayOpenNextActionSummary(List<string> parts, JsonElement root)
    {
        if (!TryGetProperty(root, "recommendedNextAction", "recommended_next_action", out var nextAction) ||
            nextAction.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        AddJsonProperty(parts, nextAction, "kind", "recommended_action");
        AddJsonProperty(parts, nextAction, "title", "recommended_title");
        AddJsonProperty(parts, nextAction, "command", "recommended_command");
    }

    private static void AddReplayOpenPrimaryFailureSummary(List<string> parts, JsonElement root)
    {
        if (!TryGetProperty(root, "primaryFailure", "primary_failure", out var primaryFailure) ||
            primaryFailure.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var summary = new[]
            {
                TryGetString(primaryFailure, "scenario"),
                TryGetString(primaryFailure, "step"),
                TryGetString(primaryFailure, "action"),
                TryGetString(primaryFailure, "message")
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Take(3)
            .ToArray();
        if (summary.Length > 0)
        {
            parts.Add("primary_failure=" + string.Join(" / ", summary));
        }
    }

    private static string? BuildReplayCapsuleSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "sessionCount", "session_count");
        AddJsonProperty(parts, root, "failureCount", "failure_count");
        AddJsonProperty(parts, root, "scenarioDraftAvailable", "scenario_draft_available");
        AddJsonProperty(parts, root, "scenarioDraftReason", "scenario_draft_reason");
        AddReplayCapsulePrimaryFailureSummary(parts, root);
        AddReplayCapsuleNextStepSummary(parts, root);
        AddArrayCount(parts, root, "artifactManifest", "artifact_manifest");
        AddArrayCount(parts, root, "failureTimeline", "failure_timeline");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static void AddReplayCapsulePrimaryFailureSummary(List<string> parts, JsonElement root)
    {
        if (!TryGetProperty(root, "primaryFailure", "primary_failure", out var primaryFailure) ||
            primaryFailure.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var summary = new[]
            {
                TryGetString(primaryFailure, "scenario"),
                TryGetString(primaryFailure, "step"),
                TryGetString(primaryFailure, "action"),
                TryGetString(primaryFailure, "message")
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Take(3)
            .ToArray();
        if (summary.Length > 0)
        {
            parts.Add("primary_failure=" + string.Join(" / ", summary));
        }
    }

    private static void AddReplayCapsuleNextStepSummary(List<string> parts, JsonElement root)
    {
        if (!TryGetProperty(root, "recommendedNextSteps", "recommended_next_steps", out var nextSteps) ||
            nextSteps.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var firstStep = nextSteps.EnumerateArray().FirstOrDefault();
        if (firstStep.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var title = TryGetString(firstStep, "title");
        var command = TryGetString(firstStep, "command");
        if (!string.IsNullOrWhiteSpace(title))
        {
            parts.Add("next_step=" + title);
        }

        if (!string.IsNullOrWhiteSpace(command))
        {
            parts.Add("next_command=" + command);
        }
    }

    private static string? BuildReplayScrubSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "eventCount", "event_count");
        AddJsonProperty(parts, root, "focusIndex", "focus_index");
        AddJsonProperty(parts, root, "markdownPath", "markdown_path");
        if (root.TryGetProperty("focusEvent", out var focusEvent) ||
            root.TryGetProperty("focus_event", out focusEvent))
        {
            AddJsonProperty(parts, focusEvent, "type", "focus_type");
            AddJsonProperty(parts, focusEvent, "detail", "focus_detail");
        }

        AddArrayCount(parts, root, "commands");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildScenarioDraftSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "confidence");
        AddArrayCount(parts, root, "sourceSummaries", "source_summaries");
        if (root.TryGetProperty("scenario", out var scenario) &&
            scenario.ValueKind == JsonValueKind.Object &&
            scenario.TryGetProperty("steps", out var steps) &&
            steps.ValueKind == JsonValueKind.Array)
        {
            parts.Add($"steps={steps.GetArrayLength()}");
        }

        if (root.TryGetProperty("validation", out var validation) && validation.ValueKind == JsonValueKind.Object)
        {
            AddJsonProperty(parts, validation, "status", "validation_status");
        }

        if (root.TryGetProperty("packageProvenance", out var packageProvenance) && packageProvenance.ValueKind == JsonValueKind.Object)
        {
            AddJsonProperty(parts, packageProvenance, "package");
        }

        if (root.TryGetProperty("deviceProvenance", out var deviceProvenance) && deviceProvenance.ValueKind == JsonValueKind.Object)
        {
            AddJsonProperty(parts, deviceProvenance, "serial", "device");
        }

        if (root.TryGetProperty("runHandoff", out var runHandoff) && runHandoff.ValueKind == JsonValueKind.Object)
        {
            AddJsonProperty(parts, runHandoff, "status", "run_handoff");
        }

        AddArrayCount(parts, root, "warnings");
        AddArrayCount(parts, root, "reviewItems", "review_items");
        AddArrayCount(parts, root, "nextActions", "next_actions");
        AddArrayCount(parts, root, "normalizations");
        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildFailureCapsuleSummary(JsonElement root)
    {
        var parts = new List<string>();
        AddJsonProperty(parts, root, "status");

        if (root.TryGetProperty("scenarios", out var scenarios) && scenarios.ValueKind == JsonValueKind.Array)
        {
            var scenarioItems = scenarios.EnumerateArray().ToArray();
            parts.Add($"scenarios={scenarioItems.Length}");

            var failedScenarioNames = scenarioItems
                .Select(static scenario => TryGetString(scenario, "scenario"))
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Take(2)
                .Cast<string>()
                .ToArray();
            if (failedScenarioNames.Length > 0)
            {
                parts.Add($"failed_scenarios={string.Join(", ", failedScenarioNames)}");
            }

            var failedSteps = scenarioItems
                .Select(static scenario => TryGetObjectString(scenario, "failedStep", "name"))
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Take(2)
                .Cast<string>()
                .ToArray();
            if (failedSteps.Length > 0)
            {
                parts.Add($"failed_steps={string.Join(", ", failedSteps)}");
            }
        }

        AddArrayCount(parts, root, "screenshots");
        AddArrayCount(parts, root, "logcat");
        AddArrayCount(parts, root, "hierarchies");
        AddArrayCount(parts, root, "screenStates", "screen_states");
        AddArrayCount(parts, root, "failureBundles", "failure_bundles");

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private async Task<string?> TryBuildJsonlSummaryAsync(string path)
    {
        try
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var terminalStatuses = new List<string>();
            var (sampledLines, truncated) = await ReadJsonlTailLinesAsync(path).ConfigureAwait(false);
            foreach (var (type, status) in sampledLines.Select(ParseJsonlEvent))
            {
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                counts[type] = counts.GetValueOrDefault(type) + 1;
                if (status is not null)
                {
                    terminalStatuses.Add(status);
                }
            }

            if (counts.Count == 0)
            {
                return null;
            }

            var parts = truncated
                ? new List<string> { $"events_sampled={counts.Values.Sum()}" }
                : new List<string> { $"events={counts.Values.Sum()}" };
            if (terminalStatuses.Count > 0)
            {
                parts.Add($"terminal={string.Join(",", terminalStatuses)}");
            }

            foreach (var (type, count) in counts.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase).Take(5))
            {
                parts.Add($"{type}={count}");
            }

            return string.Join(" | ", parts);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<(string[] Lines, bool Truncated)> ReadJsonlTailLinesAsync(string path)
    {
        using var stream = _fileSystem.OpenRead(Path.Join(_root, path));
        var truncatedByBytes = false;
        if (stream is {CanSeek: true, Length: > MaxJsonlSummaryBytes})
        {
            stream.Seek(-MaxJsonlSummaryBytes, SeekOrigin.End);
            truncatedByBytes = true;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (truncatedByBytes && lines.Length > 0)
        {
            lines = lines[1..];
        }

        if (lines.Length <= MaxJsonlSummaryLines)
        {
            return (lines, truncatedByBytes);
        }

        return (lines[^MaxJsonlSummaryLines..], true);
    }

    private static (string? Type, string? Status) ParseJsonlEvent(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeProperty))
        {
            return (null, null);
        }

        var type = typeProperty.GetString();
        var status = string.Equals(type, "scenario_run_ended", StringComparison.OrdinalIgnoreCase) &&
            root.TryGetProperty("status", out var statusProperty)
                ? statusProperty.GetString() ?? "unknown"
                : null;
        return (type, status);
    }

    private static void AddJsonProperty(List<string> parts, JsonElement root, string name, string? label = null)
    {
        if (!root.TryGetProperty(name, out var property))
        {
            return;
        }

        var value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label ?? ToSnakeCase(name)}={value}");
        }
    }

    private static void AddXmlAttribute(List<string> parts, XElement element, string name, string? label = null)
    {
        var value = element.Attribute(name)?.Value;
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label ?? ToSnakeCase(name)}={value}");
        }
    }

    private static void AddArrayCount(List<string> parts, JsonElement root, string name, string? label = null)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        parts.Add($"{label ?? ToSnakeCase(name)}={property.GetArrayLength()}");
    }

    private static string? TryGetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static bool TryGetProperty(JsonElement root, string name, string alternateName, out JsonElement property) =>
        root.TryGetProperty(name, out property) || root.TryGetProperty(alternateName, out property);

    private static string? TryGetObjectString(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetString(property, propertyName);
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        foreach (var ch in value)
        {
            if (char.IsUpper(ch) && builder.Length > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }
}
