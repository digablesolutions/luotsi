using System.Text.RegularExpressions;
using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal sealed class ReplayGraphService(IFileSystem fileSystem, ReplayTimelineService timelineService)
{
    private const string GraphJsonFileName = "replay-graph.json";
    private const string GraphJsonlFileName = "replay-graph.jsonl";
    private const string GraphMarkdownFileName = "replay-graph.md";
    private static readonly JsonSerializerOptions JsonLineOptions = new(AppJson.Options) { WriteIndented = false };
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ReplayTimelineService _timelineService = timelineService ?? throw new ArgumentNullException(nameof(timelineService));
    private static readonly Regex StableIdChars = new("[^a-zA-Z0-9._:-]+", RegexOptions.Compiled);

    public async Task<ReplayGraphResult> CreateAsync(CliOptions options, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(artifacts);

        var allFiles = _fileSystem.GetFiles(artifacts.Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(artifacts.Root, path))
            .ToArray();
        var summaries = new SessionReplaySummaryReader(artifacts.Root, _fileSystem).ReadSummaries(allFiles);
        if (summaries.Count == 0)
        {
            throw new UsageException($"No session replay metadata was found under artifact root '{artifacts.Root}'.");
        }

        var timeline = await _timelineService.ReadEventsAsync(options, artifacts).ConfigureAwait(false);
        var nodes = new Dictionary<string, ReplayGraphNodeResult>(StringComparer.Ordinal);
        var edges = new List<ReplayGraphEdgeResult>();

        foreach (var summary in summaries)
        {
            var sessionId = "session:" + summary.SessionId;
            AddNode(nodes, new ReplayGraphNodeResult(
                sessionId,
                "session",
                summary.SessionKind + " " + summary.SessionId,
                new Dictionary<string, string?>
                {
                    ["kind"] = summary.SessionKind,
                    ["started_at"] = summary.StartedAt.ToString("O"),
                    ["ended_at"] = summary.EndedAt.ToString("O"),
                    ["reason"] = summary.Reason,
                    ["target"] = summary.Target,
                    ["metadata_path"] = summary.MetadataPath,
                    ["timeline_path"] = summary.TimelinePath
                }));

            if (summary.FailureCapsule is not null)
            {
                AddFailureCapsule(summary, sessionId, nodes, edges);
            }
        }

        new ReplayGraphScenarioDraftAppender(_fileSystem).Add(artifacts.Root, allFiles, nodes, edges);

        ReplayTimelineEventResult? previousEvent = null;
        foreach (var evt in timeline.Events)
        {
            var eventId = "event:" + evt.Path + ":" + evt.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            AddNode(nodes, new ReplayGraphNodeResult(
                eventId,
                "event",
                evt.Type,
                new Dictionary<string, string?>
                {
                    ["path"] = evt.Path,
                    ["sequence"] = evt.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["timestamp"] = evt.Timestamp?.ToString("O"),
                    ["failure_relevant"] = evt.FailureRelevant.ToString().ToLowerInvariant(),
                    ["detail"] = evt.Detail
                }.Concat(evt.Properties.Select(static property => new KeyValuePair<string, string?>("event." + property.Key, property.Value)))
                    .ToDictionary(static property => property.Key, static property => property.Value, StringComparer.Ordinal)));

            if (previousEvent is not null && string.Equals(previousEvent.Path, evt.Path, StringComparison.Ordinal))
            {
                var previousEventId = "event:" + previousEvent.Path + ":" + previousEvent.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
                edges.Add(new ReplayGraphEdgeResult(previousEventId, eventId, "next", new Dictionary<string, string?>()));
                edges.Add(new ReplayGraphEdgeResult(previousEventId, eventId, "transitions_to", BuildTransitionProperties(previousEvent, evt)));
            }

            if (evt.FailureRelevant)
            {
                var failureId = "failure:" + evt.Path + ":" + evt.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
                AddNode(nodes, new ReplayGraphNodeResult(
                    failureId,
                    "failure",
                    evt.Type,
                    new Dictionary<string, string?>
                    {
                        ["timestamp"] = evt.Timestamp?.ToString("O"),
                        ["detail"] = evt.Detail
                    }));
                edges.Add(new ReplayGraphEdgeResult(eventId, failureId, "indicates", new Dictionary<string, string?>()));
            }

            AddSemanticEventNodes(evt, eventId, nodes, edges);
            previousEvent = evt;
        }

        var query = ReplayGraphQueryEngine.Create(options);
        var allNodes = nodes.Values.OrderBy(static node => node.Id, StringComparer.Ordinal).ToArray();
        var allEdges = edges
            .OrderBy(static edge => edge.From, StringComparer.Ordinal)
            .ThenBy(static edge => edge.Kind, StringComparer.Ordinal)
            .ThenBy(static edge => edge.To, StringComparer.Ordinal)
            .ToArray();
        var filtered = ReplayGraphQueryEngine.Apply(allNodes, allEdges, query);
        var orderedNodes = filtered.Nodes;
        var orderedEdges = filtered.Edges;
        var jsonPath = options.HasFlag("write-json")
            ? Path.Join(artifacts.Root, GraphJsonFileName)
            : null;
        var jsonlPath = options.HasFlag("write-jsonl")
            ? Path.Join(artifacts.Root, GraphJsonlFileName)
            : null;
        var markdownPath = options.HasFlag("write-markdown")
            ? Path.Join(artifacts.Root, GraphMarkdownFileName)
            : null;
        var insights = ReplayGraphQueryEngine.ApplyInsightFilters(
            ReplayGraphInsightBuilder.Build(allNodes, allEdges),
            query);
        var actions = ReplayGraphInsightBuilder.BuildActions(artifacts.Root, query, allNodes);
        var evidence = ReplayGraphQueryEngine.ApplyEvidenceFilters(
            ReplayGraphEvidenceBuilder.Build(artifacts.Root, orderedNodes, orderedEdges),
            query);
        var failurePaths = ReplayGraphFailurePathBuilder.Build(allNodes, allEdges);
        var facts = ReplayGraphQueryEngine.ApplyFactFilters(
            ReplayGraphFactBuilder.Build(artifacts.Root, orderedNodes, orderedEdges, evidence, failurePaths),
            query);
        var result = new ReplayGraphResult(
            ResultSchemas.ReplayGraph,
            artifacts.Root,
            query,
            orderedNodes.Count,
            orderedEdges.Count,
            allNodes.Length,
            allEdges.Length,
            filtered.MatchedNodeCount,
            filtered.MatchedEdgeCount,
            filtered.Truncated,
            CountKinds(orderedNodes),
            CountKinds(orderedEdges),
            ReplayGraphTaxonomy.Build(artifacts.Root),
            ReplayGraphAgentSummaryBuilder.Build(allNodes, allEdges, failurePaths, evidence, actions),
            insights,
            actions,
            CountKinds(evidence),
            evidence,
            facts,
            failurePaths,
            jsonPath,
            jsonlPath,
            markdownPath,
            orderedNodes,
            orderedEdges);

        if (jsonPath is not null)
        {
            await artifacts.WriteJsonAsync(GraphJsonFileName, result).ConfigureAwait(false);
        }

        if (jsonlPath is not null)
        {
            await artifacts.WriteTextAsync(GraphJsonlFileName, ToJsonLines(result)).ConfigureAwait(false);
        }

        if (markdownPath is not null)
        {
            await artifacts.WriteTextAsync(GraphMarkdownFileName, ReplayGraphMarkdownWriter.Build(result)).ConfigureAwait(false);
        }

        return result;
    }

    public static IEnumerable<object> ToJsonLineObjects(ReplayGraphResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        yield return new ReplayGraphJsonLine(
            ResultSchemas.ReplayGraph,
            "summary",
            result.ArtifactRoot,
            result.NodeCount,
            result.EdgeCount,
            result.TotalNodeCount,
            result.TotalEdgeCount,
            result.Truncated,
            result.Query,
            result.AgentSummary,
            result.NodeKinds,
            result.EdgeKinds,
            result.EvidenceKinds,
            null,
            null,
            null,
            null,
            null,
            null);

        foreach (var path in result.FailurePaths)
        {
            yield return new ReplayGraphJsonLine(
                ResultSchemas.ReplayGraph,
                "failure_path",
                result.ArtifactRoot,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                path,
                null,
                null,
                null,
                null,
                null);
        }

        foreach (var evidence in result.Evidence)
        {
            yield return new ReplayGraphJsonLine(
                ResultSchemas.ReplayGraph,
                "evidence",
                result.ArtifactRoot,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                evidence,
                null,
                null,
                null);
        }

        foreach (var fact in result.Facts)
        {
            yield return new ReplayGraphJsonLine(
                ResultSchemas.ReplayGraph,
                "fact",
                result.ArtifactRoot,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                fact,
                null,
                null);
        }

        foreach (var insight in result.Insights)
        {
            yield return new ReplayGraphJsonLine(
                ResultSchemas.ReplayGraph,
                "insight",
                result.ArtifactRoot,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                insight,
                null,
                null,
                null,
                null);
        }

        foreach (var node in result.Nodes)
        {
            yield return new ReplayGraphJsonLine(
                ResultSchemas.ReplayGraph,
                "node",
                result.ArtifactRoot,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                node,
                null);
        }

        foreach (var edge in result.Edges)
        {
            yield return new ReplayGraphJsonLine(
                ResultSchemas.ReplayGraph,
                "edge",
                result.ArtifactRoot,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                edge);
        }
    }

    private static string ToJsonLines(ReplayGraphResult result) =>
        string.Join('\n', ToJsonLineObjects(result).Select(static line => JsonSerializer.Serialize(line, JsonLineOptions))) + "\n";

    private static void AddFailureCapsule(
        SessionReplaySummary summary,
        string sessionId,
        Dictionary<string, ReplayGraphNodeResult> nodes,
        List<ReplayGraphEdgeResult> edges)
    {
        var capsule = summary.FailureCapsule!;
        var capsuleId = "capsule:" + summary.FailureCapsulePath;
        AddNode(nodes, new ReplayGraphNodeResult(
            capsuleId,
            "failure_capsule",
            summary.FailureCapsulePath ?? FailureCapsuleArtifactNames.FileName,
            new Dictionary<string, string?>
            {
                ["path"] = summary.FailureCapsulePath,
                ["status"] = capsule.Status
            }));
        edges.Add(new ReplayGraphEdgeResult(sessionId, capsuleId, "has_capsule", new Dictionary<string, string?>()));

        foreach (var scenario in capsule.Scenarios)
        {
            var scenarioId = "scenario:" + (scenario.ScenarioId ?? scenario.Scenario);
            AddNode(nodes, new ReplayGraphNodeResult(
                scenarioId,
                "scenario",
                scenario.Scenario,
                new Dictionary<string, string?>
                {
                    ["scenario_id"] = scenario.ScenarioId,
                    ["status"] = scenario.Status,
                    ["file"] = scenario.File,
                    ["error_message"] = scenario.Error?.Message,
                    ["error_category"] = scenario.Error?.Category
                }));
            edges.Add(new ReplayGraphEdgeResult(capsuleId, scenarioId, "contains", new Dictionary<string, string?>()));

            foreach (var artifact in scenario.Artifacts)
            {
                var artifactId = "artifact:" + artifact.Path;
                AddNode(nodes, new ReplayGraphNodeResult(
                    artifactId,
                    "artifact",
                    artifact.Path,
                    new Dictionary<string, string?>
                    {
                        ["kind"] = artifact.Kind,
                        ["step_index"] = artifact.StepIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["step_name"] = artifact.StepName
                    }));
                edges.Add(new ReplayGraphEdgeResult(scenarioId, artifactId, "has_artifact", new Dictionary<string, string?> { ["kind"] = artifact.Kind }));
            }
        }
    }

    private static void AddNode(Dictionary<string, ReplayGraphNodeResult> nodes, ReplayGraphNodeResult node) =>
        nodes.TryAdd(node.Id, node);

    private static void AddSemanticEventNodes(
        ReplayTimelineEventResult evt,
        string eventId,
        Dictionary<string, ReplayGraphNodeResult> nodes,
        List<ReplayGraphEdgeResult> edges)
    {
        var action = FirstProperty(evt, "action", "command");
        if (!string.IsNullOrWhiteSpace(action))
        {
            var actionId = "action:" + StableId(action);
            AddNode(nodes, new ReplayGraphNodeResult(
                actionId,
                "action",
                action,
                new Dictionary<string, string?>
                {
                    ["action"] = action,
                    ["source_event_type"] = evt.Type
                }));
            edges.Add(new ReplayGraphEdgeResult(eventId, actionId, "describes_action", new Dictionary<string, string?>()));
        }

        var selector = FirstProperty(evt, "text", "data.text", "label", "data.label");
        if (!string.IsNullOrWhiteSpace(selector))
        {
            var selectorId = "selector:text:" + StableId(selector);
            AddNode(nodes, new ReplayGraphNodeResult(
                selectorId,
                "selector",
                selector,
                new Dictionary<string, string?>
                {
                    ["strategy"] = "text",
                    ["value"] = selector
                }));
            edges.Add(new ReplayGraphEdgeResult(eventId, selectorId, "mentions_selector", new Dictionary<string, string?> { ["strategy"] = "text" }));
        }

        if (IsScreenStateEvent(evt))
        {
            var screenId = "screen_state:" + evt.Path + ":" + evt.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            AddNode(nodes, new ReplayGraphNodeResult(
                screenId,
                "screen_state",
                evt.Type,
                new Dictionary<string, string?>
                {
                    ["path"] = evt.Path,
                    ["timestamp"] = evt.Timestamp?.ToString("O"),
                    ["detail"] = evt.Detail
                }));
            edges.Add(new ReplayGraphEdgeResult(eventId, screenId, "observes_screen", new Dictionary<string, string?>()));
        }

        var telemetrySignal = FirstProperty(evt, "data.event", "event", "data.step", "data.action");
        if (IsTelemetryEvent(evt) && !string.IsNullOrWhiteSpace(telemetrySignal))
        {
            var telemetryId = "telemetry:" + StableId(telemetrySignal);
            AddNode(nodes, new ReplayGraphNodeResult(
                telemetryId,
                "telemetry_signal",
                telemetrySignal,
                new Dictionary<string, string?>
                {
                    ["signal"] = telemetrySignal,
                    ["step"] = FirstProperty(evt, "data.step", "step"),
                    ["action"] = FirstProperty(evt, "data.action", "action")
                }));
            edges.Add(new ReplayGraphEdgeResult(eventId, telemetryId, "observes_telemetry", new Dictionary<string, string?>()));
        }
    }

    private static bool IsScreenStateEvent(ReplayTimelineEventResult evt)
    {
        var action = FirstProperty(evt, "action", "command");
        return evt.Type.Contains("screen", StringComparison.OrdinalIgnoreCase) ||
            evt.Detail.Contains("screen_state", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "screen_state", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "take_screenshot", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "screenshot", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTelemetryEvent(ReplayTimelineEventResult evt)
    {
        var action = FirstProperty(evt, "action", "command");
        return evt.Type.Contains("telemetry", StringComparison.OrdinalIgnoreCase) ||
            evt.Detail.Contains("telemetry", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "telemetry_tail", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "telemetry_watch", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstProperty(ReplayTimelineEventResult evt, params string[] names)
    {
        return names
            .Select(name => evt.Properties.TryGetValue(name, out var value) ? value : null)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .FirstOrDefault();
    }

    private static string StableId(string value)
    {
        var stable = StableIdChars.Replace(value.Trim(), "-").Trim('-');
        return stable.Length == 0 ? "value" : stable.ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, string?> BuildTransitionProperties(ReplayTimelineEventResult from, ReplayTimelineEventResult to)
    {
        var properties = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["from_type"] = from.Type,
            ["to_type"] = to.Type,
            ["from_detail"] = from.Detail,
            ["to_detail"] = to.Detail,
            ["category"] = ClassifyTransition(from, to)
        };

        if (from.Timestamp is not null && to.Timestamp is not null)
        {
            properties["elapsed_ms"] = (to.Timestamp.Value - from.Timestamp.Value).TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        var fromAction = FirstProperty(from, "action", "command");
        var toAction = FirstProperty(to, "action", "command");
        if (!string.IsNullOrWhiteSpace(fromAction))
        {
            properties["from_action"] = fromAction;
        }

        if (!string.IsNullOrWhiteSpace(toAction))
        {
            properties["to_action"] = toAction;
        }

        return properties;
    }

    private static string ClassifyTransition(ReplayTimelineEventResult from, ReplayTimelineEventResult to)
    {
        if (to.FailureRelevant)
        {
            var action = FirstProperty(from, "action", "command") ?? FirstProperty(to, "action", "command");
            return string.IsNullOrWhiteSpace(action) ? "to_failure" : "action_to_failure";
        }

        if (from.FailureRelevant)
        {
            return "from_failure";
        }

        if (IsScreenStateEvent(to))
        {
            return "screen_changed";
        }

        if (IsTelemetryEvent(to))
        {
            return "telemetry_observed";
        }

        return "progression";
    }

    private static IReadOnlyDictionary<string, int> CountKinds(IReadOnlyList<ReplayGraphNodeResult> nodes) =>
        nodes.GroupBy(static node => node.Kind, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, int> CountKinds(IReadOnlyList<ReplayGraphEdgeResult> edges) =>
        edges.GroupBy(static edge => edge.Kind, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, int> CountKinds(IReadOnlyList<ReplayGraphEvidenceResult> evidence) =>
        evidence.GroupBy(static item => item.Kind, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

    private sealed record ReplayGraphJsonLine(
        string Schema,
        string Type,
        string ArtifactRoot,
        int? NodeCount,
        int? EdgeCount,
        int? TotalNodeCount,
        int? TotalEdgeCount,
        bool? Truncated,
        ReplayGraphQueryResult? Query,
        ReplayGraphAgentSummaryResult? AgentSummary,
        IReadOnlyDictionary<string, int>? NodeKinds,
        IReadOnlyDictionary<string, int>? EdgeKinds,
        IReadOnlyDictionary<string, int>? EvidenceKinds,
        ReplayGraphFailurePathResult? FailurePath,
        ReplayGraphInsightResult? Insight,
        ReplayGraphEvidenceResult? Evidence,
        ReplayGraphFactResult? Fact,
        ReplayGraphNodeResult? Node,
        ReplayGraphEdgeResult? Edge);

}
