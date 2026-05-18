using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Infrastructure.Processes;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task RunScenarioAsync_Parses_Valid_Steps_And_Returns_Passed_Status()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
        var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
        var scenarioPath = "/tmp/scenario.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "basic",
          "steps": [
            { "name": "pause", "action": "sleep", "milliseconds": 250 },
            { "name": "press back", "action": "keyevent", "code": "KEYCODE_BACK" }
          ]
        }
        """);

        var result = await scenarios.RunAsync(scenarioPath);
        var envelope = SerializeToJsonElement(result);

        Assert.Equal("basic", envelope.GetProperty("scenario").GetString());
        Assert.Equal("passed", envelope.GetProperty("status").GetString());
        Assert.Equal(0, envelope.GetProperty("timing").GetProperty("prologue_ms").GetInt32());
        Assert.Equal(250, envelope.GetProperty("timing").GetProperty("steps_ms").GetInt32());
        Assert.Equal(2, envelope.GetProperty("steps").GetArrayLength());
        Assert.Equal("sleep", envelope.GetProperty("steps")[0].GetProperty("action").GetString());
                Assert.Equal(250, envelope.GetProperty("steps")[0].GetProperty("timing").GetProperty("harness_delay_ms").GetInt32());
                Assert.Equal(250, envelope.GetProperty("steps")[0].GetProperty("timing").GetProperty("configured_delay_ms").GetInt32());
                Assert.Equal(0, envelope.GetProperty("steps")[0].GetProperty("timing").GetProperty("non_delay_ms").GetInt32());
        Assert.Equal("keyevent", envelope.GetProperty("steps")[1].GetProperty("action").GetString());
                Assert.Equal(0, envelope.GetProperty("steps")[1].GetProperty("timing").GetProperty("harness_delay_ms").GetInt32());
    }


    [Fact]
    public async Task ScenarioList_Filters_By_Tag_Name_And_Action()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/login.json", """
        {
          "name": "login smoke",
          "tags": ["smoke", "auth"],
          "steps": [
            { "action": "waitVisible", "text": "Sign in" }
          ]
        }
        """);
        fileSystem.AddFile("/tmp/scenarios/logout.json", """
        {
          "name": "logout regression",
          "tags": ["regression", "auth"],
          "steps": [
            { "action": "tapText", "text": "Sign out" }
          ]
        }
        """);
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            AdbClientFactory = new FakeAdbClientFactory(new FakeAdbClient()),
            Console = console
        });

        var exitCode = await app.RunAsync(["scenario-list", "--path", "/tmp/scenarios", "--include-tag", "smoke", "--name", "login", "--action", "waitVisible"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.True(exitCode == 0, console.OutputLines.SingleOrDefault() ?? string.Join(Environment.NewLine, console.ErrorLines));
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("total_count").GetInt32());
        Assert.Equal(1, data.GetProperty("matched_count").GetInt32());
        Assert.Equal("login smoke", data.GetProperty("scenarios")[0].GetProperty("name").GetString());
        Assert.Equal("smoke", data.GetProperty("scenarios")[0].GetProperty("tags")[1].GetString());
    }


    [Fact]
    public async Task RunAsync_Path_DryRun_Returns_Deterministic_Shard_Plan()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/a.json", """
        {
          "name": "a",
          "tags": ["smoke"],
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        fileSystem.AddFile("/tmp/scenarios/b.json", """
        {
          "name": "b",
          "tags": ["smoke"],
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        fileSystem.AddFile("/tmp/scenarios/c.json", """
        {
          "name": "c",
          "tags": ["smoke"],
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        var adb = new FakeAdbClient();
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            AdbClientFactory = new FakeAdbClientFactory(adb),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--path", "/tmp/scenarios", "--dry-run", "--include-tag", "smoke", "--shard-count", "2", "--shard-index", "1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("dry_run").GetBoolean());
        Assert.Equal(3, data.GetProperty("total_count").GetInt32());
        Assert.Equal(3, data.GetProperty("matched_count").GetInt32());
        Assert.Equal(1, data.GetProperty("selected_count").GetInt32());
        Assert.Equal(2, data.GetProperty("sharded_out_count").GetInt32());
        Assert.Equal("b", data.GetProperty("scenarios")[0].GetProperty("name").GetString());
        Assert.Empty(adb.RunCommands);
        Assert.Empty(adb.ShellCommands);
    }

    [Fact]
    public async Task RunAsync_Path_DryRun_Does_Not_Write_Jsonl_Events()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/a.json", """
        {
          "name": "a",
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--path", "/tmp/scenarios", "--dry-run", "--events-jsonl", "/tmp/events.jsonl"]);

        Assert.Equal(0, exitCode);
        Assert.False(fileSystem.FileExists("/tmp/events.jsonl"));
    }


    [Fact]
    public async Task RunAsync_File_DryRun_Returns_Usage_Error()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--dry-run"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("requires --path", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }


    [Fact]
    public async Task ScenarioList_Does_Not_Reject_Unmatched_Invalid_Action()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/valid.json", """
        {
          "name": "valid smoke",
          "tags": ["smoke"],
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        fileSystem.AddFile("/tmp/scenarios/draft.json", """
        {
          "name": "draft",
          "tags": ["draft"],
          "steps": [
            { "action": "notYetImplemented" }
          ]
        }
        """);
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            Console = console
        });

        var exitCode = await app.RunAsync(["scenario-list", "--path", "/tmp/scenarios", "--include-tag", "smoke"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("total_count").GetInt32());
        Assert.Equal(1, data.GetProperty("matched_count").GetInt32());
        Assert.Equal("valid smoke", data.GetProperty("scenarios")[0].GetProperty("name").GetString());
    }


    [Fact]
    public async Task RunAsync_Path_Aggregates_Runtime_Failures()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/fails.json", """
        {
          "name": "fails",
          "steps": [
            { "action": "waitVisible", "text": "Target" }
          ]
        }
        """);
        fileSystem.AddFile("/tmp/scenarios/passes.json", """
        {
          "name": "passes",
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        var host = new FakeDeviceHost
        {
            WaitVisibleException = new InvalidOperationException("not visible")
        };
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--path", "/tmp/scenarios"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(1, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("failed", data.GetProperty("status").GetString());
        Assert.Equal(1, data.GetProperty("passed_count").GetInt32());
        Assert.Equal(1, data.GetProperty("failed_count").GetInt32());
        Assert.Equal(0, data.GetProperty("sharded_out_count").GetInt32());
        var scenarios = data.GetProperty("scenarios");
        Assert.Equal(2, scenarios.GetArrayLength());
        Assert.Equal("fails", scenarios[0].GetProperty("scenario").GetString());
        Assert.Equal("failed", scenarios[0].GetProperty("status").GetString());
        Assert.Equal("fails", scenarios[0].GetProperty("data").GetProperty("scenario").GetString());
        Assert.Equal("passes", scenarios[1].GetProperty("scenario").GetString());
        Assert.Equal("passed", scenarios[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task RunAsync_File_Writes_Jsonl_Events_With_Terminal_Result()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
            { "name": "pause", "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--events-jsonl", "/tmp/events.jsonl"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");

        Assert.Equal(0, exitCode);
        Assert.Equal([
            "scenario_run_started",
            "scenario_started",
            "scenario_step_started",
            "scenario_step_passed",
            "scenario_ended",
            "scenario_run_ended"
        ], events.Select(static evt => evt.GetProperty("event").GetString()!).ToArray());
        Assert.Equal("passed", events[^1].GetProperty("status").GetString());
        Assert.Equal(1, events[^1].GetProperty("passed_count").GetInt32());
        Assert.Equal(0, events[^1].GetProperty("failed_count").GetInt32());
    }

    [Fact]
    public async Task RunAsync_File_Failure_Writes_Jsonl_Terminal_Result()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
            { "name": "target", "action": "waitVisible", "text": "Target" }
          ]
        }
        """);
        var host = new FakeDeviceHost
        {
            WaitVisibleException = new InvalidOperationException("not visible")
        };
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--events-jsonl", "/tmp/events.jsonl"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");

        Assert.Equal(1, exitCode);
        Assert.Contains(events, static evt => evt.GetProperty("event").GetString() == "scenario_step_failed");
        Assert.Equal("scenario_ended", events[^2].GetProperty("event").GetString());
        Assert.Equal("failed", events[^2].GetProperty("status").GetString());
        Assert.Equal("scenario_run_ended", events[^1].GetProperty("event").GetString());
        Assert.Equal("failed", events[^1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task RunAsync_Path_Writes_Jsonl_Run_And_Per_Scenario_Events()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/a.json", """
        {
          "name": "a",
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        fileSystem.AddFile("/tmp/scenarios/b.json", """
        {
          "name": "b",
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--path", "/tmp/scenarios", "--events-jsonl", "/tmp/events.jsonl"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");

        Assert.Equal(0, exitCode);
        Assert.Equal("scenario_run_started", events[0].GetProperty("event").GetString());
        Assert.Equal(2, events[0].GetProperty("selected_count").GetInt32());
        Assert.Equal(["a", "b"], events.Where(static evt => evt.GetProperty("event").GetString() == "scenario_started").Select(static evt => evt.GetProperty("scenario").GetString()!).ToArray());
        Assert.Equal("scenario_run_ended", events[^1].GetProperty("event").GetString());
        Assert.Equal("passed", events[^1].GetProperty("status").GetString());
        Assert.Equal(2, events[^1].GetProperty("passed_count").GetInt32());
    }


    [Fact]
    public async Task RunAsync_Path_Executes_Deterministic_Shard_Order()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/c.json", """
        {
          "name": "c",
          "steps": [
            { "action": "typeText", "text": "c" }
          ]
        }
        """);
        fileSystem.AddFile("/tmp/scenarios/a.json", """
        {
          "name": "a",
          "steps": [
            { "action": "typeText", "text": "a" }
          ]
        }
        """);
        fileSystem.AddFile("/tmp/scenarios/b.json", """
        {
          "name": "b",
          "steps": [
            { "action": "typeText", "text": "b" }
          ]
        }
        """);
        var host = new FakeDeviceHost();
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--path", "/tmp/scenarios", "--shard-count", "2", "--shard-index", "0"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal(["a", "c"], host.TypeTextRequests);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("passed", data.GetProperty("status").GetString());
        Assert.Equal(3, data.GetProperty("matched_count").GetInt32());
        Assert.Equal(2, data.GetProperty("selected_count").GetInt32());
        Assert.Equal(1, data.GetProperty("sharded_out_count").GetInt32());
        Assert.Equal("a", data.GetProperty("scenarios")[0].GetProperty("scenario").GetString());
        Assert.Equal("c", data.GetProperty("scenarios")[1].GetProperty("scenario").GetString());
    }


    [Theory]
    [InlineData("0", "0", "--shard-count must be greater than zero.")]
    [InlineData("2", "2", "--shard-index must be zero or greater and less than --shard-count.")]
    [InlineData("2", null, "--shard-index is required when --shard-count is supplied.")]
    [InlineData(null, "0", "--shard-count is required when --shard-index is supplied.")]
    public async Task RunAsync_Path_Invalid_Shards_Return_Usage_Error(string? shardCount, string? shardIndex, string expectedMessage)
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/a.json", """
        {
          "name": "a",
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        var args = new List<string> { "run", "--path", "/tmp/scenarios", "--dry-run" };
        if (shardCount is not null)
        {
            args.AddRange(["--shard-count", shardCount]);
        }

        if (shardIndex is not null)
        {
            args.AddRange(["--shard-index", shardIndex]);
        }

        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            Console = console
        });

        var exitCode = await app.RunAsync(args.ToArray());
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains(expectedMessage, envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }


        [Fact]
        public async Task RunScenarioAsync_TapPoint_Timing_Reports_PostTap_Delay()
        {
                var fileSystem = new FakeFileSystem();
                var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
                var delay = new FakeDelay(timeProvider);
                var adb = new FakeAdbClient();
                var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, delay, fileSystem);
                var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, delay);
                var scenarioPath = "/tmp/tap-point.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "tap-point-delay",
                    "steps": [
                        { "name": "tap target", "action": "tapPoint", "x": 10, "y": 20, "postTapDelayMs": 150 }
                    ]
                }
                """);

                var result = await scenarios.RunAsync(scenarioPath);
                var json = SerializeToJsonElement(result);

                Assert.Equal(150, json.GetProperty("steps")[0].GetProperty("timing").GetProperty("harness_delay_ms").GetInt32());
                Assert.Equal(150, json.GetProperty("steps")[0].GetProperty("timing").GetProperty("configured_delay_ms").GetInt32());
                Assert.Equal(150, json.GetProperty("steps")[0].GetProperty("result").GetProperty("post_tap_delay_ms").GetInt32());
        }


    [Fact]
    public async Task RunScenarioAsync_Unknown_Action_Throws_UsageException()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
        var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
        var scenarioPath = "/tmp/unknown-action.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "broken",
          "steps": [
            { "action": "launchApp" }
          ]
        }
        """);

        var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

        Assert.Contains("Unknown scenario action 'launchApp'", error.Message, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RunScenarioAsync_Corrupted_NonEmpty_Json_Throws_UsageException()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
        var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
        var scenarioPath = "/tmp/corrupted.json";
        fileSystem.AddFile(scenarioPath, "{ \"name\": \"broken\", \"steps\": [ ");

        var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

        Assert.Contains("not valid JSON", error.Message, StringComparison.Ordinal);
    }


        [Fact]
        public async Task RunScenarioAsync_Empty_Steps_Throws_UsageException()
        {
                var fileSystem = new FakeFileSystem();
                var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
                var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
                var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
                var scenarioPath = "/tmp/empty-steps.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "empty",
                    "steps": []
                }
                """);

                var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

                Assert.Contains("must define at least one step", error.Message, StringComparison.Ordinal);
        }


        [Fact]
        public async Task RunScenarioAsync_Negative_Sleep_Throws_UsageException()
        {
                var fileSystem = new FakeFileSystem();
                var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
                var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
                var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
                var scenarioPath = "/tmp/negative-sleep.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "broken-sleep",
                    "steps": [
                        { "name": "pause", "action": "sleep", "milliseconds": -1 }
                    ]
                }
                """);

                var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

                Assert.Contains("milliseconds must be zero or greater", error.Message, StringComparison.Ordinal);
        }


        [Fact]
        public async Task RunScenarioAsync_Missing_Action_Text_Fails_Before_Device_Work()
        {
                var fileSystem = new FakeFileSystem();
                var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
                var adb = new FakeAdbClient();
                var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
                var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
                var scenarioPath = "/tmp/missing-wait-visible-text.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "broken-missing-text",
                    "steps": [
                        { "name": "pause", "action": "sleep", "milliseconds": 10 },
                        { "name": "wait for sign in", "action": "waitVisible", "timeoutSec": 15 }
                    ]
                }
                """);

                var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

                Assert.Contains("waitVisible requires text", error.Message, StringComparison.Ordinal);
                Assert.Empty(adb.ShellCommands);
                Assert.Empty(adb.RunCommands);
        }


        [Fact]
        public async Task RunScenarioAsync_Invalid_AssertEvent_Regex_Fails_Before_Device_Work()
        {
                var fileSystem = new FakeFileSystem();
                var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
                var adb = new FakeAdbClient();
                var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
                var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
                var scenarioPath = "/tmp/invalid-assert-event-regex.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "broken-regex",
                    "steps": [
                        { "name": "printing succeeded", "action": "assertEvent", "event": "PRINTING_SUCCESSFUL", "detailsPattern": "[", "timeoutSec": 15 }
                    ]
                }
                """);

                var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

                Assert.Contains("assertEvent detailsPattern is not a valid regular expression", error.Message, StringComparison.Ordinal);
                Assert.Empty(adb.ShellCommands);
                Assert.Empty(adb.RunCommands);
        }


        [Fact]
        public async Task RunScenarioAsync_AssertEvent_Can_Observe_From_Previous_Step()
        {
                var fileSystem = new FakeFileSystem();
                var initialTime = DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
                var timeProvider = new ManualTimeProvider(initialTime);
                var delay = new FakeDelay(timeProvider);
                var adb = new FakeAdbClient();
                adb.EnqueueShellResult(new ProcessResult(0, CreateDeviceFingerprintShellOutput("SER123", "Pixel 9", "16", "36", "google/pixel/device", "arm64-v8a", "mCurrentFocus=App"), string.Empty));
                adb.EnqueueLogLines("I/flutter (17495): Log.PRINTING_SUCCESSFUL: [Main Isolate] Printing successful");
                var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, delay, fileSystem);
                var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, delay);
                var scenarioPath = "/tmp/assert-event-lookback.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "assert-event-lookback",
                    "steps": [
                        { "name": "pause", "action": "sleep", "milliseconds": 250 },
                        { "name": "printing succeeded", "action": "assertEvent", "event": "PRINTING_SUCCESSFUL", "timeoutSec": 5, "observeFromPreviousStep": true }
                    ]
                }
                """);

                var result = await scenarios.RunAsync(scenarioPath);
                var envelope = SerializeToJsonElement(result);

                Assert.Equal(initialTime, adb.StreamingLogRequests[0].Since);
                Assert.Equal(250, envelope.GetProperty("timing").GetProperty("steps_ms").GetInt32());
                Assert.Equal(0, envelope.GetProperty("steps")[1].GetProperty("timing").GetProperty("harness_delay_ms").GetInt32());
        }


    [Fact]
    public async Task RunScenarioAsync_WaitStep_Action_Uses_Step_Field()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, CreateDeviceFingerprintShellOutput("SER123", "Pixel 9", "16", "36", "google/pixel/device", "arm64-v8a", "mCurrentFocus=App"), string.Empty));
        adb.EnqueueLogLines("05-15 12:00:00.000 I/Luotsi: LUOTSI_DEVICE_TELEMETRY {\"schema\":\"luotsi-device-telemetry.v1\",\"seq\":12,\"session\":\"abc\",\"timestamp\":\"2026-05-15T12:00:00Z\",\"event\":\"step\",\"step\":\"STEP_IDLE\"}");
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
        var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
        var scenarioPath = "/tmp/wait-step.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "semantic-step",
          "steps": [
            { "name": "wait for idle", "action": "waitStep", "step": "idle", "timeoutSec": 5 }
          ]
        }
        """);

        var result = await scenarios.RunAsync(scenarioPath);
        var json = SerializeToJsonElement(result);

        Assert.Equal("semantic-step", json.GetProperty("scenario").GetString());
        Assert.Equal("passed", json.GetProperty("status").GetString());
        Assert.Equal("waitStep", json.GetProperty("steps")[0].GetProperty("action").GetString());
        Assert.Equal("STEP_IDLE", json.GetProperty("steps")[0].GetProperty("result").GetProperty("step").GetString());
    }


        [Fact]
        public async Task RunScenarioAsync_Resolves_Nested_Variables_And_Environment_Fallback()
        {
                var fileSystem = new FakeFileSystem();
                var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
                var environment = new FakeEnvironmentVariables(new Dictionary<string, string>());
                var host = new FakeDeviceHost();
                var scenarios = new ScenarioExecutor(host, fileSystem, timeProvider, new FakeDelay(timeProvider), environment);
                var scenarioPath = "/tmp/template-resolution.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "${var:scenarioName}",
                    "variables": {
                        "envName": "${env:SCENARIO_NAME|fallback-title}",
                        "scenarioName": "case-${var:envName}"
                    },
                    "steps": [
                        { "name": "${var:scenarioName}", "action": "typeText", "text": "${var:envName}" }
                    ]
                }
                """);

                var result = await scenarios.RunAsync(scenarioPath);
                var json = SerializeToJsonElement(result);

                Assert.Equal("case-fallback-title", json.GetProperty("scenario").GetString());
                Assert.Equal("case-fallback-title", json.GetProperty("steps")[0].GetProperty("step").GetString());
                Assert.Equal(["fallback-title"], host.TypeTextRequests);
        }


        [Fact]
        public async Task RunScenarioAsync_Variable_Cycle_Throws_UsageException()
        {
                var fileSystem = new FakeFileSystem();
                var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
                var host = new FakeDeviceHost();
                var scenarios = new ScenarioExecutor(host, fileSystem, timeProvider, new FakeDelay(timeProvider), new FakeEnvironmentVariables(new Dictionary<string, string>()));
                var scenarioPath = "/tmp/template-cycle.json";
                fileSystem.AddFile(scenarioPath, """
                {
                    "name": "${var:first}",
                    "variables": {
                        "first": "${var:second}",
                        "second": "${var:first}"
                    },
                    "steps": [
                        { "action": "sleep", "milliseconds": 1 }
                    ]
                }
                """);

                var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

                Assert.Contains("part of a cycle", error.Message, StringComparison.Ordinal);
                Assert.Empty(host.TypeTextRequests);
        }


    [Fact]
    public void ScenarioCatalog_Files_Are_Valid_Json()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var scenarioDirectory = Path.Combine(repoRoot, "examples", "scenarios");

        Assert.True(Directory.Exists(scenarioDirectory), $"Scenario directory was not found: {scenarioDirectory}");

        var files = Directory.GetFiles(scenarioDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var scenario = JsonSerializer.Deserialize<ScenarioFile>(text, AppJson.Options);

            Assert.NotNull(scenario);
            Assert.False(string.IsNullOrWhiteSpace(scenario.Name));
            Assert.NotEmpty(scenario.Steps);
        }
    }


    [Fact]
    public async Task RunAsync_Scenario_LogWait_Timeout_Captures_Failure_Bundle()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var console = new FakeConsole();
        var scenarioPath = "/tmp/log-timeout.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "log-timeout",
          "steps": [
            { "name": "wait for ready marker", "action": "waitLog", "text": "READY", "timeoutSec": 2 }
          ]
        }
        """);
                adb.EnqueueShellResult(new ProcessResult(0, CreateDeviceFingerprintShellOutput("SER123", "Pixel 9", "16", "36", "google/pixel/device", "arm64-v8a", "mCurrentFocus=App"), string.Empty));
        adb.EnqueueLogLines("I/Test: boot", "I/Test: still waiting");
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "01-01 00:00:00.000 I/Test: snapshot", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, CreateUiDump("Failure"), string.Empty));
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            AdbClientFactory = new FakeAdbClientFactory(adb),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", scenarioPath, "--artifacts", "/tmp/test-artifacts"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(1, exitCode);
        Assert.Equal("log_wait_timeout", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal("failed", envelope.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("wait for ready marker", envelope.RootElement.GetProperty("data").GetProperty("failed_step").GetProperty("name").GetString());
        var failureArtifacts = envelope.RootElement.GetProperty("data").GetProperty("failure_artifacts");
        Assert.Equal(ResultSchemas.FailureBundle, failureArtifacts.GetProperty("schema").GetString());
        Assert.True(failureArtifacts.GetProperty("artifacts").GetArrayLength() >= 2);
    }

    private static JsonElement[] ReadJsonlEvents(FakeFileSystem fileSystem, string path) =>
        fileSystem.ReadAllTextAsync(path).GetAwaiter().GetResult()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();

}
