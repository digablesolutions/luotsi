using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Processes;
using Luotsi.Cli.Models;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task ProcessRunner_Captures_Stdout_And_Exit_Code()
    {
        var (fileName, args) = OperatingSystem.IsWindows()
            ? ("powershell.exe", ["-NoLogo", "-NoProfile", "-Command", "[Console]::Out.Write('ok')"])
            : ("/bin/sh", new[] { "-c", "printf 'ok'" });
        var result = await new DefaultProcessRunner().RunAsync(fileName, args);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ok", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public async Task ArtifactSession_Rejects_Rooted_Or_Nested_Artifact_Names()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["devices"]), fileSystem, timeProvider);

        await Assert.ThrowsAsync<UsageException>(() => session.WriteTextAsync("../escape.txt", "bad"));
        await Assert.ThrowsAsync<UsageException>(() => session.WriteJsonAsync("/tmp/escape.json", new { ok = true }));
    }

    [Fact]
    public void ArtifactSession_Create_Sanitizes_Command_Segment()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var constructor = typeof(CliOptions).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [typeof(string)],
            modifiers: null);
        var options = Assert.IsType<CliOptions>(constructor?.Invoke(["../escape"]));

        var session = ArtifactSession.Create(options, fileSystem, timeProvider);

        Assert.Equal(Path.Join("/tmp", "luotsi", "20260518-100000-escape"), session.Root);
    }

    [Fact]
    public async Task ArtifactSession_Writes_Markdown_Index_For_Text_And_Json_Artifacts()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["screen-state"]), fileSystem, timeProvider);

        await session.WriteTextAsync("logcat.txt", "log");
        await session.WriteJsonAsync("screen-state.json", new { element_count = 1 });
        await session.WriteTextAsync("hierarchy.xml", "<hierarchy />");

        var index = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.md"));

        Assert.Contains("# Luotsi Artifacts", index, StringComparison.Ordinal);
        Assert.Contains("## Logs", index, StringComparison.Ordinal);
        Assert.Contains("- [logcat.txt](logcat.txt)", index, StringComparison.Ordinal);
        Assert.Contains("## Screen State", index, StringComparison.Ordinal);
        Assert.Contains("- [screen-state.json](screen-state.json)", index, StringComparison.Ordinal);
        Assert.Contains("## Hierarchy", index, StringComparison.Ordinal);
        Assert.Contains("- [hierarchy.xml](hierarchy.xml)", index, StringComparison.Ordinal);
        Assert.DoesNotContain("[index.md]", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArtifactSession_Writes_Html_Index_For_Browsing_Artifacts()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider);

        await session.WriteTextAsync("logcat.txt", "log");
        await session.WriteJsonAsync("run-report.json", new { schema = "luotsi-scenario-run-report.v1", status = "passed", total = 2, passed = 2, failed = 0, durationMs = 1234 });
        await session.WriteTextAsync("events.jsonl", """
        {"type":"scenario_run_started"}
        {"type":"scenario_started"}
        {"type":"scenario_run_ended","status":"passed"}
        """);
        await using (var screenshot = fileSystem.OpenWrite(Path.Join(session.Root, "home shot.png")))
        {
            await screenshot.WriteAsync(new byte[] { 1, 2, 3 });
        }

        await session.RefreshIndexAsync();

        var index = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.html"));

        Assert.Contains("<title>Luotsi Artifacts</title>", index, StringComparison.Ordinal);
        Assert.Contains("<h2>Screenshots</h2>", index, StringComparison.Ordinal);
        Assert.Contains("href=\"home%20shot.png\"", index, StringComparison.Ordinal);
        Assert.Contains("<h2>Reports</h2>", index, StringComparison.Ordinal);
        Assert.Contains("run-report.json", index, StringComparison.Ordinal);
        Assert.Contains("status=passed | total=2 | passed=2 | failed=0 | duration_ms=1234", index, StringComparison.Ordinal);
        Assert.Contains("events=3 | terminal=passed", index, StringComparison.Ordinal);
        Assert.Contains("<h2>Logs</h2>", index, StringComparison.Ordinal);
        Assert.DoesNotContain("index.md", index, StringComparison.Ordinal);
        Assert.DoesNotContain("index.html", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArtifactSession_Html_Index_Summarizes_Bounded_Jsonl_Tail()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider);
        var events = string.Join(Environment.NewLine, Enumerable.Range(0, 510).Select(static index => index == 509
            ? """{"type":"scenario_run_ended","status":"passed"}"""
            : """{"type":"scenario_step_passed"}"""));

        await session.WriteTextAsync("events.jsonl", events);
        await session.RefreshIndexAsync();

        var index = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.html"));

        Assert.Contains("events_sampled=500 | terminal=passed", index, StringComparison.Ordinal);
        Assert.DoesNotContain("events=510", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArtifactSession_Indexes_Replay_Session_Summary_For_Ci_And_Browsing()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider);

        await session.WriteTextAsync("session-timeline.jsonl", """
        {"type":"view_started","session_id":"view-session","started_at":"2026-05-18T10:00:00Z"}
        {"type":"view_reconnect_requested","session_id":"view-session","occurred_at":"2026-05-18T10:00:01Z","device":"192.168.0.134:5555","source":"toolbar","reason":"manual_retry"}
        {"type":"view_share_client_connected","session_id":"view-session","occurred_at":"2026-05-18T10:00:02Z","endpoint":"127.0.0.1:9000","remote_endpoint":"10.0.0.25:40122","observer_count":1,"reason":"observer_joined"}
        {"type":"view_stats","session_id":"view-session","observed_at":"2026-05-18T10:00:03Z","stats":{"decoded_frames":120,"presented_frames":118,"dropped_frames":2,"decode_fps":29.5,"present_fps":29.0,"end_to_end_latency_ms":142}}
        {"type":"view_diagnostic","session_id":"view-session","occurred_at":"2026-05-18T10:00:03Z","category":"transport","message":"Unexpected end of stream"}
        {"type":"view_error","session_id":"view-session","occurred_at":"2026-05-18T10:00:03Z","error":{"category":"transport","message":"Unexpected end of stream"}}
        {"type":"view_ended","session_id":"view-session","ended_at":"2026-05-18T10:00:03Z","reason":"error"}
        """);
        await session.WriteJsonAsync("session-replay.json", new
        {
            schema = ResultSchemas.SessionReplay,
            sessionKind = "view",
            sessionId = "view-session",
            startedAt = DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            endedAt = DateTimeOffset.Parse("2026-05-18T10:00:03Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            reason = "error",
            exitCode = 1,
            target = "192.168.0.134:5555",
            timelineFileName = "session-timeline.jsonl",
            eventCount = 7,
            eventTypes = new[] { "view_started", "view_reconnect_requested", "view_share_client_connected", "view_stats", "view_diagnostic", "view_error", "view_ended" }
        });

        var markdownIndex = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.md"));
        var htmlIndex = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.html"));

        Assert.Contains("## Replay Sessions", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("### view 192.168.0.134:5555", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("reason=error | exit_code=1 | events=7 | target=192.168.0.134:5555", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("[metadata](session-replay.json) | [timeline](session-timeline.jsonl)", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("Failure timeline:", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("view_reconnect_requested | device=192.168.0.134:5555 | source=toolbar | reason=manual_retry", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("view_share_client_connected | endpoint=127.0.0.1:9000 | remote_endpoint=10.0.0.25:40122 | observer_count=1 | reason=observer_joined", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("view_stats | decoded_frames=120 | presented_frames=118 | dropped_frames=2 | decode_fps=29.5 | present_fps=29.0 | end_to_end_latency_ms=142", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("view_error | error=transport: Unexpected end of stream", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("## Reports", markdownIndex, StringComparison.Ordinal);

        Assert.Contains("<h2>Replay Sessions</h2>", htmlIndex, StringComparison.Ordinal);
        Assert.Contains("<strong>view 192.168.0.134:5555</strong>", htmlIndex, StringComparison.Ordinal);
        Assert.Contains(">metadata</a> | <a href=\"session-timeline.jsonl\">timeline</a>", htmlIndex, StringComparison.Ordinal);
        Assert.Contains("Failure timeline", htmlIndex, StringComparison.Ordinal);
        Assert.Contains("view_reconnect_requested | device=192.168.0.134:5555 | source=toolbar | reason=manual_retry", htmlIndex, StringComparison.Ordinal);
        Assert.Contains("view_share_client_connected | endpoint=127.0.0.1:9000 | remote_endpoint=10.0.0.25:40122 | observer_count=1 | reason=observer_joined", htmlIndex, StringComparison.Ordinal);
        Assert.Contains("view_stats | decoded_frames=120 | presented_frames=118 | dropped_frames=2 | decode_fps=29.5 | present_fps=29.0 | end_to_end_latency_ms=142", htmlIndex, StringComparison.Ordinal);
        Assert.Contains("view_error | error=transport: Unexpected end of stream", htmlIndex, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionReplayArtifacts_Stream_Timeline_Entries_And_Tag_Invalid_Json()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider);
        var replay = new SessionReplayArtifacts(session, "view", "view-session", timeProvider.GetUtcNow());

        replay.RecordSerializedEvent("""{"type":"view_started"}""");
        replay.RecordSerializedEvent("not-json");

        var timelinePath = Path.Join(session.Root, SessionReplayArtifacts.TimelineFileName);
        var timeline = await fileSystem.ReadAllTextAsync(timelinePath);
        Assert.Contains("{\"type\":\"view_started\"}", timeline, StringComparison.Ordinal);
        Assert.Contains("not-json", timeline, StringComparison.Ordinal);

        await replay.PersistAsync(timeProvider.GetUtcNow().AddSeconds(2), "stream_ended", 0);

        using var metadata = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(session.Root, SessionReplayArtifacts.MetadataFileName)));
        Assert.Equal(2, metadata.RootElement.GetProperty("eventCount").GetInt32());
        var eventTypes = metadata.RootElement.GetProperty("eventTypes").EnumerateArray().Select(static value => value.GetString()).ToArray();
        Assert.Contains("view_started", eventTypes);
        Assert.Contains("invalid-json", eventTypes);
    }

    [Fact]
    public async Task ArtifactSession_Index_Summarizes_Failure_Capsule_Report()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider);

        await session.WriteJsonAsync("failure-capsule.json", new
        {
            schema = ResultSchemas.FailureCapsule,
            generatedAt = DateTimeOffset.Parse("2026-05-18T10:00:03Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            path = "/tmp/scenario.json",
            status = "failed",
            replayMetadataPath = "session-replay.json",
            replayTimelinePath = "session-timeline.jsonl",
            reports = new
            {
                jsonPath = "/tmp/report.json",
                junitPath = "/tmp/junit.xml"
            },
            scenarios = new object[]
            {
                new
                {
                    scenario = "broken scenario",
                    scenarioId = "/tmp/scenario.json::broken scenario",
                    status = "failed",
                    file = "/tmp/scenario.json",
                    failedStep = new
                    {
                        index = 1,
                        name = "waitVisible",
                        action = "waitVisible",
                        phase = "main"
                    },
                    artifacts = new object[]
                    {
                        new { kind = "screenshot", path = "failure.png", stepIndex = 1, stepName = "waitVisible" },
                        new { kind = "logcat", path = "failure-logcat.txt", stepIndex = 1, stepName = "waitVisible" }
                    }
                }
            },
            screenshots = new object[]
            {
                new { kind = "screenshot", path = "failure.png", stepIndex = 1, stepName = "waitVisible" }
            },
            logcat = new object[]
            {
                new { kind = "logcat", path = "failure-logcat.txt", stepIndex = 1, stepName = "waitVisible" }
            },
            hierarchies = new object[]
            {
                new { kind = "hierarchy", path = "failure-hierarchy.xml", stepIndex = 1, stepName = "waitVisible" }
            },
            screenStates = new object[]
            {
                new { kind = "screen_state", path = "failure-screen-state.json", stepIndex = 1, stepName = "waitVisible" }
            },
            failureBundles = new object[]
            {
                new
                {
                    path = "failure.json",
                    scenario = "broken scenario",
                    scenarioId = "/tmp/scenario.json::broken scenario",
                    file = "/tmp/scenario.json",
                    failedStep = new
                    {
                        index = 1,
                        name = "waitVisible",
                        action = "waitVisible",
                        phase = "main"
                    },
                    artifacts = new object[]
                    {
                        new { kind = "screenshot", path = "failure.png", stepIndex = 1, stepName = "waitVisible" }
                    }
                }
            }
        });

        var markdownIndex = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.md"));
        var htmlIndex = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.html"));

        Assert.Contains("- [failure-capsule.json](failure-capsule.json)", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("status=failed | scenarios=1 | failed_scenarios=broken scenario | failed_steps=waitVisible | screenshots=1 | logcat=1 | hierarchies=1 | screen_states=1 | failure_bundles=1", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("href=\"failure-capsule.json\"", htmlIndex, StringComparison.Ordinal);
        Assert.Contains("status=failed | scenarios=1 | failed_scenarios=broken scenario | failed_steps=waitVisible | screenshots=1 | logcat=1 | hierarchies=1 | screen_states=1 | failure_bundles=1", htmlIndex, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArtifactSession_Index_Summarizes_Replay_Capsule_And_Scenario_Draft()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["replay"]), fileSystem, timeProvider);

        await session.WriteJsonAsync("replay-capsule-summary.json", new
        {
            schema = ResultSchemas.ReplayCapsule,
            artifactRoot = session.Root,
            sessionCount = 2,
            failureCount = 1,
            hasFailureCapsule = true,
            scenarioDraftAvailable = true,
            scenarioDraftReason = "Found command_result:tap_text source in inspect/session-timeline.jsonl."
        });
        await session.WriteJsonAsync("scenario-draft-summary.json", new
        {
            schema = ResultSchemas.ScenarioDraft,
            confidence = "medium",
            source_summaries = new[] { new { source = "inspect_command", step_count = 2, normalization_count = 0 } },
            warnings = new[] { "Review generated selectors before CI use." },
            normalizations = new[] { new { kind = "duplicate_wait" } },
            scenario = new
            {
                name = "draft",
                steps = new object[]
                {
                    new { action = "tapText", text = "Sign in" },
                    new { action = "waitVisible", text = "Welcome" }
                }
            }
        });

        var markdownIndex = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.md"));
        var htmlIndex = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.html"));

        Assert.Contains("## Replay", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("- [replay-capsule-summary.json](replay-capsule-summary.json)", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("- [scenario-draft-summary.json](scenario-draft-summary.json)", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("session_count=2 | failure_count=1 | scenario_draft_available=true | scenario_draft_reason=Found command_result:tap_text source in inspect/session-timeline.jsonl.", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("confidence=medium | source_summaries=1 | steps=2 | warnings=1 | normalizations=1", markdownIndex, StringComparison.Ordinal);
        Assert.Contains("<h2>Replay</h2>", htmlIndex, StringComparison.Ordinal);
        Assert.Contains("session_count=2 | failure_count=1 | scenario_draft_available=true | scenario_draft_reason=Found command_result:tap_text source in inspect/session-timeline.jsonl.", htmlIndex, StringComparison.Ordinal);
        Assert.Contains("confidence=medium | source_summaries=1 | steps=2 | warnings=1 | normalizations=1", htmlIndex, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArtifactSession_RefreshIndex_Includes_Pulled_Media_Files()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["record"]), fileSystem, timeProvider);
        await using (var screenshot = fileSystem.OpenWrite(Path.Join(session.Root, "demo shot.png")))
        {
            await screenshot.WriteAsync(new byte[] { 1, 2, 3 });
        }

        await using (var recording = fileSystem.OpenWrite(Path.Join(session.Root, "demo.mp4")))
        {
            await recording.WriteAsync(new byte[] { 4, 5, 6 });
        }

        await session.RefreshIndexAsync();

        var index = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.md"));

        Assert.Contains("## Screenshots", index, StringComparison.Ordinal);
        Assert.Contains("- [demo shot.png](demo%20shot.png)", index, StringComparison.Ordinal);
        Assert.Contains("## Recordings", index, StringComparison.Ordinal);
        Assert.Contains("- [demo.mp4](demo.mp4)", index, StringComparison.Ordinal);
    }


}
