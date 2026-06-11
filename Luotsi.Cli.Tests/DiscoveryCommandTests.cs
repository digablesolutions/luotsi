using System.Text.Json;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Cli.Discovery;
using Luotsi.Cli.Models;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class DiscoveryCommandTests
{
    [Fact]
    public void Parse_Recognizes_Discover_Command()
    {
        var options = CliOptions.Parse([
            "discover",
            "--device",
            "SER",
            "--package",
            "dev.luotsi.app",
            "--budget",
            "30s",
            "--max-depth",
            "2",
            "--allow-text",
            "Open",
            "--deny-text",
            "Delete",
            "--deny-resource-id",
            "logout",
            "--deny-class",
            "android.widget.Switch"
        ]);

        Assert.Equal("discover", options.Command);
        Assert.Equal("SER", options.Get("device"));
        Assert.Equal("dev.luotsi.app", options.Get("package"));
        Assert.Equal("30s", options.Get("budget"));
        Assert.Equal(2, options.Int("max-depth", 0));
        Assert.Equal("Open", options.Get("allow-text"));
        Assert.Equal("Delete", options.Get("deny-text"));
        Assert.Equal("logout", options.Get("deny-resource-id"));
        Assert.Equal("android.widget.Switch", options.Get("deny-class"));
    }

    [Fact]
    public void Planner_Selects_Unvisited_Safe_Actions_Deterministically()
    {
        var planner = new DiscoveryPlanner();
        var actionBuild = planner.BuildActions("screen-001", new ScreenState(
            DateTimeOffset.Parse("2026-05-31T08:00:00Z"),
            3,
            [
                Element("Delete account", 0, 0, 100, 100),
                Element("Open details", 0, 120, 100, 220),
                Element("Settings", 0, 240, 100, 340)
            ]));
        var screen = new DiscoveryMapScreen(
            "screen-001",
            "signature",
            DateTimeOffset.Parse("2026-05-31T08:00:00Z"),
            3,
            actionBuild.Actions.Count,
            actionBuild.SkippedActions.Count,
            actionBuild.Actions,
            actionBuild.SkippedActions);

        var first = planner.SelectNextAction(screen, new HashSet<string>(StringComparer.Ordinal));
        var attempted = new HashSet<string>(StringComparer.Ordinal) { first!.Key };
        var second = planner.SelectNextAction(screen, attempted);

        Assert.Equal("Open details", first.Label);
        Assert.Equal("Settings", second!.Label);
        Assert.DoesNotContain(screen.Actions, action => action.Label.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(screen.SkippedActions, action => action is { Label: "Delete account", Reason: "built_in_risky_text" });
    }

    [Fact]
    public void Planner_Applies_Discovery_Policy_Before_Action_Ranking()
    {
        var planner = new DiscoveryPlanner();
        var policy = new DiscoveryPolicy(
            ["Open", "Settings", "Sign in"],
            ["Sign in"],
            ["settings"],
            ["android.widget.Switch"],
            DiscoveryPlanner.DefaultDenyTextTerms);
        var actionBuild = planner.BuildActions("screen-001", new ScreenState(
            DateTimeOffset.Parse("2026-05-31T08:00:00Z"),
            5,
            [
                Element("Open details", 0, 200, 100, 300),
                Element("Sign in", 0, 0, 100, 100),
                Element("Settings", 0, 100, 100, 200),
                new ScreenElement(null, "Toggle email", "dev.luotsi.app:id/email_toggle", "android.widget.Switch", true, true, 0, 300, 100, 400),
                Element("Help", 0, 400, 100, 500)
            ]),
            policy);

        Assert.Equal(["Open details"], actionBuild.Actions.Select(static action => action.Label).ToArray());
        Assert.Equal(4, actionBuild.SkippedActions.Count);
        Assert.Contains(actionBuild.SkippedActions, action => action is { Label: "Sign in", Reason: "text_denied", MatchedPattern: "Sign in" });
        Assert.Contains(actionBuild.SkippedActions, action => action is { Label: "Settings", Reason: "resource_id_denied", MatchedPattern: "settings" });
        Assert.Contains(actionBuild.SkippedActions, action => action is { Label: "Toggle email", Reason: "class_denied", MatchedPattern: "android.widget.Switch" });
        Assert.Contains(actionBuild.SkippedActions, action => action is { Label: "Help", Reason: "text_not_allowed", MatchedPattern: null });
    }

    [Fact]
    public async Task Discover_Writes_Map_Events_And_Valid_Scenario_Candidate()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-31T08:00:00Z"));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(
            Screen("Home", Element("Open details", 10, 10, 210, 110)),
            Screen("Details", Element("Details title", 10, 10, 210, 110, clickable: false)),
            Screen("Home", Element("Open details", 10, 10, 210, 110)));
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Delay = new FakeDelay(timeProvider)
        });

        var exitCode = await app.RunAsync([
            "discover",
            "--device",
            "SER",
            "--package",
            "dev.luotsi.app",
            "--activity",
            ".MainActivity",
            "--artifacts",
            "/tmp/discovery",
            "--budget",
            "30s",
            "--max-actions",
            "1",
            "--post-tap-delay-ms",
            "750"
        ]);
        using var envelope = console.ParseSingleOutputAsJson();
        var data = envelope.RootElement.GetProperty("data");
        var artifactRoot = data.GetProperty("artifact_root").GetString()!;
        var scenarioRelativePath = data.GetProperty("scenario_candidate_paths")[0].GetString()!;
        var scenarioPath = Path.Join(artifactRoot, scenarioRelativePath);

        Assert.Equal(0, exitCode);
        Assert.Equal(ResultSchemas.DiscoveryResult, data.GetProperty("schema").GetString());
        Assert.Equal("action_limit_reached", data.GetProperty("stop_reason").GetString());
        Assert.Equal(2, data.GetProperty("visited_screen_count").GetInt32());
        Assert.Equal(1, data.GetProperty("attempted_action_count").GetInt32());
        Assert.Equal(2, data.GetProperty("max_depth").GetInt32());
        var nextCommands = data.GetProperty("next_commands").EnumerateArray().Select(static command => command.GetString()).ToArray();
        Assert.Equal($"luotsi replay packet --artifacts {artifactRoot}", nextCommands[0]);
        Assert.Equal($"luotsi replay packet --artifacts {artifactRoot} --check", nextCommands[1]);
        Assert.Contains(nextCommands, command => command == $"luotsi scenario-validate --file {scenarioPath}");
        Assert.Contains(nextCommands, command => command == $"luotsi replay open --artifacts {artifactRoot} --dry-run");
        Assert.Contains(nextCommands, command => command == $"luotsi artifacts open {artifactRoot}");
        Assert.Single(host.TapPointRequests);
        Assert.Contains("KEYCODE_BACK", host.KeyEventRequests);
        Assert.True(fileSystem.FileExists(Path.Join(artifactRoot, "discovery-map.json")));
        Assert.True(fileSystem.FileExists(Path.Join(artifactRoot, "discovery-events.jsonl")));
        Assert.True(fileSystem.FileExists(Path.Join(artifactRoot, "session-replay.json")));
        Assert.True(fileSystem.FileExists(Path.Join(artifactRoot, "session-timeline.jsonl")));
        Assert.True(fileSystem.FileExists(scenarioPath));

        using var map = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(artifactRoot, "discovery-map.json")));
        Assert.Equal(ResultSchemas.DiscoveryMap, map.RootElement.GetProperty("schema").GetString());
        Assert.Equal("dev.luotsi.app", map.RootElement.GetProperty("package").GetString());
        Assert.Equal(2, map.RootElement.GetProperty("screens").GetArrayLength());
        Assert.Equal(1, map.RootElement.GetProperty("transitions").GetArrayLength());

        var scenarioJson = await fileSystem.ReadAllTextAsync(scenarioPath);
        using var scenario = JsonDocument.Parse(scenarioJson);
        Assert.Equal(750, scenario.RootElement.GetProperty("steps")[1].GetProperty("postTapDelayMs").GetInt32());
        Assert.DoesNotContain("${var:targetPackage}", scenarioJson, StringComparison.Ordinal);
        Assert.Equal("dev.luotsi.app", scenario.RootElement.GetProperty("setup")[0].GetProperty("package").GetString());
        Assert.Equal(".MainActivity", scenario.RootElement.GetProperty("setup")[0].GetProperty("activity").GetString());
        if (scenario.RootElement.TryGetProperty("variables", out var variables) && variables.ValueKind == JsonValueKind.Object)
        {
            Assert.DoesNotContain(variables.EnumerateObject(), property => property.NameEquals("targetPackage"));
        }

        var events = (await fileSystem.ReadAllTextAsync(Path.Join(artifactRoot, "discovery-events.jsonl")))
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(events, line => line.Contains("\"type\":\"screen_observed\"", StringComparison.Ordinal));
        Assert.Contains(events, line => line.Contains("\"type\":\"scenario_candidate_generated\"", StringComparison.Ordinal));

        console.OutputLines.Clear();
        var validateExitCode = await app.RunAsync(["scenario-validate", "--file", scenarioPath]);
        using var validateEnvelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, validateExitCode);
        Assert.Equal("validated", validateEnvelope.RootElement.GetProperty("data").GetProperty("status").GetString());

        console.OutputLines.Clear();
        var timelineExitCode = await app.RunAsync([
            "replay",
            "timeline",
            "--artifacts",
            artifactRoot,
            "--type",
            "command_result"
        ]);
        using var timelineEnvelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, timelineExitCode);
        Assert.Equal(ResultSchemas.ReplayTimeline, timelineEnvelope.RootElement.GetProperty("data").GetProperty("schema").GetString());
        Assert.Equal(2, timelineEnvelope.RootElement.GetProperty("data").GetProperty("event_count").GetInt32());

        console.OutputLines.Clear();
        var replayDraftPath = Path.Join(artifactRoot, "scenario-candidates", "replay-draft.json");
        var replayDraftExitCode = await app.RunAsync([
            "replay",
            "scenario-draft",
            "--artifacts",
            artifactRoot,
            "--output",
            replayDraftPath
        ]);
        using var replayDraftEnvelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, replayDraftExitCode);
        Assert.True(fileSystem.FileExists(replayDraftPath));
        Assert.Contains(
            replayDraftEnvelope.RootElement.GetProperty("data").GetProperty("suggestions").EnumerateArray(),
            suggestion => suggestion.GetProperty("kind").GetString() == "coordinate");
    }

    [Fact]
    public async Task Discover_Traverses_Frontier_Until_MaxDepth()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-31T08:00:00Z"));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(
            Screen("Home", Element("Open details", 10, 10, 210, 110)),
            Screen("Details", Element("Open child", 10, 120, 210, 220)),
            Screen("Child", Element("Child title", 10, 10, 210, 110, clickable: false)));
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Delay = new FakeDelay(timeProvider)
        });

        var exitCode = await app.RunAsync([
            "discover",
            "--device",
            "SER",
            "--package",
            "dev.luotsi.app",
            "--artifacts",
            "/tmp/discovery",
            "--budget",
            "30s",
            "--max-actions",
            "2",
            "--max-depth",
            "2"
        ]);
        using var envelope = console.ParseSingleOutputAsJson();
        var data = envelope.RootElement.GetProperty("data");
        var artifactRoot = data.GetProperty("artifact_root").GetString()!;
        var scenarioPath = Path.Join(artifactRoot, data.GetProperty("scenario_candidate_paths")[0].GetString()!);

        Assert.Equal(0, exitCode);
        Assert.Equal("action_limit_reached", data.GetProperty("stop_reason").GetString());
        Assert.Equal(3, data.GetProperty("visited_screen_count").GetInt32());
        Assert.Equal(2, data.GetProperty("attempted_action_count").GetInt32());
        Assert.Equal(["Open details", "Open child"], host.TapPointRequests.Select(static request => request.Label ?? string.Empty).ToArray());
        Assert.Equal(["KEYCODE_BACK", "KEYCODE_BACK"], host.KeyEventRequests);

        using var map = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(artifactRoot, "discovery-map.json")));
        Assert.Equal(2, map.RootElement.GetProperty("maxDepth").GetInt32());
        Assert.Equal(3, map.RootElement.GetProperty("screens").GetArrayLength());
        Assert.Equal(2, map.RootElement.GetProperty("transitions").GetArrayLength());

        using var scenario = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(scenarioPath));
        var scenarioActions = scenario.RootElement.GetProperty("steps")
            .EnumerateArray()
            .Select(static step => step.GetProperty("action").GetString()!)
            .ToArray();
        Assert.Equal(
            ["takeScreenshot", "tapPoint", "takeScreenshot", "tapPoint", "takeScreenshot", "keyevent", "keyevent"],
            scenarioActions);

        var events = (await fileSystem.ReadAllTextAsync(Path.Join(artifactRoot, "discovery-events.jsonl")))
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, events.Count(static line => line.Contains("\"type\":\"backtrack_result\"", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Discover_Persists_Policy_And_Records_Skipped_Candidates()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-31T08:00:00Z"));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(
            Screen(
                "Home",
                Element("Open details", 10, 10, 210, 110),
                Element("Sign in", 10, 120, 210, 220),
                Element("Settings", 10, 230, 210, 330)),
            Screen("Details", Element("Details title", 10, 10, 210, 110, clickable: false)),
            Screen(
                "Home",
                Element("Open details", 10, 10, 210, 110),
                Element("Sign in", 10, 120, 210, 220),
                Element("Settings", 10, 230, 210, 330)));
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Delay = new FakeDelay(timeProvider)
        });

        var exitCode = await app.RunAsync([
            "discover",
            "--device",
            "SER",
            "--package",
            "dev.luotsi.app",
            "--artifacts",
            "/tmp/discovery",
            "--budget",
            "30s",
            "--max-actions",
            "1",
            "--allow-text",
            "Open,Settings,Sign in",
            "--deny-text",
            "Sign in",
            "--deny-resource-id",
            "Settings"
        ]);
        using var envelope = console.ParseSingleOutputAsJson();
        var data = envelope.RootElement.GetProperty("data");
        var artifactRoot = data.GetProperty("artifact_root").GetString()!;

        Assert.Equal(0, exitCode);
        Assert.Equal(["Open details"], host.TapPointRequests.Select(static request => request.Label ?? string.Empty).ToArray());

        using var map = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(artifactRoot, "discovery-map.json")));
        var policy = map.RootElement.GetProperty("policy");
        Assert.Equal(["Open", "Settings", "Sign in"], policy.GetProperty("allowText").EnumerateArray().Select(static value => value.GetString()!).ToArray());
        Assert.Equal(["Sign in"], policy.GetProperty("denyText").EnumerateArray().Select(static value => value.GetString()!).ToArray());
        Assert.Equal(["Settings"], policy.GetProperty("denyResourceId").EnumerateArray().Select(static value => value.GetString()!).ToArray());
        Assert.Contains(policy.GetProperty("builtInDenyText").EnumerateArray(), value => value.GetString() == "delete");

        var firstScreen = map.RootElement.GetProperty("screens")[0];
        Assert.Equal(1, firstScreen.GetProperty("actionableCount").GetInt32());
        Assert.Equal(2, firstScreen.GetProperty("skippedActionableCount").GetInt32());
        Assert.Equal(["Open details"], firstScreen.GetProperty("actions").EnumerateArray().Select(static action => action.GetProperty("label").GetString()!).ToArray());
        Assert.Equal(
            ["text_denied", "resource_id_denied"],
            firstScreen.GetProperty("skippedActions").EnumerateArray().Select(static action => action.GetProperty("reason").GetString()!).ToArray());

        var events = await fileSystem.ReadAllTextAsync(Path.Join(artifactRoot, "discovery-events.jsonl"));
        Assert.Contains("\"type\":\"action_skipped\"", events, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"text_denied\"", events, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"resource_id_denied\"", events, StringComparison.Ordinal);
        Assert.Contains("\"policy\":", events, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discover_Records_Action_Failure_As_StopReason()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-31T08:00:00Z"));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var host = new FakeDeviceHost(Screen("Home", Element("Open details", 10, 10, 210, 110)))
        {
            TapPointException = new InvalidOperationException("tap failed")
        };
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Delay = new FakeDelay(timeProvider)
        });

        var exitCode = await app.RunAsync([
            "discover",
            "--device",
            "SER",
            "--package",
            "dev.luotsi.app",
            "--artifacts",
            "/tmp/discovery",
            "--budget",
            "30s",
            "--max-actions",
            "1"
        ]);
        using var envelope = console.ParseSingleOutputAsJson();
        var data = envelope.RootElement.GetProperty("data");
        var artifactRoot = data.GetProperty("artifact_root").GetString()!;

        Assert.Equal(0, exitCode);
        Assert.Equal("action_failed", data.GetProperty("stop_reason").GetString());

        var events = await fileSystem.ReadAllTextAsync(Path.Join(artifactRoot, "discovery-events.jsonl"));
        Assert.Contains("\"status\":\"failed\"", events, StringComparison.Ordinal);
        Assert.Contains("tap failed", events, StringComparison.Ordinal);
    }

    private static ScreenState Screen(string label, params ScreenElement[] elements) =>
        new(DateTimeOffset.Parse("2026-05-31T08:00:00Z"), elements.Length, elements);

    private static ScreenElement Element(string text, int left, int top, int right, int bottom, bool clickable = true) =>
        new(text, null, $"dev.luotsi.app:id/{text.Replace(' ', '_')}", "android.widget.Button", true, clickable, left, top, right, bottom);
}
