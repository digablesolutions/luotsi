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
        var options = CliOptions.Parse(["discover", "--device", "SER", "--package", "dev.luotsi.app", "--budget", "30s"]);

        Assert.Equal("discover", options.Command);
        Assert.Equal("SER", options.Get("device"));
        Assert.Equal("dev.luotsi.app", options.Get("package"));
        Assert.Equal("30s", options.Get("budget"));
    }

    [Fact]
    public void Planner_Selects_Unvisited_Safe_Actions_Deterministically()
    {
        var planner = new DiscoveryPlanner();
        var screen = new DiscoveryMapScreen(
            "screen-001",
            "signature",
            DateTimeOffset.Parse("2026-05-31T08:00:00Z"),
            3,
            0,
            planner.BuildActions("screen-001", new ScreenState(
                DateTimeOffset.Parse("2026-05-31T08:00:00Z"),
                3,
                [
                    Element("Delete account", 0, 0, 100, 100),
                    Element("Open details", 0, 120, 100, 220),
                    Element("Settings", 0, 240, 100, 340)
                ])));

        var first = planner.SelectNextAction(screen, new HashSet<string>(StringComparer.Ordinal));
        var attempted = new HashSet<string>(StringComparer.Ordinal) { first!.Key };
        var second = planner.SelectNextAction(screen, attempted);

        Assert.Equal("Open details", first.Label);
        Assert.Equal("Settings", second!.Label);
        Assert.DoesNotContain(screen.Actions, action => action.Label.Contains("Delete", StringComparison.OrdinalIgnoreCase));
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

        using var scenario = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(scenarioPath));
        Assert.Equal(750, scenario.RootElement.GetProperty("steps")[1].GetProperty("postTapDelayMs").GetInt32());

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
