using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Discovery;

internal sealed class DiscoveryCommandService(IFileSystem fileSystem, TimeProvider timeProvider)
{
    private const string MapFileName = "discovery-map.json";
    private const string EventsFileName = "discovery-events.jsonl";
    private const string ScenarioCandidateDirectory = "scenario-candidates";
    private const string ScenarioCandidateFileName = "discovery-candidate.json";
    private const int DefaultBudgetSeconds = 300;
    private const int DefaultMaxActions = 25;
    private const int DefaultPostTapDelayMs = 300;
    private static readonly JsonSerializerOptions EventJsonOptions = new(AppJson.Options)
    {
        WriteIndented = false
    };

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly DiscoveryPlanner _planner = new();

    public async Task<DiscoveryRunResult> RunAsync(CliOptions options, IDeviceHost runner, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(artifacts);

        var packageName = options.Require("package").Trim();
        var activity = NormalizeOptional(options.Get("activity"));
        var budget = ParseBudget(options.Get("budget"));
        var maxActions = options.Int("max-actions", DefaultMaxActions);
        var postTapDelayMs = options.Int("post-tap-delay-ms", DefaultPostTapDelayMs);
        if (maxActions <= 0)
        {
            throw new UsageException("Option --max-actions must be greater than zero.");
        }

        if (postTapDelayMs < 0)
        {
            throw new UsageException("Option --post-tap-delay-ms must be zero or greater.");
        }

        var startedAt = _timeProvider.GetUtcNow();
        var deadline = startedAt.Add(budget);
        var replay = new DiscoveryReplayRecorder(artifacts, packageName, startedAt);
        var events = new DiscoveryEventLog(_timeProvider, replay.Record);
        var screens = new DiscoveryScreenRegistry(_planner);
        var transitions = new List<DiscoveryMapTransition>();
        var successfulActions = new List<DiscoveryExecutedAction>();
        var attemptedActionKeys = new HashSet<string>(StringComparer.Ordinal);
        var stopReason = "completed";
        PreflightResult? readiness = null;

        events.Add("discovery_started", new Dictionary<string, object?>
        {
            ["package"] = packageName,
            ["activity"] = activity,
            ["budget_ms"] = (long)budget.TotalMilliseconds,
            ["max_actions"] = maxActions,
            ["artifact_root"] = artifacts.Root
        });

        if (!options.HasFlag("no-start"))
        {
            var start = await runner.StartAppAsync(packageName, activity, wait: activity is not null && options.HasFlag("wait")).ConfigureAwait(false);
            events.Add("app_started", new Dictionary<string, object?>
            {
                ["package"] = start.Package,
                ["activity"] = start.Activity,
                ["component"] = start.Component,
                ["wait"] = start.Wait
            });
        }

        readiness = await runner.PreflightAsync(packageName).ConfigureAwait(false);
        events.Add("device_ready", new Dictionary<string, object?>
        {
            ["serial"] = readiness.Serial,
            ["model"] = readiness.Model,
            ["android_release"] = readiness.AndroidRelease,
            ["sdk"] = readiness.Sdk,
            ["current_focus"] = readiness.CurrentFocus,
            ["foreground_package"] = readiness.ForegroundPackage,
            ["display_width"] = readiness.DisplayWidth,
            ["display_height"] = readiness.DisplayHeight,
            ["display_orientation"] = readiness.DisplayOrientation
        });

        while (attemptedActionKeys.Count < maxActions && _timeProvider.GetUtcNow() < deadline)
        {
            DiscoveryScreenObservation currentScreen;
            try
            {
                currentScreen = screens.Observe(await runner.GetScreenStateAsync().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                stopReason = "screen_state_failed";
                events.Add("screen_state_failed", new Dictionary<string, object?>
                {
                    ["error_type"] = ex.GetType().FullName ?? ex.GetType().Name,
                    ["message"] = ex.Message
                });
                break;
            }

            events.Add("screen_observed", new Dictionary<string, object?>
            {
                ["screen_id"] = currentScreen.Screen.Id,
                ["signature"] = currentScreen.Screen.Signature,
                ["element_count"] = currentScreen.Screen.ElementCount,
                ["actionable_count"] = currentScreen.Screen.ActionableCount,
                ["new_screen"] = currentScreen.IsNew
            });

            var action = _planner.SelectNextAction(currentScreen.Screen, attemptedActionKeys);
            if (action is null)
            {
                stopReason = "no_new_actions";
                break;
            }

            attemptedActionKeys.Add(action.Key);
            var actionSelected = events.Add("action_selected", new Dictionary<string, object?>
            {
                ["screen_id"] = currentScreen.Screen.Id,
                ["action_id"] = action.Id,
                ["label"] = action.Label,
                ["source"] = action.Source,
                ["x"] = action.X,
                ["y"] = action.Y,
                ["confidence"] = action.Confidence
            });

            object? actionResult = null;
            try
            {
                actionResult = await runner.TapPointAsync(action.Label, action.X, action.Y, null, null, postTapDelayMs).ConfigureAwait(false);
                events.Add("action_result", new Dictionary<string, object?>
                {
                    ["screen_id"] = currentScreen.Screen.Id,
                    ["action_id"] = action.Id,
                    ["selected_event_id"] = actionSelected.EventId,
                    ["label"] = action.Label,
                    ["x"] = action.X,
                    ["y"] = action.Y,
                    ["post_tap_delay_ms"] = postTapDelayMs,
                    ["status"] = "passed",
                    ["result"] = actionResult
                });
            }
            catch (Exception ex)
            {
                stopReason = "action_failed";
                events.Add("action_result", new Dictionary<string, object?>
                {
                    ["screen_id"] = currentScreen.Screen.Id,
                    ["action_id"] = action.Id,
                    ["selected_event_id"] = actionSelected.EventId,
                    ["label"] = action.Label,
                    ["x"] = action.X,
                    ["y"] = action.Y,
                    ["post_tap_delay_ms"] = postTapDelayMs,
                    ["status"] = "failed",
                    ["error_type"] = ex.GetType().FullName ?? ex.GetType().Name,
                    ["message"] = ex.Message
                });
                break;
            }

            DiscoveryScreenObservation nextScreen;
            try
            {
                nextScreen = screens.Observe(await runner.GetScreenStateAsync().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                stopReason = "screen_state_failed";
                events.Add("screen_state_failed", new Dictionary<string, object?>
                {
                    ["after_action_id"] = action.Id,
                    ["error_type"] = ex.GetType().FullName ?? ex.GetType().Name,
                    ["message"] = ex.Message
                });
                break;
            }

            var transitionId = $"transition-{transitions.Count + 1:D3}";
            var changed = !string.Equals(currentScreen.Screen.Id, nextScreen.Screen.Id, StringComparison.Ordinal);
            transitions.Add(new DiscoveryMapTransition(
                transitionId,
                currentScreen.Screen.Id,
                nextScreen.Screen.Id,
                action.Id,
                changed,
                actionSelected.EventId));
            successfulActions.Add(new DiscoveryExecutedAction(action, currentScreen.Screen.Id, nextScreen.Screen.Id, changed, actionSelected.EventId));
            events.Add("transition_observed", new Dictionary<string, object?>
            {
                ["transition_id"] = transitionId,
                ["from_screen_id"] = currentScreen.Screen.Id,
                ["to_screen_id"] = nextScreen.Screen.Id,
                ["action_id"] = action.Id,
                ["changed"] = changed
            });

            if (changed)
            {
                try
                {
                    var back = await runner.KeyEventAsync("KEYCODE_BACK").ConfigureAwait(false);
                    events.Add("backtrack_result", new Dictionary<string, object?>
                    {
                        ["from_screen_id"] = nextScreen.Screen.Id,
                        ["to_screen_id"] = currentScreen.Screen.Id,
                        ["status"] = "passed",
                        ["result"] = back
                    });
                }
                catch (Exception ex)
                {
                    stopReason = "backtrack_failed";
                    events.Add("backtrack_result", new Dictionary<string, object?>
                    {
                        ["from_screen_id"] = nextScreen.Screen.Id,
                        ["to_screen_id"] = currentScreen.Screen.Id,
                        ["status"] = "failed",
                        ["error_type"] = ex.GetType().FullName ?? ex.GetType().Name,
                        ["message"] = ex.Message
                    });
                    break;
                }
            }
        }

        if (string.Equals(stopReason, "completed", StringComparison.Ordinal) && _timeProvider.GetUtcNow() >= deadline)
        {
            stopReason = "budget_expired";
        }
        else if (string.Equals(stopReason, "completed", StringComparison.Ordinal) && attemptedActionKeys.Count >= maxActions)
        {
            stopReason = "action_limit_reached";
        }

        var scenarioCandidatePath = await WriteScenarioCandidateAsync(packageName, activity, readiness, artifacts, successfulActions, postTapDelayMs).ConfigureAwait(false);
        var scenarioCandidates = new[]
        {
            new DiscoveryScenarioCandidateRecord(
                scenarioCandidatePath,
                successfulActions.Count,
                "review_required",
                successfulActions.Select(static action => action.SourceEventId).ToArray())
        };
        events.Add("scenario_candidate_generated", new Dictionary<string, object?>
        {
            ["path"] = scenarioCandidatePath,
            ["step_source_count"] = successfulActions.Count,
            ["review_status"] = "review_required"
        });

        var endedAt = _timeProvider.GetUtcNow();
        events.Add("discovery_ended", new Dictionary<string, object?>
        {
            ["stop_reason"] = stopReason,
            ["visited_screen_count"] = screens.Screens.Count,
            ["attempted_action_count"] = attemptedActionKeys.Count,
            ["duration_ms"] = (long)(endedAt - startedAt).TotalMilliseconds
        });
        await replay.PersistAsync(endedAt, stopReason, 0).ConfigureAwait(false);

        var map = new DiscoveryMap(
            ResultSchemas.DiscoveryMap,
            packageName,
            activity,
            startedAt,
            endedAt,
            (long)(endedAt - startedAt).TotalMilliseconds,
            (long)budget.TotalMilliseconds,
            maxActions,
            stopReason,
            readiness,
            screens.Screens,
            transitions,
            scenarioCandidates);

        await WriteJsonFileAsync(Path.Join(artifacts.Root, MapFileName), map).ConfigureAwait(false);
        await WriteEventsAsync(Path.Join(artifacts.Root, EventsFileName), events.Events).ConfigureAwait(false);
        await artifacts.RefreshIndexAsync().ConfigureAwait(false);

        var nextCommands = BuildNextCommands(artifacts.Root, scenarioCandidatePath);
        return new DiscoveryRunResult(
            ResultSchemas.DiscoveryResult,
            artifacts.Root,
            MapFileName,
            EventsFileName,
            [scenarioCandidatePath],
            screens.Screens.Count,
            attemptedActionKeys.Count,
            stopReason,
            nextCommands);
    }

    private async Task<string> WriteScenarioCandidateAsync(
        string packageName,
        string? activity,
        PreflightResult? readiness,
        ArtifactSession artifacts,
        IReadOnlyList<DiscoveryExecutedAction> successfulActions,
        int postTapDelayMs)
    {
        var candidateDirectory = Path.Join(artifacts.Root, ScenarioCandidateDirectory);
        _fileSystem.CreateDirectory(candidateDirectory);
        var scenarioPath = Path.Join(candidateDirectory, ScenarioCandidateFileName);
        var relativePath = Path.Join(ScenarioCandidateDirectory, ScenarioCandidateFileName);
        var steps = new List<ScenarioStep>
        {
            new("capture discovered start", "takeScreenshot", null, null, null, Label: "discovery-start")
        };

        for (var i = 0; i < successfulActions.Count; i++)
        {
            var executed = successfulActions[i];
            var stepNumber = i + 1;
            steps.Add(new ScenarioStep(
                $"tap discovered action {stepNumber}: {executed.Action.Label}",
                "tapPoint",
                null,
                null,
                null,
                Label: SanitizeLabel(executed.Action.Label),
                X: executed.Action.X,
                Y: executed.Action.Y,
                PostTapDelayMs: postTapDelayMs));
            steps.Add(new ScenarioStep(
                $"capture after discovered action {stepNumber}",
                "takeScreenshot",
                null,
                null,
                null,
                Label: $"discovery-action-{stepNumber:D2}"));
            if (executed.ChangedScreen)
            {
                steps.Add(new ScenarioStep(
                    $"backtrack after discovered action {stepNumber}",
                    "keyevent",
                    null,
                    "KEYCODE_BACK",
                    null));
            }
        }

        var scenario = new ScenarioFile(
            $"discovery candidate for {packageName}",
            steps,
            Variables: new Dictionary<string, string>
            {
                ["targetPackage"] = packageName
            },
            Tags: ["generated", "discovery", "review-required"],
            Setup:
            [
                new ScenarioStep(
                    "start app",
                    "startApp",
                    null,
                    null,
                    null,
                    Package: "${var:targetPackage}",
                    Activity: activity,
                    Wait: activity is not null)
            ],
            Teardown:
            [
                new ScenarioStep("collect discovery artifacts", "captureArtifacts", null, null, null, Label: "discovery-teardown")
            ],
            Metadata: new ScenarioMetadata(
                packageName,
                activity,
                "Generated by `luotsi discover`. Review coordinates, waits, assertions, and safety before using in CI.",
                readiness is null
                    ? null
                    : new ScenarioDeviceMetadata(readiness.Serial, readiness.Model, readiness.AndroidRelease, readiness.Sdk),
                readiness is null
                    ? null
                    : new ScenarioLayoutMetadata(readiness.DisplayWidth, readiness.DisplayHeight, readiness.DisplayOrientation)));

        await WriteJsonFileAsync(scenarioPath, scenario).ConfigureAwait(false);
        return relativePath;
    }

    private async Task WriteJsonFileAsync(string path, object value)
    {
        await using var stream = _fileSystem.OpenWrite(path);
        await JsonSerializer.SerializeAsync(stream, value, value.GetType(), AppJson.Options).ConfigureAwait(false);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(Environment.NewLine)).ConfigureAwait(false);
    }

    private async Task WriteEventsAsync(string path, IReadOnlyList<DiscoveryEvent> events)
    {
        var builder = new StringBuilder();
        foreach (var discoveryEvent in events)
        {
            builder.Append(JsonSerializer.Serialize(discoveryEvent, EventJsonOptions));
            builder.AppendLine();
        }

        await _fileSystem.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false)).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> BuildNextCommands(string artifactRoot, string scenarioCandidatePath)
    {
        var scenarioPath = Path.Join(artifactRoot, scenarioCandidatePath);
        return
        [
            $"luotsi artifacts open {Quote(artifactRoot)}",
            $"luotsi scenario-validate --file {Quote(scenarioPath)}",
            $"luotsi run --file {Quote(scenarioPath)} --device <adb serial>",
            $"luotsi replay open --artifacts {Quote(artifactRoot)} --dry-run"
        ];
    }

    private static TimeSpan ParseBudget(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeSpan.FromSeconds(DefaultBudgetSeconds);
        }

        var trimmed = value.Trim();
        var suffix = char.ToLowerInvariant(trimmed[^1]);
        if (char.IsLetter(suffix))
        {
            var numberText = trimmed[..^1];
            if (!double.TryParse(numberText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) || number <= 0)
            {
                throw new UsageException("Option --budget must be a positive duration such as 30s, 5m, or 1h.");
            }

            return suffix switch
            {
                's' => TimeSpan.FromSeconds(number),
                'm' => TimeSpan.FromMinutes(number),
                'h' => TimeSpan.FromHours(number),
                _ => throw new UsageException("Option --budget supports s, m, or h suffixes.")
            };
        }

        return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : throw new UsageException("Option --budget must be a positive duration such as 30s, 5m, or 1h.");
    }

    private static string SanitizeLabel(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        }

        var label = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(label) ? "discovered-action" : label;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Quote(string value) =>
        value.Any(static ch => char.IsWhiteSpace(ch) || ch == '"')
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
}

internal sealed class DiscoveryPlanner
{
    private static readonly string[] RiskTerms =
    [
        "delete",
        "remove",
        "sign out",
        "log out",
        "logout",
        "clear",
        "reset",
        "purchase",
        "buy",
        "pay",
        "send",
        "submit",
        "confirm",
        "allow",
        "deny",
        "uninstall",
        "discard",
        "erase"
    ];

    public DiscoveryMapAction? SelectNextAction(DiscoveryMapScreen screen, IReadOnlySet<string> attemptedActionKeys) =>
        screen.Actions.FirstOrDefault(action => !attemptedActionKeys.Contains(action.Key));

    public IReadOnlyList<DiscoveryMapAction> BuildActions(string screenId, ScreenState state)
    {
        var ordered = state.Elements
            .Where(IsCandidateElement)
            .Select(ToCandidateElement)
            .Where(static candidate => !IsRisky(candidate.Label))
            .OrderByDescending(static candidate => candidate.Text is not null)
            .ThenBy(static candidate => candidate.Top)
            .ThenBy(static candidate => candidate.Left)
            .ThenBy(static candidate => candidate.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ordered.Select((candidate, index) => new DiscoveryMapAction(
            $"{screenId}:action-{index + 1:D3}",
            $"{screenId}|{candidate.StableKey}",
            candidate.Label,
            candidate.Source,
            candidate.Text,
            candidate.ContentDescription,
            candidate.ResourceId,
            candidate.ClassName,
            candidate.X,
            candidate.Y,
            candidate.Left,
            candidate.Top,
            candidate.Right,
            candidate.Bottom,
            candidate.Text is not null || candidate.ContentDescription is not null ? "medium" : "low")).ToArray();
    }

    private static bool IsCandidateElement(ScreenElement element) =>
        element.Enabled &&
        element.Clickable &&
        element.Right > element.Left &&
        element.Bottom > element.Top &&
        (element.IsUseful || !string.IsNullOrWhiteSpace(element.ResourceId));

    private static CandidateElement ToCandidateElement(ScreenElement element)
    {
        var label = FirstNonBlank(element.Text, element.ContentDescription, LastResourceSegment(element.ResourceId), element.ClassName) ?? "unnamed";
        var source = !string.IsNullOrWhiteSpace(element.Text)
            ? "text"
            : !string.IsNullOrWhiteSpace(element.ContentDescription)
                ? "content_description"
                : !string.IsNullOrWhiteSpace(element.ResourceId)
                    ? "resource_id"
                    : "class_name";
        var stableKey = string.Join("|", new[]
        {
            NormalizeKey(element.Text),
            NormalizeKey(element.ContentDescription),
            NormalizeKey(element.ResourceId),
            NormalizeKey(element.ClassName),
            element.Left.ToString(CultureInfo.InvariantCulture),
            element.Top.ToString(CultureInfo.InvariantCulture),
            element.Right.ToString(CultureInfo.InvariantCulture),
            element.Bottom.ToString(CultureInfo.InvariantCulture)
        });
        return new CandidateElement(
            label,
            source,
            NormalizeOptional(element.Text),
            NormalizeOptional(element.ContentDescription),
            NormalizeOptional(element.ResourceId),
            NormalizeOptional(element.ClassName),
            element.CenterX,
            element.CenterY,
            element.Left,
            element.Top,
            element.Right,
            element.Bottom,
            stableKey);
    }

    private static bool IsRisky(string label) =>
        RiskTerms.Any(term => label.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string? LastResourceSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var slash = value.LastIndexOf('/');
        return slash >= 0 && slash + 1 < value.Length ? value[(slash + 1)..] : value;
    }

    private static string NormalizeKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record CandidateElement(
        string Label,
        string Source,
        string? Text,
        string? ContentDescription,
        string? ResourceId,
        string? ClassName,
        int X,
        int Y,
        int Left,
        int Top,
        int Right,
        int Bottom,
        string StableKey);
}

internal sealed class DiscoveryScreenRegistry(DiscoveryPlanner planner)
{
    private readonly DiscoveryPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    private readonly Dictionary<string, DiscoveryMapScreen> _screensBySignature = new(StringComparer.Ordinal);
    private readonly List<DiscoveryMapScreen> _screens = [];

    public IReadOnlyList<DiscoveryMapScreen> Screens => _screens;

    public DiscoveryScreenObservation Observe(ScreenState state)
    {
        var signature = CreateSignature(state);
        if (_screensBySignature.TryGetValue(signature, out var existing))
        {
            return new DiscoveryScreenObservation(existing, false);
        }

        var screenId = $"screen-{_screens.Count + 1:D3}";
        var actions = _planner.BuildActions(screenId, state);
        var screen = new DiscoveryMapScreen(
            screenId,
            signature,
            state.CapturedAt,
            state.ElementCount,
            actions.Count,
            actions);
        _screensBySignature.Add(signature, screen);
        _screens.Add(screen);
        return new DiscoveryScreenObservation(screen, true);
    }

    private static string CreateSignature(ScreenState state)
    {
        var normalized = string.Join(
            "\n",
            state.Elements
                .Select(static element => string.Join("|", [
                    Normalize(element.Text),
                    Normalize(element.ContentDescription),
                    Normalize(element.ResourceId),
                    Normalize(element.ClassName),
                    element.Enabled.ToString(),
                    element.Clickable.ToString(),
                    element.Left.ToString(CultureInfo.InvariantCulture),
                    element.Top.ToString(CultureInfo.InvariantCulture),
                    element.Right.ToString(CultureInfo.InvariantCulture),
                    element.Bottom.ToString(CultureInfo.InvariantCulture)
                ]))
                .Order(StringComparer.Ordinal));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}

internal sealed class DiscoveryEventLog(TimeProvider timeProvider)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly Action<DiscoveryEvent>? _eventSink;
    private readonly List<DiscoveryEvent> _events = [];

    public DiscoveryEventLog(TimeProvider timeProvider, Action<DiscoveryEvent>? eventSink)
        : this(timeProvider)
    {
        _eventSink = eventSink;
    }

    public IReadOnlyList<DiscoveryEvent> Events => _events;

    public DiscoveryEvent Add(string type, IReadOnlyDictionary<string, object?> data)
    {
        var discoveryEvent = new DiscoveryEvent(
            ResultSchemas.DiscoveryEvent,
            $"event-{_events.Count + 1:D4}",
            _events.Count + 1,
            _timeProvider.GetUtcNow(),
            type,
            data);
        _events.Add(discoveryEvent);
        _eventSink?.Invoke(discoveryEvent);
        return discoveryEvent;
    }
}

internal sealed class DiscoveryReplayRecorder
{
    private static readonly JsonSerializerOptions ReplayJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    private readonly SessionReplayArtifacts _replayArtifacts;

    public DiscoveryReplayRecorder(ArtifactSession artifacts, string packageName, DateTimeOffset startedAt)
    {
        _replayArtifacts = new SessionReplayArtifacts(artifacts, "discover", $"discover-{startedAt:yyyyMMddHHmmssfff}", startedAt);
        _replayArtifacts.SetTarget(packageName);
    }

    public void Record(DiscoveryEvent discoveryEvent)
    {
        _replayArtifacts.RecordSerializedEvent(JsonSerializer.Serialize(ToReplayEvent(discoveryEvent), ReplayJsonOptions));
    }

    public Task PersistAsync(DateTimeOffset endedAt, string reason, int exitCode) =>
        _replayArtifacts.PersistAsync(endedAt, reason, exitCode);

    private static DiscoveryReplayTimelineEvent ToReplayEvent(DiscoveryEvent discoveryEvent)
    {
        var status = GetString(discoveryEvent.Data, "status");
        var label = GetString(discoveryEvent.Data, "label");
        var message = GetString(discoveryEvent.Data, "message") ?? GetString(discoveryEvent.Data, "stop_reason");
        var command = GetReplayCommand(discoveryEvent.Type);
        return new DiscoveryReplayTimelineEvent(
            GetReplayType(discoveryEvent.Type),
            discoveryEvent.Timestamp,
            status,
            GetReplayAction(discoveryEvent.Type),
            command,
            label,
            message,
            ToOk(status),
            BuildReplayData(discoveryEvent, command));
    }

    private static string GetReplayType(string type) =>
        type switch
        {
            "action_result" or "backtrack_result" => "command_result",
            _ => type
        };

    private static string? GetReplayCommand(string type) =>
        type switch
        {
            "action_result" => "tap_point",
            "backtrack_result" => "keyevent",
            _ => null
        };

    private static string? GetReplayAction(string type) =>
        type switch
        {
            "discovery_started" => "discover",
            "app_started" => "startApp",
            "device_ready" => "preflight",
            "screen_observed" => "screen_state",
            "action_selected" => "tapPoint",
            "action_result" => "tapPoint",
            "transition_observed" => "screen_delta",
            "backtrack_result" => "keyevent",
            "scenario_candidate_generated" => "scenarioDraft",
            _ => null
        };

    private static IReadOnlyDictionary<string, object?> BuildReplayData(DiscoveryEvent discoveryEvent, string? command)
    {
        var data = new Dictionary<string, object?>(discoveryEvent.Data, StringComparer.Ordinal);
        if (string.Equals(command, "keyevent", StringComparison.Ordinal) && !data.ContainsKey("code"))
        {
            data["code"] = "KEYCODE_BACK";
        }

        if (string.Equals(command, "tap_point", StringComparison.Ordinal))
        {
            CopyIfMissing(data, "x", discoveryEvent.Data);
            CopyIfMissing(data, "y", discoveryEvent.Data);
            CopyIfMissing(data, "label", discoveryEvent.Data);
        }

        return data;
    }

    private static void CopyIfMissing(Dictionary<string, object?> data, string key, IReadOnlyDictionary<string, object?> source)
    {
        if (!data.ContainsKey(key) && source.TryGetValue(key, out var value))
        {
            data[key] = value;
        }
    }

    private static bool? ToOk(string? status) =>
        status switch
        {
            "passed" => true,
            "failed" => false,
            _ => null
        };

    private static string? GetString(IReadOnlyDictionary<string, object?> data, string key) =>
        data.TryGetValue(key, out var value) ? value?.ToString() : null;
}

internal sealed record DiscoveryReplayTimelineEvent(
    string Type,
    DateTimeOffset OccurredAt,
    string? Status,
    string? Action,
    string? Command,
    string? Label,
    string? Message,
    bool? Ok,
    IReadOnlyDictionary<string, object?> Data);

internal sealed record DiscoveryScreenObservation(DiscoveryMapScreen Screen, bool IsNew);

internal sealed record DiscoveryExecutedAction(
    DiscoveryMapAction Action,
    string FromScreenId,
    string ToScreenId,
    bool ChangedScreen,
    string SourceEventId);

internal sealed record DiscoveryMap(
    string Schema,
    string Package,
    string? Activity,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    long DurationMs,
    long BudgetMs,
    int MaxActions,
    string StopReason,
    PreflightResult? Device,
    IReadOnlyList<DiscoveryMapScreen> Screens,
    IReadOnlyList<DiscoveryMapTransition> Transitions,
    IReadOnlyList<DiscoveryScenarioCandidateRecord> ScenarioCandidates);

internal sealed record DiscoveryMapScreen(
    string Id,
    string Signature,
    DateTimeOffset ObservedAt,
    int ElementCount,
    int ActionableCount,
    IReadOnlyList<DiscoveryMapAction> Actions);

internal sealed record DiscoveryMapAction(
    string Id,
    string Key,
    string Label,
    string Source,
    string? Text,
    string? ContentDescription,
    string? ResourceId,
    string? ClassName,
    int X,
    int Y,
    int Left,
    int Top,
    int Right,
    int Bottom,
    string Confidence);

internal sealed record DiscoveryMapTransition(
    string Id,
    string FromScreenId,
    string ToScreenId,
    string ActionId,
    bool Changed,
    string SourceEventId);

internal sealed record DiscoveryScenarioCandidateRecord(
    string Path,
    int SourceActionCount,
    string ReviewStatus,
    IReadOnlyList<string> SourceEventIds);

internal sealed record DiscoveryEvent(
    string Schema,
    string EventId,
    int Sequence,
    DateTimeOffset Timestamp,
    string Type,
    IReadOnlyDictionary<string, object?> Data);
