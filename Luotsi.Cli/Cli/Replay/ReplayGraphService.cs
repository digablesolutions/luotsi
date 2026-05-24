using System.Text;
using System.Text.RegularExpressions;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Replay;

internal sealed class ReplayGraphService(IFileSystem fileSystem, ReplayTimelineService timelineService)
{
    private const string GraphJsonFileName = "replay-graph.json";
    private const string GraphMarkdownFileName = "replay-graph.md";
    private const int DefaultLimit = 200;
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
                edges.Add(new ReplayGraphEdgeResult(
                    "event:" + previousEvent.Path + ":" + previousEvent.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    eventId,
                    "next",
                    new Dictionary<string, string?>()));
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

        var query = CreateQuery(options);
        var allNodes = nodes.Values.OrderBy(static node => node.Id, StringComparer.Ordinal).ToArray();
        var allEdges = edges
            .OrderBy(static edge => edge.From, StringComparer.Ordinal)
            .ThenBy(static edge => edge.Kind, StringComparer.Ordinal)
            .ThenBy(static edge => edge.To, StringComparer.Ordinal)
            .ToArray();
        var filtered = ApplyQuery(allNodes, allEdges, query);
        var orderedNodes = filtered.Nodes;
        var orderedEdges = filtered.Edges;
        var jsonPath = options.HasFlag("write-json")
            ? Path.Join(artifacts.Root, GraphJsonFileName)
            : null;
        var markdownPath = options.HasFlag("write-markdown")
            ? Path.Join(artifacts.Root, GraphMarkdownFileName)
            : null;
        var result = new ReplayGraphResult(
            ResultSchemas.ReplayGraph,
            artifacts.Root,
            query,
            orderedNodes.Count,
            orderedEdges.Count,
            allNodes.Length,
            allEdges.Length,
            CountKinds(orderedNodes),
            CountKinds(orderedEdges),
            BuildInsights(allNodes, allEdges),
            BuildActions(artifacts.Root, query, allNodes, allEdges),
            jsonPath,
            markdownPath,
            orderedNodes,
            orderedEdges);

        if (jsonPath is not null)
        {
            await artifacts.WriteJsonAsync(GraphJsonFileName, result).ConfigureAwait(false);
        }

        if (markdownPath is not null)
        {
            await artifacts.WriteTextAsync(GraphMarkdownFileName, BuildMarkdown(result)).ConfigureAwait(false);
        }

        return result;
    }

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

    private static IReadOnlyDictionary<string, int> CountKinds(IReadOnlyList<ReplayGraphNodeResult> nodes) =>
        nodes.GroupBy(static node => node.Kind, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, int> CountKinds(IReadOnlyList<ReplayGraphEdgeResult> edges) =>
        edges.GroupBy(static edge => edge.Kind, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

    private static ReplayGraphQueryResult CreateQuery(CliOptions options)
    {
        var limit = options.Int("limit", DefaultLimit);
        if (limit <= 0)
        {
            throw new UsageException("replay graph requires --limit greater than zero.");
        }

        return new ReplayGraphQueryResult(
            NormalizeBlank(options.Get("node-kind")),
            NormalizeBlank(options.Get("edge-kind")),
            NormalizeBlank(options.Get("action")),
            NormalizeBlank(options.Get("selector")),
            options.HasFlag("failed"),
            limit);
    }

    private static (IReadOnlyList<ReplayGraphNodeResult> Nodes, IReadOnlyList<ReplayGraphEdgeResult> Edges) ApplyQuery(
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges,
        ReplayGraphQueryResult query)
    {
        var filteredNodes = nodes.Where(node => MatchesNode(node, query)).ToArray();
        var nodeIds = filteredNodes.Select(static node => node.Id).ToHashSet(StringComparer.Ordinal);
        var filteredEdges = edges
            .Where(edge => MatchesEdge(edge, query))
            .Where(edge => nodeIds.Contains(edge.From) || nodeIds.Contains(edge.To))
            .ToArray();

        if (query.EdgeKind is not null && query.NodeKind is null && query.Action is null && query.Selector is null && !query.FailedOnly)
        {
            var edgeNodeIds = filteredEdges.SelectMany(static edge => new[] { edge.From, edge.To }).ToHashSet(StringComparer.Ordinal);
            filteredNodes = nodes.Where(node => edgeNodeIds.Contains(node.Id)).ToArray();
            nodeIds = filteredNodes.Select(static node => node.Id).ToHashSet(StringComparer.Ordinal);
        }

        filteredNodes = ExpandOneHopContext(nodes, edges, filteredNodes, nodeIds, query);
        nodeIds = filteredNodes.Select(static node => node.Id).ToHashSet(StringComparer.Ordinal);
        filteredEdges = edges
            .Where(edge => MatchesEdge(edge, query) || query.EdgeKind is null)
            .Where(edge => nodeIds.Contains(edge.From) && nodeIds.Contains(edge.To))
            .ToArray();

        return (
            filteredNodes.Take(query.Limit).ToArray(),
            filteredEdges.Take(query.Limit).ToArray());
    }

    private static ReplayGraphNodeResult[] ExpandOneHopContext(
        IReadOnlyList<ReplayGraphNodeResult> allNodes,
        IReadOnlyList<ReplayGraphEdgeResult> allEdges,
        IReadOnlyList<ReplayGraphNodeResult> filteredNodes,
        HashSet<string> nodeIds,
        ReplayGraphQueryResult query)
    {
        if (query.NodeKind is null && query.Action is null && query.Selector is null && !query.FailedOnly)
        {
            return filteredNodes.ToArray();
        }

        var contextIds = new HashSet<string>(nodeIds, StringComparer.Ordinal);
        foreach (var edge in allEdges)
        {
            if (nodeIds.Contains(edge.From))
            {
                contextIds.Add(edge.To);
            }

            if (nodeIds.Contains(edge.To))
            {
                contextIds.Add(edge.From);
            }
        }

        return allNodes.Where(node => contextIds.Contains(node.Id)).ToArray();
    }

    private static bool MatchesNode(ReplayGraphNodeResult node, ReplayGraphQueryResult query)
    {
        if (query.NodeKind is not null && !string.Equals(node.Kind, query.NodeKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.Action is not null && !Contains(node.Label, query.Action) && !PropertyContains(node, "action", query.Action))
        {
            return false;
        }

        if (query.Selector is not null && !Contains(node.Label, query.Selector) && !PropertyContains(node, "value", query.Selector))
        {
            return false;
        }

        if (query.FailedOnly && !IsFailureNode(node))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesEdge(ReplayGraphEdgeResult edge, ReplayGraphQueryResult query) =>
        query.EdgeKind is null || string.Equals(edge.Kind, query.EdgeKind, StringComparison.OrdinalIgnoreCase);

    private static bool IsFailureNode(ReplayGraphNodeResult node) =>
        string.Equals(node.Kind, "failure", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(GetProperty(node, "status"), "failed", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(GetProperty(node, "error_message")) ||
        string.Equals(GetProperty(node, "failure_relevant"), "true", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ReplayGraphInsightResult> BuildInsights(
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges)
    {
        var insights = new List<ReplayGraphInsightResult>();
        var failures = nodes.Where(IsFailureNode).Take(8).ToArray();
        if (failures.Length > 0)
        {
            insights.Add(new ReplayGraphInsightResult(
                "failure",
                "error",
                $"Graph contains {failures.Length} failure-relevant node(s). Start with `replay graph --failed --write-markdown` or `replay scrub --failures`.",
                failures.Select(static node => node.Id).ToArray(),
                []));
        }

        var selectors = nodes.Where(static node => string.Equals(node.Kind, "selector", StringComparison.Ordinal)).Take(8).ToArray();
        if (selectors.Length > 0)
        {
            insights.Add(new ReplayGraphInsightResult(
                "selector",
                "info",
                $"Graph promotes {selectors.Length} selector node(s) that agents can map back to actions and failures.",
                selectors.Select(static node => node.Id).ToArray(),
                edges.Where(edge => selectors.Any(selector => edge.From == selector.Id || edge.To == selector.Id)).Select(EdgeId).Take(8).ToArray()));
        }

        var telemetry = nodes.Where(static node => string.Equals(node.Kind, "telemetry_signal", StringComparison.Ordinal)).Take(8).ToArray();
        if (telemetry.Length > 0)
        {
            insights.Add(new ReplayGraphInsightResult(
                "telemetry",
                "info",
                $"Graph includes {telemetry.Length} telemetry signal node(s) for semantic assertions and waits.",
                telemetry.Select(static node => node.Id).ToArray(),
                []));
        }

        var drafts = nodes.Where(static node => string.Equals(node.Kind, "scenario_draft", StringComparison.Ordinal)).Take(8).ToArray();
        if (drafts.Length > 0)
        {
            insights.Add(new ReplayGraphInsightResult(
                "scenario_draft",
                "info",
                "Scenario draft provenance is present; use generated_step and draft_source edges to audit where each step came from.",
                drafts.Select(static node => node.Id).ToArray(),
                []));
        }

        return insights;
    }

    private static IReadOnlyList<ReplayGraphActionResult> BuildActions(
        string artifactRoot,
        ReplayGraphQueryResult query,
        IReadOnlyList<ReplayGraphNodeResult> nodes,
        IReadOnlyList<ReplayGraphEdgeResult> edges)
    {
        var actions = new List<ReplayGraphActionResult>
        {
            new("open_artifacts", "Open the browser index for screenshots, logs, reports, graph, and replay files.", $"luotsi replay open --artifacts {Quote(artifactRoot)} --dry-run"),
            new("scrub_failures", "Review the failure timeline with previous/focused/next context.", $"luotsi replay scrub --artifacts {Quote(artifactRoot)} --failures --context 3 --write-markdown")
        };

        if (!query.FailedOnly && nodes.Any(IsFailureNode))
        {
            actions.Add(new ReplayGraphActionResult("filter_failures", "Narrow graph output to failure-relevant nodes and their local context.", $"luotsi replay graph --artifacts {Quote(artifactRoot)} --failed --write-markdown"));
        }

        if (nodes.Any(static node => string.Equals(node.Kind, "selector", StringComparison.Ordinal)))
        {
            actions.Add(new ReplayGraphActionResult("filter_selectors", "List promoted selector nodes and the actions/events that mention them.", $"luotsi replay graph --artifacts {Quote(artifactRoot)} --node-kind selector --write-markdown"));
        }

        if (nodes.Any(static node => string.Equals(node.Kind, "scenario_draft", StringComparison.Ordinal)))
        {
            actions.Add(new ReplayGraphActionResult("audit_draft", "Inspect generated scenario draft provenance.", $"luotsi replay graph --artifacts {Quote(artifactRoot)} --node-kind generated_step --write-markdown"));
        }

        return actions;
    }

    private static string BuildMarkdown(ReplayGraphResult graph)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Luotsi Replay Graph");
        builder.AppendLine();
        builder.AppendLine($"Artifact root: `{graph.ArtifactRoot}`");
        builder.AppendLine($"Nodes: `{graph.NodeCount}` of `{graph.TotalNodeCount}`");
        builder.AppendLine($"Edges: `{graph.EdgeCount}` of `{graph.TotalEdgeCount}`");
        builder.AppendLine($"Query: `{DescribeQuery(graph.Query)}`");
        builder.AppendLine();
        builder.AppendLine("## What Failed");
        builder.AppendLine();
        var failures = graph.Nodes.Where(IsFailureNode).Take(10).ToArray();
        if (failures.Length == 0)
        {
            builder.AppendLine("No failure-relevant nodes are present in this graph view.");
        }
        else
        {
            foreach (var failure in failures)
            {
                builder.AppendLine($"- `{EscapeMarkdown(failure.Id)}` {EscapeMarkdown(failure.Label)}: {EscapeMarkdown(GetProperty(failure, "detail") ?? GetProperty(failure, "error_message"))}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## What Agents Can Act On");
        builder.AppendLine();
        foreach (var action in graph.Actions)
        {
            builder.AppendLine($"- **{EscapeMarkdown(action.Kind)}**: {EscapeMarkdown(action.Message)}");
            if (!string.IsNullOrWhiteSpace(action.Command))
            {
                builder.AppendLine($"  `{EscapeMarkdown(action.Command)}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Insights");
        builder.AppendLine();
        if (graph.Insights.Count == 0)
        {
            builder.AppendLine("No semantic insights were derived from this graph.");
        }
        else
        {
            builder.AppendLine("| Kind | Severity | Message | Nodes |");
            builder.AppendLine("|---|---|---|---|");
            foreach (var insight in graph.Insights)
            {
                builder.AppendLine($"| {EscapeMarkdown(insight.Kind)} | {EscapeMarkdown(insight.Severity)} | {EscapeMarkdown(insight.Message)} | {EscapeMarkdown(string.Join(", ", insight.NodeIds.Take(5)))} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Node Kinds");
        builder.AppendLine();
        builder.AppendLine("| Kind | Count |");
        builder.AppendLine("|---|---:|");
        foreach (var kind in graph.NodeKinds)
        {
            builder.AppendLine($"| {EscapeMarkdown(kind.Key)} | {kind.Value} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Edge Kinds");
        builder.AppendLine();
        builder.AppendLine("| Kind | Count |");
        builder.AppendLine("|---|---:|");
        foreach (var kind in graph.EdgeKinds)
        {
            builder.AppendLine($"| {EscapeMarkdown(kind.Key)} | {kind.Value} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Nodes");
        builder.AppendLine();
        builder.AppendLine("| Id | Kind | Label |");
        builder.AppendLine("|---|---|---|");
        foreach (var node in graph.Nodes)
        {
            builder.AppendLine($"| {EscapeMarkdown(node.Id)} | {EscapeMarkdown(node.Kind)} | {EscapeMarkdown(node.Label)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Edges");
        builder.AppendLine();
        builder.AppendLine("| From | Kind | To |");
        builder.AppendLine("|---|---|---|");
        foreach (var edge in graph.Edges)
        {
            builder.AppendLine($"| {EscapeMarkdown(edge.From)} | {EscapeMarkdown(edge.Kind)} | {EscapeMarkdown(edge.To)} |");
        }

        return builder.ToString();
    }

    private static string DescribeQuery(ReplayGraphQueryResult query)
    {
        var parts = new List<string>();
        AddQueryPart(parts, "node-kind", query.NodeKind);
        AddQueryPart(parts, "edge-kind", query.EdgeKind);
        AddQueryPart(parts, "action", query.Action);
        AddQueryPart(parts, "selector", query.Selector);
        if (query.FailedOnly)
        {
            parts.Add("failed=true");
        }

        parts.Add("limit=" + query.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return string.Join(", ", parts);
    }

    private static void AddQueryPart(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(name + "=" + value);
        }
    }

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Contains(string? source, string value) =>
        source?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;

    private static bool PropertyContains(ReplayGraphNodeResult node, string propertyName, string value) =>
        node.Properties.Any(property =>
            property.Key.EndsWith(propertyName, StringComparison.OrdinalIgnoreCase) &&
            Contains(property.Value, value));

    private static string? GetProperty(ReplayGraphNodeResult node, string name) =>
        node.Properties.TryGetValue(name, out var value) ? value : null;

    private static string EdgeId(ReplayGraphEdgeResult edge) => edge.From + " -> " + edge.Kind + " -> " + edge.To;

    private static string Quote(string value) => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static string EscapeMarkdown(string? value) =>
        (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

}
