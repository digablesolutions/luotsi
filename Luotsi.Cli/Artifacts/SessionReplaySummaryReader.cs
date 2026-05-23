using System.Text;
using System.Text.Json;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Artifacts;

internal sealed class SessionReplaySummaryReader(string root, IFileSystem fileSystem)
{
    private readonly string _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public IReadOnlyList<SessionReplaySummary> ReadSummaries(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        return files
            .Select(TryRead)
            .Where(static summary => summary is not null)
            .Select(static summary => summary!)
            .OrderByDescending(static summary => summary.StartedAt)
            .ToArray();
    }

    public SessionReplaySummary? TryRead(string metadataPath)
    {
        if (!string.Equals(Path.GetExtension(metadataPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var stream = _fileSystem.OpenRead(Path.Join(_root, metadataPath));
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("schema", out var schemaProperty) ||
                !string.Equals(schemaProperty.GetString(), ResultSchemas.SessionReplay, StringComparison.Ordinal))
            {
                return null;
            }

            var sessionKind = GetRequiredString(root, "sessionKind");
            var sessionId = GetRequiredString(root, "sessionId");
            var startedAt = GetRequiredDateTimeOffset(root, "startedAt");
            var endedAt = GetRequiredDateTimeOffset(root, "endedAt");
            var reason = GetRequiredString(root, "reason");
            var exitCode = GetRequiredInt32(root, "exitCode");
            var timelinePath = ResolveSiblingPath(metadataPath, GetRequiredString(root, "timelineFileName"));
            var eventCount = GetRequiredInt32(root, "eventCount");
            var target = GetOptionalString(root, "target");
            var eventTypes = GetStringArray(root, "eventTypes");
            var (failureCapsulePath, failureCapsule) = TryReadFailureCapsule(metadataPath);
            var (hasTimeline, highlights, sawFailureSignal) = ReadTimelineHighlights(timelinePath, sessionKind, failureCapsule);
            return new SessionReplaySummary(
                metadataPath,
                timelinePath,
                failureCapsulePath,
                failureCapsule,
                sessionKind,
                sessionId,
                startedAt,
                endedAt,
                reason,
                exitCode,
                target,
                eventCount,
                eventTypes,
                hasTimeline,
                sawFailureSignal || exitCode != 0 || string.Equals(reason, "error", StringComparison.OrdinalIgnoreCase),
                highlights);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            return null;
        }
    }

    private (bool HasTimeline, IReadOnlyList<SessionReplayTimelineEntry> Highlights, bool SawFailureSignal) ReadTimelineHighlights(
        string timelinePath,
        string sessionKind,
        FailureCapsuleManifest? failureCapsule)
    {
        var fullPath = Path.Join(_root, timelinePath);
        if (!_fileSystem.FileExists(fullPath))
        {
            return (false, [], false);
        }

        using var stream = _fileSystem.OpenRead(fullPath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096);

        SessionReplayTimelineEntry? firstEntry = null;
        var failureEntries = new List<SessionReplayTimelineEntry>();
        var contextEntries = new List<SessionReplayTimelineEntry>();
        var tailEntries = new Queue<SessionReplayTimelineEntry>();
        SessionReplayTimelineEntry? latestStatsEntry = null;
        var sawFailureSignal = false;
        var sequence = 0;

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = TryParseTimelineEntry(line, sequence++, failureCapsule);
            if (entry is null)
            {
                continue;
            }

            firstEntry ??= entry;
            if (entry.IsFailureRelevant)
            {
                sawFailureSignal = true;
                failureEntries.Add(entry);
            }

            if (IsContextRelevant(entry.Type))
            {
                contextEntries.Add(entry);
            }

            if (string.Equals(entry.Type, SessionEventTypes.View.Stats, StringComparison.Ordinal))
            {
                latestStatsEntry = entry;
            }

            if (tailEntries.Count == 3)
            {
                tailEntries.Dequeue();
            }

            tailEntries.Enqueue(entry);
        }

        var highlights = new SortedDictionary<int, SessionReplayTimelineEntry>();
        if (firstEntry is not null)
        {
            highlights[firstEntry.Sequence] = firstEntry;
        }

        foreach (var entry in SelectFailureEntries(failureEntries))
        {
            highlights[entry.Sequence] = entry;
        }

        foreach (var entry in SelectContextEntries(contextEntries, sessionKind, failureCapsule))
        {
            highlights[entry.Sequence] = entry;
        }

        if (latestStatsEntry is not null)
        {
            highlights[latestStatsEntry.Sequence] = latestStatsEntry;
        }

        foreach (var entry in tailEntries)
        {
            highlights[entry.Sequence] = entry;
        }

        return (true, highlights.Values.ToArray(), sawFailureSignal);
    }

    private static IEnumerable<SessionReplayTimelineEntry> SelectFailureEntries(IReadOnlyList<SessionReplayTimelineEntry> entries)
    {
        if (entries.Count <= 4)
        {
            return entries;
        }

        return [entries[0], entries[1], entries[^2], entries[^1]];
    }

    private static IEnumerable<SessionReplayTimelineEntry> SelectContextEntries(
        IReadOnlyList<SessionReplayTimelineEntry> entries,
        string sessionKind,
        FailureCapsuleManifest? failureCapsule)
    {
        if (string.Equals(sessionKind, "run", StringComparison.OrdinalIgnoreCase))
        {
            return SelectRunContextEntries(entries, failureCapsule);
        }

        if (entries.Count <= 6)
        {
            return entries;
        }

        return [entries[0], entries[1], entries[2], entries[^3], entries[^2], entries[^1]];
    }

    private static IEnumerable<SessionReplayTimelineEntry> SelectRunContextEntries(
        IReadOnlyList<SessionReplayTimelineEntry> entries,
        FailureCapsuleManifest? failureCapsule)
    {
        if (failureCapsule?.Scenarios.Count > 0)
        {
            var matched = entries.Where(entry => IsRunContextEntry(entry, failureCapsule)).ToArray();
            if (matched.Length > 0)
            {
                return matched.Length <= 6
                    ? matched
                    : [matched[0], matched[1], matched[2], matched[^3], matched[^2], matched[^1]];
            }
        }

        if (entries.Count <= 6)
        {
            return entries;
        }

        return [entries[0], entries[1], entries[2], entries[^3], entries[^2], entries[^1]];
    }

    private static bool IsRunContextEntry(SessionReplayTimelineEntry entry, FailureCapsuleManifest failureCapsule)
    {
        if (string.Equals(entry.Type, "scenario_started", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Type, "scenario_ended", StringComparison.OrdinalIgnoreCase))
        {
            return MatchesFailureScenario(entry.ScenarioId, entry.Scenario, failureCapsule);
        }

        return string.Equals(entry.Type, "scenario_step_started", StringComparison.OrdinalIgnoreCase) &&
            MatchesFailureStep(entry.ScenarioId, entry.Scenario, entry.StepIndex, failureCapsule);
    }

    private static SessionReplayTimelineEntry? TryParseTimelineEntry(string line, int sequence, FailureCapsuleManifest? failureCapsule)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var typeProperty) ||
            typeProperty.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var type = typeProperty.GetString();
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        return new SessionReplayTimelineEntry(
            sequence,
            TryGetTimestamp(root),
            type,
            BuildDetail(root, type, failureCapsule),
            IsFailureRelevant(root, type),
            GetOptionalString(root, "scenario_id"),
            GetOptionalString(root, "scenario"),
            GetOptionalInt32(root, "step_index"));
    }

    private static DateTimeOffset? TryGetTimestamp(JsonElement root)
    {
        foreach (var propertyName in new[] { "received_at", "occurred_at", "observed_at", "captured_at", "started_at", "ended_at" })
        {
            if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (DateTimeOffset.TryParse(property.GetString(), out var timestamp))
            {
                return timestamp;
            }
        }

        return null;
    }

    private static bool IsFailureRelevant(JsonElement root, string type)
    {
        if (type.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("diagnostic", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("fallback", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (root.TryGetProperty("ok", out var okProperty) && okProperty.ValueKind == JsonValueKind.False)
        {
            return true;
        }

        if (root.TryGetProperty("reason", out var reasonProperty) &&
            reasonProperty.ValueKind == JsonValueKind.String &&
            string.Equals(reasonProperty.GetString(), "error", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return root.TryGetProperty("error", out _);
    }

    private static bool IsContextRelevant(string type) =>
        string.Equals(type, SessionEventTypes.View.ReconnectRequested, StringComparison.Ordinal)
        || string.Equals(type, SessionEventTypes.View.Reconnected, StringComparison.Ordinal)
        || string.Equals(type, SessionEventTypes.View.ShareStarted, StringComparison.Ordinal)
        || string.Equals(type, SessionEventTypes.View.ShareClientConnected, StringComparison.Ordinal)
        || string.Equals(type, SessionEventTypes.View.ShareClientDisconnected, StringComparison.Ordinal)
        || string.Equals(type, "scenario_started", StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "scenario_step_started", StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "scenario_ended", StringComparison.OrdinalIgnoreCase);

    private static string BuildDetail(JsonElement root, string type, FailureCapsuleManifest? failureCapsule)
    {
        if (string.Equals(type, SessionEventTypes.View.ReconnectRequested, StringComparison.Ordinal))
        {
            return BuildReconnectRequestedDetail(root);
        }

        if (string.Equals(type, SessionEventTypes.View.Reconnected, StringComparison.Ordinal))
        {
            return BuildReconnectedDetail(root);
        }

        if (string.Equals(type, SessionEventTypes.View.ShareStarted, StringComparison.Ordinal))
        {
            return BuildShareStartedDetail(root);
        }

        if (string.Equals(type, SessionEventTypes.View.ShareClientConnected, StringComparison.Ordinal) ||
            string.Equals(type, SessionEventTypes.View.ShareClientDisconnected, StringComparison.Ordinal))
        {
            return BuildShareClientDetail(root);
        }

        if (string.Equals(type, SessionEventTypes.View.Stats, StringComparison.Ordinal))
        {
            return BuildStatsDetail(root);
        }

        if (type.StartsWith("scenario_", StringComparison.OrdinalIgnoreCase))
        {
            return BuildScenarioDetail(root, type, failureCapsule);
        }

        var parts = new List<string>();
        AddStringProperty(parts, root, "scenario");
        AddStringProperty(parts, root, "step");
        AddStringProperty(parts, root, "action");
        AddStringProperty(parts, root, "status");
        AddStringProperty(parts, root, "command");
        AddStringProperty(parts, root, "reason");
        AddStringProperty(parts, root, "category");
        AddStringProperty(parts, root, "message");
        AddBoolProperty(parts, root, "ok", includeWhenTrue: false);
        AddErrorProperty(parts, root);
        return string.Join(" | ", parts);
    }

    private static string BuildScenarioDetail(JsonElement root, string type, FailureCapsuleManifest? failureCapsule)
    {
        var parts = new List<string>();
        AddStringProperty(parts, root, "scenario");
        AddNumberProperty(parts, root, "step_index");
        AddStringProperty(parts, root, "phase");
        AddStringProperty(parts, root, "step");
        AddStringProperty(parts, root, "action");
        AddStringProperty(parts, root, "status");
        AddNumberProperty(parts, root, "passed_count");
        AddNumberProperty(parts, root, "failed_count");
        AddErrorProperty(parts, root);
        AddFailureCapsuleProperties(parts, root, type, failureCapsule);
        return string.Join(" | ", parts);
    }

    private static string BuildReconnectRequestedDetail(JsonElement root)
    {
        var parts = new List<string>();
        AddStringProperty(parts, root, "device");
        AddStringProperty(parts, root, "source");
        AddStringProperty(parts, root, "reason");
        return string.Join(" | ", parts);
    }

    private static string BuildReconnectedDetail(JsonElement root)
    {
        var parts = new List<string>();
        AddStringProperty(parts, root, "device");
        AddStringProperty(parts, root, "capture_backend");
        AddStringProperty(parts, root, "requested_capture_backend");
        if (root.TryGetProperty("connection", out var connection) && connection.ValueKind == JsonValueKind.Object)
        {
            var connectionSummary = BuildConnectionSummary(connection);
            if (!string.IsNullOrWhiteSpace(connectionSummary))
            {
                parts.Add(connectionSummary);
            }
        }

        return string.Join(" | ", parts);
    }

    private static string BuildShareStartedDetail(JsonElement root)
    {
        var parts = new List<string>();
        AddStringProperty(parts, root, "endpoint");
        AddNumberProperty(parts, root, "observer_count");
        return string.Join(" | ", parts);
    }

    private static string BuildShareClientDetail(JsonElement root)
    {
        var parts = new List<string>();
        AddStringProperty(parts, root, "endpoint");
        AddStringProperty(parts, root, "remote_endpoint");
        AddNumberProperty(parts, root, "observer_count");
        AddStringProperty(parts, root, "reason");
        return string.Join(" | ", parts);
    }

    private static string BuildStatsDetail(JsonElement root)
    {
        if (!root.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        AddNumberProperty(parts, stats, "decoded_frames");
        AddNumberProperty(parts, stats, "presented_frames");
        AddNumberProperty(parts, stats, "dropped_frames");
        AddNumberProperty(parts, stats, "decode_fps");
        AddNumberProperty(parts, stats, "present_fps");
        AddNumberProperty(parts, stats, "end_to_end_latency_ms");
        return string.Join(" | ", parts);
    }

    private static string BuildConnectionSummary(JsonElement connection)
    {
        var parts = new List<string>();
        AddStringProperty(parts, connection, "codec");
        AddNumberProperty(parts, connection, "width");
        AddNumberProperty(parts, connection, "height");
        AddStringProperty(parts, connection, "transport");
        return string.Join(" | ", parts);
    }

    private static void AddStringProperty(List<string> parts, JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var value = property.GetString();
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{name}={Truncate(value, 140)}");
        }
    }

    private static void AddNumberProperty(List<string> parts, JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        parts.Add($"{name}={property.GetRawText()}");
    }

    private static void AddBoolProperty(List<string> parts, JsonElement root, string name, bool includeWhenTrue)
    {
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return;
        }

        var value = property.GetBoolean();
        if (value || includeWhenTrue)
        {
            parts.Add($"{name}={value.ToString().ToLowerInvariant()}");
        }
    }

    private static void AddErrorProperty(List<string> parts, JsonElement root)
    {
        if (!root.TryGetProperty("error", out var errorProperty) || errorProperty.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var category = errorProperty.TryGetProperty("category", out var categoryProperty) && categoryProperty.ValueKind == JsonValueKind.String
            ? categoryProperty.GetString()
            : null;
        var message = errorProperty.TryGetProperty("message", out var messageProperty) && messageProperty.ValueKind == JsonValueKind.String
            ? messageProperty.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var safeCategory = category ?? string.Empty;
        var safeMessage = message ?? string.Empty;
        var error = string.IsNullOrWhiteSpace(category)
            ? Truncate(safeMessage, 140)
            : string.IsNullOrWhiteSpace(message)
                ? safeCategory
                : $"{safeCategory}: {Truncate(safeMessage, 140)}";
        parts.Add($"error={error}");
    }

    private static void AddFailureCapsuleProperties(List<string> parts, JsonElement root, string type, FailureCapsuleManifest? failureCapsule)
    {
        if (failureCapsule is null)
        {
            return;
        }

        if (string.Equals(type, "scenario_run_ended", StringComparison.OrdinalIgnoreCase))
        {
            var scenarioNames = failureCapsule.Scenarios
                .Select(static scenario => scenario.Scenario)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (scenarioNames.Length > 0)
            {
                parts.Add($"failed_scenarios={string.Join(", ", scenarioNames.Select(static name => Truncate(name, 60)))}");
            }

            var bundles = failureCapsule.FailureBundles.Select(static bundle => bundle.Path).Distinct(StringComparer.Ordinal).ToArray();
            if (bundles.Length > 0)
            {
                parts.Add($"failure_bundles={string.Join(", ", bundles)}");
            }

            var runArtifacts = failureCapsule.FailureBundles
                .SelectMany(static bundle => bundle.Artifacts.Append(new FailureCapsuleArtifactLink("metadata", bundle.Path, bundle.FailedStep?.Index, bundle.FailedStep?.Name)))
                .Distinct()
                .ToArray();
            AddArtifactSummary(parts, runArtifacts);
            return;
        }

        var scenario = MatchFailureScenario(GetOptionalString(root, "scenario_id"), GetOptionalString(root, "scenario"), failureCapsule);
        if (scenario is null)
        {
            return;
        }

        if (string.Equals(type, "scenario_ended", StringComparison.OrdinalIgnoreCase) && scenario.FailedStep is not null)
        {
            parts.Add($"failed_step={Truncate(scenario.FailedStep.Name, 80)}");
        }

        if (string.Equals(type, "scenario_step_failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "scenario_ended", StringComparison.OrdinalIgnoreCase))
        {
            var stepIndex = GetOptionalInt32(root, "step_index");
            var artifacts = scenario.Artifacts
                .Where(artifact => stepIndex is null || artifact.StepIndex == stepIndex || string.Equals(artifact.Kind, "metadata", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            AddArtifactSummary(parts, artifacts);

            var bundlePaths = failureCapsule.FailureBundles
                .Where(bundle => string.Equals(bundle.ScenarioId, scenario.ScenarioId, StringComparison.Ordinal) ||
                    string.Equals(bundle.Scenario, scenario.Scenario, StringComparison.Ordinal))
                .Select(static bundle => bundle.Path)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (bundlePaths.Length == 1)
            {
                parts.Add($"failure_bundle={bundlePaths[0]}");
            }
            else if (bundlePaths.Length > 1)
            {
                parts.Add($"failure_bundles={string.Join(", ", bundlePaths)}");
            }
        }
    }

    private static void AddArtifactSummary(List<string> parts, IReadOnlyList<FailureCapsuleArtifactLink> artifacts)
    {
        if (artifacts.Count == 0)
        {
            return;
        }

        var summary = string.Join(
            ", ",
            artifacts
                .Take(5)
                .Select(static artifact => $"{artifact.Kind}: {artifact.Path}"));
        if (artifacts.Count > 5)
        {
            summary += $", +{artifacts.Count - 5} more";
        }

        parts.Add($"artifacts={summary}");
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 3)] + "...";
    }

    private (string? Path, FailureCapsuleManifest? Manifest) TryReadFailureCapsule(string metadataPath)
    {
        var failureCapsulePath = ResolveSiblingPath(metadataPath, FailureCapsuleArtifactNames.FileName);
        var fullPath = Path.Join(_root, failureCapsulePath);
        if (!_fileSystem.FileExists(fullPath))
        {
            return (null, null);
        }

        try
        {
            using var stream = _fileSystem.OpenRead(fullPath);
            var manifest = JsonSerializer.Deserialize<FailureCapsuleManifest>(stream, AppJson.Options);
            if (!string.Equals(manifest?.Schema, ResultSchemas.FailureCapsule, StringComparison.Ordinal))
            {
                return (null, null);
            }

            return (failureCapsulePath, manifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, null);
        }
    }

    private static string ResolveSiblingPath(string metadataPath, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(metadataPath);
        return string.IsNullOrWhiteSpace(directory)
            ? path
            : Path.Join(directory, path);
    }

    private static FailureCapsuleScenario? MatchFailureScenario(
        string? scenarioId,
        string? scenarioName,
        FailureCapsuleManifest failureCapsule)
    {
        if (!string.IsNullOrWhiteSpace(scenarioId))
        {
            var byId = failureCapsule.Scenarios.FirstOrDefault(scenario => string.Equals(scenario.ScenarioId, scenarioId, StringComparison.Ordinal));
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(scenarioName))
        {
            return failureCapsule.Scenarios.FirstOrDefault(scenario => string.Equals(scenario.Scenario, scenarioName, StringComparison.Ordinal));
        }

        return null;
    }

    private static bool MatchesFailureScenario(string? scenarioId, string? scenarioName, FailureCapsuleManifest failureCapsule) =>
        MatchFailureScenario(scenarioId, scenarioName, failureCapsule) is not null;

    private static bool MatchesFailureStep(string? scenarioId, string? scenarioName, int? stepIndex, FailureCapsuleManifest failureCapsule)
    {
        var scenario = MatchFailureScenario(scenarioId, scenarioName, failureCapsule);
        if (scenario?.FailedStep is null)
        {
            return false;
        }

        return stepIndex is null || scenario.FailedStep.Index == stepIndex;
    }

    private static string GetRequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"Missing string property '{name}'.");
        }

        return property.GetString() ?? throw new FormatException($"Missing string property '{name}'.");
    }

    private static string? GetOptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static int? GetOptionalInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            return null;
        }

        return value;
    }

    private static int GetRequiredInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            throw new FormatException($"Missing number property '{name}'.");
        }

        return value;
    }

    private static DateTimeOffset GetRequiredDateTimeOffset(JsonElement root, string name)
    {
        var raw = GetRequiredString(root, name);
        if (!DateTimeOffset.TryParse(raw, out var value))
        {
            throw new FormatException($"Invalid date property '{name}'.");
        }

        return value;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }
}

internal sealed record SessionReplaySummary(
    string MetadataPath,
    string TimelinePath,
    string? FailureCapsulePath,
    FailureCapsuleManifest? FailureCapsule,
    string SessionKind,
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Reason,
    int ExitCode,
    string? Target,
    int EventCount,
    IReadOnlyList<string> EventTypes,
    bool HasTimeline,
    bool HasFailureSignals,
    IReadOnlyList<SessionReplayTimelineEntry> TimelineHighlights);

internal sealed record SessionReplayTimelineEntry(
    int Sequence,
    DateTimeOffset? Timestamp,
    string Type,
    string Detail,
    bool IsFailureRelevant,
    string? ScenarioId,
    string? Scenario,
    int? StepIndex);