using System.Text.Json;
using System.Xml.Linq;
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
    public async Task ScenarioInit_Writes_Starter_Scenario_And_Returns_Next_Commands()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            AdbClientFactory = new FakeAdbClientFactory(new FakeAdbClient()),
            Console = console
        });

        var exitCode = await app.RunAsync(["scenario-init", "--file", "/tmp/scenarios/login-smoke.json", "--name", "login smoke", "--package", "dev.luotsi.demo", "--activity", ".MainActivity", "--width", "720", "--height", "1280", "--orientation", "portrait"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(fileSystem.FileExists("/tmp/scenarios/login-smoke.json"));
        var scenarioJson = await fileSystem.ReadAllTextAsync("/tmp/scenarios/login-smoke.json");
        using var scenario = JsonDocument.Parse(scenarioJson);
        Assert.Equal("login smoke", scenario.RootElement.GetProperty("name").GetString());
        Assert.Equal("startApp", scenario.RootElement.GetProperty("setup")[0].GetProperty("action").GetString());
        Assert.Equal("${var:targetPackage}", scenario.RootElement.GetProperty("setup")[0].GetProperty("package").GetString());
        Assert.Equal("${var:targetActivity}", scenario.RootElement.GetProperty("setup")[0].GetProperty("activity").GetString());
        Assert.Equal("takeScreenshot", scenario.RootElement.GetProperty("steps")[1].GetProperty("action").GetString());
        Assert.Equal(720, scenario.RootElement.GetProperty("metadata").GetProperty("layout").GetProperty("width").GetInt32());
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("scenario-validate", envelope.RootElement.GetProperty("data").GetProperty("next_commands")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScenarioInit_Creates_Target_Directory()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            AdbClientFactory = new FakeAdbClientFactory(new FakeAdbClient()),
            Console = console
        });

        var exitCode = await app.RunAsync(["scenario-init", "--file", "/tmp/new-scenarios/smoke.json"]);

        Assert.Equal(0, exitCode);
        Assert.True(fileSystem.DirectoryExists("/tmp/new-scenarios"));
        Assert.True(fileSystem.FileExists("/tmp/new-scenarios/smoke.json"));
    }

    [Fact]
    public async Task ScenarioExplain_Returns_Authoring_Summary()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/login.json", """
        {
          "name": "login smoke",
          "tags": ["smoke", "auth"],
          "metadata": {
            "package": "dev.luotsi.demo",
            "layout": { "width": 720, "height": 1280, "orientation": "portrait" }
          },
          "setup": [
            { "action": "startApp", "package": "dev.luotsi.demo", "activity": ".MainActivity", "wait": true }
          ],
          "steps": [
            { "action": "waitVisible", "text": "Sign in" },
            { "action": "tapText", "text": "Sign in" }
          ],
          "teardown": [
            { "action": "captureArtifacts", "label": "final" }
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

        var exitCode = await app.RunAsync(["scenario-explain", "--file", "/tmp/scenarios/login.json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("login smoke", data.GetProperty("name").GetString());
        Assert.Equal(1, data.GetProperty("setup_step_count").GetInt32());
        Assert.Equal(2, data.GetProperty("step_count").GetInt32());
        Assert.Equal(1, data.GetProperty("teardown_step_count").GetInt32());
        Assert.Contains(data.GetProperty("actions").EnumerateArray(), action => action.GetString() == "tapText");
        Assert.Contains("run --file", data.GetProperty("suggested_commands")[2].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScenarioValidate_Validates_File_Without_Device()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "basic",
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
            AdbClientFactory = new FakeAdbClientFactory(new FakeAdbClient()),
            Console = console
        });

        var exitCode = await app.RunAsync(["scenario-validate", "--file", "/tmp/scenario.json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("validated", envelope.RootElement.GetProperty("data").GetProperty("status").GetString());
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
    public async Task RunAsync_File_ValidateOnly_Validates_Without_Device_Work()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new SteppingTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind), TimeSpan.FromMilliseconds(1));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "validate app flow",
          "setup": [
            { "action": "startApp", "package": "dev.luotsi.app", "activity": ".MainActivity", "wait": true }
          ],
          "steps": [
            { "name": "press back", "action": "keyevent", "code": "KEYCODE_BACK" }
          ],
          "teardown": [
            { "action": "forceStop", "package": "dev.luotsi.app" }
          ]
        }
        """);
        var host = new FakeDeviceHost
        {
            DeviceFingerprintException = new InvalidOperationException("device should not be touched")
        };
        var deviceHostFactory = new FakeDeviceHostFactory(host);
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
          Delay = new SteppingDelay(timeProvider),
            DeviceHostFactory = deviceHostFactory,
            Console = console
        });

        var exitCode = await app.RunAsync([
            "run",
            "--file", "/tmp/scenario.json",
            "--validate-only",
            "--events-jsonl", "/tmp/events.jsonl",
            "--report-json", "/tmp/report.json",
            "--report-junit", "/tmp/junit.xml"]);
        using var envelope = console.ParseSingleOutputAsJson();
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));
        var junit = XDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/junit.xml"));
        var startedAt = events.Single(static evt => evt.GetProperty("event").GetString() == "scenario_started").GetProperty("timestamp").GetDateTimeOffset();
        var endedEvent = events.Single(static evt => evt.GetProperty("event").GetString() == "scenario_ended");
        var durationMs = (endedEvent.GetProperty("timestamp").GetDateTimeOffset() - startedAt).TotalMilliseconds;

        Assert.Equal(0, exitCode);
        Assert.Equal("validated", envelope.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(3, envelope.RootElement.GetProperty("data").GetProperty("steps").GetArrayLength());
        Assert.All(envelope.RootElement.GetProperty("data").GetProperty("steps").EnumerateArray(), step => Assert.Equal("validated", step.GetProperty("status").GetString()));
        Assert.Equal("validated", events[^1].GetProperty("status").GetString());
        Assert.Equal(0, events[^1].GetProperty("failed_count").GetInt32());
        Assert.Equal(durationMs, endedEvent.GetProperty("duration_ms").GetDouble());
        Assert.Equal(durationMs, envelope.RootElement.GetProperty("data").GetProperty("timing").GetProperty("total_ms").GetDouble());
        Assert.Equal(durationMs, envelope.RootElement.GetProperty("data").GetProperty("timing").GetProperty("non_step_ms").GetDouble());
        Assert.Equal("validated", envelope.RootElement.GetProperty("data").GetProperty("governance").GetProperty("kind").GetString());
        Assert.Equal("validated", report.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, report.RootElement.GetProperty("failed_count").GetInt32());
        Assert.Equal("validated", report.RootElement.GetProperty("governance").GetProperty("kind").GetString());
        Assert.Equal("validated", report.RootElement.GetProperty("scenarios")[0].GetProperty("governance").GetProperty("kind").GetString());
        Assert.Equal(durationMs, report.RootElement.GetProperty("scenarios")[0].GetProperty("duration_ms").GetDouble());
        Assert.Equal("validated", endedEvent.GetProperty("governance").GetProperty("kind").GetString());
        Assert.Equal("validated", events[^1].GetProperty("governance").GetProperty("kind").GetString());
        Assert.Empty(junit.Root!.Elements("testcase").Single().Elements("failure"));
        Assert.Empty(host.StartAppRequests);
        Assert.Empty(host.KeyEventRequests);
        Assert.Empty(host.ForceStopRequests);
        Assert.Equal(0, deviceHostFactory.CreateCallCount);
    }

    [Fact]
    public async Task RunAsync_Path_ValidateOnly_Uses_Filters_And_Reports_Invalid_Selected_Scenarios()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/a.json", """
        {
          "name": "valid smoke",
          "tags": ["smoke"],
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        fileSystem.AddFile("/tmp/scenarios/b.json", """
        {
          "name": "invalid smoke",
          "tags": ["smoke"],
          "steps": [
            { "action": "tapText" }
          ]
        }
        """);
        fileSystem.AddFile("/tmp/scenarios/c.json", """
        {
          "name": "ignored regression",
          "tags": ["regression"],
          "steps": [
            { "action": "tapText" }
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

        var exitCode = await app.RunAsync([
            "run",
            "--path", "/tmp/scenarios",
            "--validate-only",
            "--include-tag", "smoke",
            "--report-json", "/tmp/report.json"]);
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(1, exitCode);
        Assert.Equal("failed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal(3, report.RootElement.GetProperty("total_count").GetInt32());
        Assert.Equal(2, report.RootElement.GetProperty("selected_count").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("failed_count").GetInt32());
        Assert.Equal("validated", report.RootElement.GetProperty("scenarios")[0].GetProperty("status").GetString());
        Assert.Equal("failed", report.RootElement.GetProperty("scenarios")[1].GetProperty("status").GetString());
        Assert.Contains("tapText requires text", report.RootElement.GetProperty("scenarios")[1].GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

      [Fact]
      public async Task ValidatePlanAsync_OperationCanceledException_Propagates()
      {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        fileSystem.AddFile("/tmp/scenarios/cancel.json", """
        {
          "name": "cancel",
          "steps": [
          { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        var scenarioCatalog = new ScenarioCatalog(
          fileSystem,
          new ScenarioTemplateResolver(
            timeProvider,
            new FakeEnvironmentVariables(new Dictionary<string, string>())));
        var executor = new ScenarioValidationExecutor(
          scenarioCatalog,
          timeProvider,
          new ThrowingScenarioEventSink(new OperationCanceledException("cancelled")));
        var scenario = new ScenarioCatalogEntry("/tmp/scenarios/cancel.json::cancel", "cancel", "/tmp/scenarios/cancel.json", [], 1, ["sleep"]);
        var plan = new ScenarioRunPlan(
          new ScenarioQuery("/tmp/scenarios", [], [], null, null, null, null, false),
          1,
          [scenario],
          [scenario],
          0);

        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.ValidatePlanAsync(plan));
      }

    [Fact]
    public async Task ScenarioList_RecursiveGlob_Finds_Nested_Scenarios()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/a.json", """
        {
          "name": "root",
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        fileSystem.AddFile("/tmp/scenarios/nested/b.json", """
        {
          "name": "nested",
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

        var exitCode = await app.RunAsync(["scenario-list", "--path", "/tmp/scenarios/**/*.json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var scenarios = envelope.RootElement.GetProperty("data").GetProperty("scenarios");
        Assert.Equal(["nested", "root"], scenarios.EnumerateArray().Select(static scenario => scenario.GetProperty("name").GetString()!).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ScenarioList_RecursiveGlob_With_Directory_Remainder_Returns_Usage_Error()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/nested/b.json", """
        {
          "name": "nested",
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

        var exitCode = await app.RunAsync(["scenario-list", "--path", "/tmp/scenarios/**/nested/*.json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Contains("only supports recursive globs", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
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
          WaitVisibleException = new InvalidOperationException("not visible"),
          PreflightTemplate = new PreflightResult("Pixel 7", "16", "36", "focus", "dev.luotsi.app", null, "fingerprint", "arm64-v8a", "SER123")
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
        using var envelope = console.ParseSingleOutputAsJson();
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        var artifactRoot = envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();
        var artifactCommands = envelope.RootElement.GetProperty("data").GetProperty("artifact_commands").EnumerateArray().ToArray();

        Assert.Equal(0, exitCode);
        Assert.Equal([
            "scenario_run_started",
            "scenario_started",
            "scenario_step_started",
            "scenario_step_passed",
            "scenario_ended",
            "scenario_run_ended"
        ], events.Select(static evt => evt.GetProperty("event").GetString()!).ToArray());
        Assert.Equal("luotsi", events[0].GetProperty("provenance").GetProperty("tool").GetString());
        Assert.True(events[0].GetProperty("provenance").TryGetProperty("os", out _));
        Assert.Equal("passed", events[^1].GetProperty("status").GetString());
        Assert.Equal("luotsi", events[^1].GetProperty("provenance").GetProperty("tool").GetString());
        Assert.Equal(1, events[^1].GetProperty("passed_count").GetInt32());
        Assert.Equal(0, events[^1].GetProperty("failed_count").GetInt32());
        Assert.Equal(1, events[^1].GetProperty("metrics").GetProperty("step_count").GetInt32());
        Assert.Equal(1, events[^1].GetProperty("metrics").GetProperty("action.sleep.count").GetInt32());
        Assert.Equal(1, Assert.Single(events, static evt => evt.GetProperty("event").GetString() == "scenario_step_passed").GetProperty("metrics").GetProperty("configured_delay_ms").GetInt32());
        Assert.NotNull(artifactRoot);
        Assert.Contains(artifactCommands, command => command.GetProperty("kind").GetString() == "open_artifacts");
        Assert.Contains(artifactCommands, command => command.GetProperty("kind").GetString() == "pack_artifacts");
        Assert.Contains(artifactCommands, command => command.GetProperty("command").GetString() == $"luotsi replay open --artifacts {artifactRoot}");
        await AssertRunReplayArtifactsAsync(fileSystem, artifactRoot, "/tmp/scenario.json", [
          "scenario_run_started",
          "scenario_started",
          "scenario_step_started",
          "scenario_step_passed",
          "scenario_ended",
          "scenario_run_ended"
        ]);
    }

    [Fact]
    public async Task RunAsync_File_Progress_Jsonl_Writes_Typed_Progress_To_Stderr()
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

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--validate-only", "--progress", "jsonl"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.NotEmpty(console.ErrorLines);
        using var first = JsonDocument.Parse(console.ErrorLines[0]);
        Assert.Equal("luotsi-scenario-progress.v1", first.RootElement.GetProperty("schema").GetString());
        Assert.Equal("scenario_progress", first.RootElement.GetProperty("type").GetString());
        Assert.Equal("scenario_run_started", first.RootElement.GetProperty("event").GetProperty("event").GetString());
        using var last = JsonDocument.Parse(console.ErrorLines[^1]);
        Assert.Equal("scenario_run_ended", last.RootElement.GetProperty("event").GetProperty("event").GetString());
        Assert.Equal("validated", last.RootElement.GetProperty("event").GetProperty("status").GetString());
    }

    [Fact]
    public async Task RunAsync_File_Progress_Quiet_Suppresses_Progress_Stderr()
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

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--validate-only", "--progress", "quiet"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Empty(console.ErrorLines);
    }

    [Fact]
    public async Task RunAsync_File_Prepares_Device_And_Writes_Allocation_Metadata()
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
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Pixel 7", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER123")
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

        var exitCode = await app.RunAsync([
            "run",
            "--file", "/tmp/scenario.json",
            "--events-jsonl", "/tmp/events.jsonl",
            "--report-json", "/tmp/report.json",
            "--package", "dev.luotsi.app",
            "--device-ready-timeout-sec", "7"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(0, exitCode);
        Assert.Equal([7], host.WaitForDeviceRequests);
        Assert.Equal(["dev.luotsi.app"], host.ReadOnlyPreflightRequests);
        var eventAllocation = events[^1].GetProperty("device_allocation");
        Assert.Equal("allocated", eventAllocation.GetProperty("status").GetString());
        Assert.Equal("SER123", eventAllocation.GetProperty("serial").GetString());
        Assert.True(eventAllocation.GetProperty("require_ready").GetBoolean());
        Assert.Equal(7, eventAllocation.GetProperty("wait_timeout_sec").GetInt32());
        Assert.Equal("dev.luotsi.app", eventAllocation.GetProperty("package").GetString());
        Assert.Equal("Pixel 7", eventAllocation.GetProperty("readiness").GetProperty("model").GetString());
        var reportAllocation = report.RootElement.GetProperty("device_allocation");
        Assert.Equal("SER123", reportAllocation.GetProperty("serial").GetString());
        Assert.Equal("dev.luotsi.app", reportAllocation.GetProperty("readiness").GetProperty("package").GetString());
    }

    [Fact]
    public async Task RunAsync_File_ClaimDevice_Releases_Lease_After_Run()
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
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Pixel 7", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER123")
        };
        host.ConnectedDevices.Add(new DeviceInfo("SER123", "device", "product:panther model:Pixel_7 device:panther"));
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync([
            "run",
            "--file", "/tmp/scenario.json",
            "--device-query", "model=Pixel_7",
            "--claim-device",
            "--owner", "ci-job-1",
            "--report-json", "/tmp/report.json",
            "--no-require-device-ready"]);

        Assert.Equal(0, exitCode);
        Assert.False(fileSystem.FileExists(Path.Join("/tmp", "luotsi", "lab-leases", "SER123.json")));
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));
        var lease = report.RootElement.GetProperty("device_allocation").GetProperty("lease");
        Assert.Equal("SER123", lease.GetProperty("serial").GetString());
        Assert.Equal("ci-job-1", lease.GetProperty("owner").GetString());
    }

    [Fact]
    public async Task RunAsync_File_ClaimDevice_Releases_Lease_After_Failure()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
            { "name": "broken", "action": "tapText" }
          ]
        }
        """);
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Pixel 7", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER123")
        };
        host.ConnectedDevices.Add(new DeviceInfo("SER123", "device", "product:panther model:Pixel_7 device:panther"));
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync([
            "run",
            "--file", "/tmp/scenario.json",
            "--device-query", "model=Pixel_7",
            "--claim-device",
            "--owner", "ci-job-1",
            "--no-require-device-ready"]);

        Assert.Equal(2, exitCode);
        Assert.False(fileSystem.FileExists(Path.Join("/tmp", "luotsi", "lab-leases", "SER123.json")));
    }

    [Fact]
    public async Task RunAsync_File_NoRequireDeviceReady_Skips_Wait_But_Records_Readiness()
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

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--report-json", "/tmp/report.json", "--no-require-device-ready"]);
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(0, exitCode);
        Assert.Empty(host.WaitForDeviceRequests);
        Assert.Single(host.ReadOnlyPreflightRequests);
        Assert.False(report.RootElement.GetProperty("device_allocation").GetProperty("require_ready").GetBoolean());
    }

      [Fact]
      public async Task RunAsync_File_Failure_Preserves_DeviceAllocation_In_Events_And_Report()
      {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
          { "action": "waitVisible", "text": "Target" }
          ]
        }
        """);
        var host = new FakeDeviceHost
        {
          WaitVisibleException = new InvalidOperationException("not visible"),
          PreflightTemplate = new PreflightResult("Pixel 7", "16", "36", "focus", "dev.luotsi.app", null, "fingerprint", "arm64-v8a", "SER123")
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

        var exitCode = await app.RunAsync([
          "run",
          "--file", "/tmp/scenario.json",
          "--events-jsonl", "/tmp/events.jsonl",
          "--report-json", "/tmp/report.json",
          "--package", "dev.luotsi.app"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(1, exitCode);
        var endedEvent = Assert.Single(events, static evt => evt.GetProperty("event").GetString() == "scenario_run_ended");
        var eventAllocation = endedEvent.GetProperty("device_allocation");
        Assert.Equal("SER123", eventAllocation.GetProperty("serial").GetString());
        Assert.Equal("dev.luotsi.app", eventAllocation.GetProperty("package").GetString());
        Assert.Equal("scenario_observable_failure", endedEvent.GetProperty("governance").GetProperty("kind").GetString());
        Assert.True(endedEvent.GetProperty("governance").GetProperty("regression_candidate").GetBoolean());
        Assert.Contains("step 1", endedEvent.GetProperty("governance").GetProperty("summary").GetString(), StringComparison.Ordinal);

        var reportAllocation = report.RootElement.GetProperty("device_allocation");
        Assert.Equal("SER123", reportAllocation.GetProperty("serial").GetString());
        Assert.Equal("dev.luotsi.app", reportAllocation.GetProperty("package").GetString());
        Assert.Equal("scenario_observable_failure", report.RootElement.GetProperty("governance").GetProperty("kind").GetString());
        Assert.Equal("scenario_observable_failure", report.RootElement.GetProperty("scenarios")[0].GetProperty("governance").GetProperty("kind").GetString());
        Assert.Contains("step 1", report.RootElement.GetProperty("governance").GetProperty("summary").GetString(), StringComparison.Ordinal);
      }

    [Fact]
    public async Task RunAsync_File_Startup_Device_Failure_Classifies_Lab_Infrastructure_In_Events_And_Report()
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
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Old Box", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER123"),
            DeviceFingerprintException = new InvalidOperationException("device offline")
        };
        host.ConnectedDevices.Add(new DeviceInfo("SER123", "offline", "product:box model:Old_Box device:box usb:1-1"));
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync([
            "run",
            "--file", "/tmp/scenario.json",
            "--events-jsonl", "/tmp/events.jsonl",
            "--report-json", "/tmp/report.json"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(1, exitCode);
        Assert.Equal("lab_infrastructure_failure", events[^1].GetProperty("governance").GetProperty("kind").GetString());
        Assert.True(events[^1].GetProperty("governance").GetProperty("infrastructure_related").GetBoolean());
        Assert.True(events[^1].GetProperty("governance").GetProperty("quarantine_candidate").GetBoolean());
        Assert.Contains("SER123", events[^1].GetProperty("governance").GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal("lab_infrastructure_failure", report.RootElement.GetProperty("governance").GetProperty("kind").GetString());
        Assert.Equal("lab_infrastructure_failure", report.RootElement.GetProperty("scenarios")[0].GetProperty("governance").GetProperty("kind").GetString());
        Assert.True(report.RootElement.GetProperty("governance").GetProperty("quarantine_candidate").GetBoolean());
        Assert.Contains("SER123", report.RootElement.GetProperty("governance").GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_File_Runs_Setup_Steps_Teardown_In_Order()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "lifecycle",
          "setup": [
            { "name": "launch", "action": "startApp", "package": "dev.luotsi.app", "activity": ".MainActivity", "wait": true }
          ],
          "steps": [
            { "name": "press back", "action": "keyevent", "code": "KEYCODE_BACK" }
          ],
          "teardown": [
            { "name": "stop", "action": "forceStop", "package": "dev.luotsi.app" }
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

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--events-jsonl", "/tmp/events.jsonl"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        var passedSteps = events
            .Where(static evt => evt.GetProperty("event").GetString() == "scenario_step_passed")
            .ToArray();

        Assert.Equal(0, exitCode);
        Assert.Equal([("setup", 1), ("main", 2), ("teardown", 3)], passedSteps
            .Select(static evt => (evt.GetProperty("phase").GetString()!, evt.GetProperty("step_index").GetInt32()))
            .ToArray());
        Assert.Equal("dev.luotsi.app", host.StartAppRequests.Single().Package);
        Assert.Equal("KEYCODE_BACK", host.KeyEventRequests.Single());
        Assert.Equal("dev.luotsi.app", host.ForceStopRequests.Single());
    }

    [Fact]
    public async Task RunAsync_File_First_Setup_Step_Cannot_Observe_Previous_Step()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "invalid lifecycle",
          "setup": [
            { "name": "event", "action": "assertEvent", "event": "ready", "observeFromPreviousStep": true }
          ],
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

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Contains("no previous lifecycle step", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_File_First_Main_Step_Can_Observe_Setup_Step()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new SteppingTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind), TimeSpan.FromMilliseconds(10));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "observe setup",
          "setup": [
            { "name": "pause", "action": "sleep", "milliseconds": 25 }
          ],
          "steps": [
            { "name": "event", "action": "assertEvent", "event": "ready", "observeFromPreviousStep": true }
          ]
        }
        """);
        var host = new FakeDeviceHost();
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new SteppingDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json"]);

        Assert.Equal(0, exitCode);
        Assert.NotNull(host.AssertEventRequests.Single().Since);
    }

    [Fact]
    public async Task RunAsync_File_Writes_Consistent_Scenario_Ended_Timestamp_And_Duration()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new SteppingTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind), TimeSpan.FromMilliseconds(1));
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
            Delay = new SteppingDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--events-jsonl", "/tmp/events.jsonl"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        var startedAt = events.Single(static evt => evt.GetProperty("event").GetString() == "scenario_started").GetProperty("timestamp").GetDateTimeOffset();
        var endedEvent = events.Single(static evt => evt.GetProperty("event").GetString() == "scenario_ended");
        var endedAt = endedEvent.GetProperty("timestamp").GetDateTimeOffset();

        Assert.Equal(0, exitCode);
        Assert.Equal((endedAt - startedAt).TotalMilliseconds, endedEvent.GetProperty("duration_ms").GetDouble());
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
        Assert.Equal(1, events[^2].GetProperty("metrics").GetProperty("step_count").GetInt32());
        Assert.Equal(1, events[^2].GetProperty("metrics").GetProperty("failed_step_count").GetInt32());
        Assert.Equal("scenario_run_ended", events[^1].GetProperty("event").GetString());
        Assert.Equal("failed", events[^1].GetProperty("status").GetString());
        Assert.Equal(0, events[^1].GetProperty("passed_count").GetInt32());
        Assert.Equal(1, events[^1].GetProperty("failed_count").GetInt32());
        Assert.Equal(1, events[^1].GetProperty("metrics").GetProperty("step_count").GetInt32());
        Assert.Equal(1, events[^1].GetProperty("metrics").GetProperty("action.waitvisible.count").GetInt32());
    }

    [Fact]
    public async Task RunAsync_File_Runs_Teardown_After_Main_Failure_And_Reports_Phases()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "failing lifecycle",
          "setup": [
            { "name": "launch", "action": "startApp", "package": "dev.luotsi.app" }
          ],
          "steps": [
            { "name": "target", "action": "waitVisible", "text": "Target" }
          ],
          "teardown": [
            { "name": "stop", "action": "forceStop", "package": "dev.luotsi.app" }
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

        var exitCode = await app.RunAsync([
            "run",
            "--file", "/tmp/scenario.json",
            "--events-jsonl", "/tmp/events.jsonl",
            "--report-json", "/tmp/report.json"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));
        var scenario = report.RootElement.GetProperty("scenarios")[0];

        Assert.Equal(1, exitCode);
        Assert.Equal("dev.luotsi.app", host.ForceStopRequests.Single());
        Assert.Contains(events, static evt =>
            evt.GetProperty("event").GetString() == "scenario_step_passed" &&
            evt.GetProperty("phase").GetString() == "teardown");
        Assert.Equal("main", scenario.GetProperty("failed_step").GetProperty("phase").GetString());
        Assert.Contains(scenario.GetProperty("steps").EnumerateArray(), static step =>
          step.GetProperty("action").GetString() == "waitVisible" &&
          step.TryGetProperty("status", out var status) &&
          status.GetString() == "failed");
        Assert.Contains(scenario.GetProperty("steps").EnumerateArray(), static step =>
            step.GetProperty("phase").GetString() == "teardown" &&
            step.GetProperty("action").GetString() == "forceStop");
    }

    [Fact]
    public async Task RunAsync_File_Teardown_Failure_Does_Not_Mask_Main_Failure()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "double failure lifecycle",
          "steps": [
            { "name": "target", "action": "waitVisible", "text": "Target" }
          ],
          "teardown": [
            { "name": "stop", "action": "forceStop", "package": "dev.luotsi.app" }
          ]
        }
        """);
        var host = new FakeDeviceHost
        {
            WaitVisibleException = new InvalidOperationException("not visible"),
            ForceStopException = new InvalidOperationException("cleanup failed")
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

        var exitCode = await app.RunAsync([
            "run",
            "--file", "/tmp/scenario.json",
            "--events-jsonl", "/tmp/events.jsonl",
            "--report-json", "/tmp/report.json"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(1, exitCode);
        Assert.Contains("main step", report.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("cleanup failed", report.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("main", report.RootElement.GetProperty("scenarios")[0].GetProperty("failed_step").GetProperty("phase").GetString());
        Assert.Contains(events, static evt =>
            evt.GetProperty("event").GetString() == "scenario_step_failed" &&
            evt.GetProperty("phase").GetString() == "teardown" &&
            evt.GetProperty("error").GetProperty("message").GetString() == "cleanup failed");
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
        using var envelope = console.ParseSingleOutputAsJson();
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        var artifactRoot = envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();

        Assert.Equal(0, exitCode);
        Assert.Equal("scenario_run_started", events[0].GetProperty("event").GetString());
        Assert.Equal(2, events[0].GetProperty("selected_count").GetInt32());
        Assert.Equal("luotsi", events[0].GetProperty("provenance").GetProperty("tool").GetString());
        Assert.Equal(["a", "b"], events.Where(static evt => evt.GetProperty("event").GetString() == "scenario_started").Select(static evt => evt.GetProperty("scenario").GetString()!).ToArray());
        Assert.Equal("scenario_run_ended", events[^1].GetProperty("event").GetString());
        Assert.Equal("passed", events[^1].GetProperty("status").GetString());
        Assert.Equal("luotsi", events[^1].GetProperty("provenance").GetProperty("tool").GetString());
        Assert.Equal(2, events[^1].GetProperty("passed_count").GetInt32());
        Assert.Equal(2, events[^1].GetProperty("metrics").GetProperty("step_count").GetInt32());
        Assert.Equal(2, events[^1].GetProperty("metrics").GetProperty("passed_scenario_count").GetInt32());
        Assert.Equal(2, events[^1].GetProperty("metrics").GetProperty("action.sleep.count").GetInt32());
        Assert.All(events.Where(static evt => evt.TryGetProperty("scenario", out _)), evt => Assert.Contains("::", evt.GetProperty("scenario_id").GetString(), StringComparison.Ordinal));
        Assert.NotNull(artifactRoot);
        await AssertRunReplayArtifactsAsync(fileSystem, artifactRoot, "/tmp/scenarios", [
          "scenario_run_started",
          "scenario_started",
          "scenario_step_started",
          "scenario_step_passed",
          "scenario_ended",
          "scenario_started",
          "scenario_step_started",
          "scenario_step_passed",
          "scenario_ended",
          "scenario_run_ended"
        ]);
    }

    [Fact]
    public async Task RunAsync_Path_PlanningFailure_Writes_Terminal_Event_And_Report()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/broken.json", "{");
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            Console = console
        });

        var exitCode = await app.RunAsync([
            "run",
            "--path", "/tmp/scenarios",
            "--events-jsonl", "/tmp/events.jsonl",
            "--report-json", "/tmp/report.json"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(2, exitCode);
        Assert.Equal(["scenario_run_started", "scenario_run_ended"], events.Select(static evt => evt.GetProperty("event").GetString()!).ToArray());
        Assert.Equal("failed", events[^1].GetProperty("status").GetString());
        Assert.Equal("usage_error", events[^1].GetProperty("error").GetProperty("category").GetString());
        Assert.Equal("failed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("usage_error", report.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(1, report.RootElement.GetProperty("failed_count").GetInt32());
        Assert.Equal("scenario discovery", report.RootElement.GetProperty("scenarios")[0].GetProperty("scenario").GetString());
    }

    [Fact]
    public async Task RunAsync_Path_PostPlanUsageFailure_Writes_Batch_Metadata_Report()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenarios/broken.json", """
        {
          "name": "broken",
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

        var exitCode = await app.RunAsync([
            "run",
            "--path", "/tmp/scenarios",
            "--events-jsonl", "/tmp/events.jsonl",
            "--report-json", "/tmp/report.json"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(2, exitCode);
        Assert.Equal(1, events[^1].GetProperty("total_count").GetInt32());
        Assert.Equal(1, events[^1].GetProperty("selected_count").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("total_count").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("selected_count").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("failed_count").GetInt32());
        Assert.Equal("scenario run", report.RootElement.GetProperty("scenarios")[0].GetProperty("scenario").GetString());
    }

    [Fact]
    public async Task RunAsync_File_Writes_Json_Report()
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

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--report-json", "/tmp/report.json"]);
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(0, exitCode);
        Assert.Equal("luotsi-scenario-run-report.v1", report.RootElement.GetProperty("schema").GetString());
        Assert.Equal("passed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("luotsi", report.RootElement.GetProperty("provenance").GetProperty("tool").GetString());
        Assert.True(report.RootElement.GetProperty("provenance").TryGetProperty("framework", out _));
        Assert.Equal(1, report.RootElement.GetProperty("passed_count").GetInt32());
        Assert.Equal("passed", report.RootElement.GetProperty("governance").GetProperty("kind").GetString());
        Assert.Equal("single", report.RootElement.GetProperty("scenarios")[0].GetProperty("scenario").GetString());
        Assert.Equal("/tmp/scenario.json::single", report.RootElement.GetProperty("scenarios")[0].GetProperty("scenario_id").GetString());
        Assert.Equal("/tmp/scenario.json", report.RootElement.GetProperty("scenarios")[0].GetProperty("file").GetString());
        Assert.Equal("passed", report.RootElement.GetProperty("scenarios")[0].GetProperty("governance").GetProperty("kind").GetString());
        Assert.Equal("sleep", report.RootElement.GetProperty("scenarios")[0].GetProperty("steps")[0].GetProperty("action").GetString());
        Assert.Equal(1, report.RootElement.GetProperty("metrics").GetProperty("step_count").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("scenarios")[0].GetProperty("metrics").GetProperty("action.sleep.count").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("scenarios")[0].GetProperty("steps")[0].GetProperty("metrics").GetProperty("configured_delay_ms").GetInt32());
    }

      [Fact]
      public async Task RunAsync_File_Report_Uses_Deterministic_ScenarioId_For_Parse_Failure()
      {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/broken.json", "{");
        var app = new App(new AppDependencies
        {
          TimeProvider = timeProvider,
          FileSystem = fileSystem,
          ProcessRunner = new DefaultProcessRunner(),
          Delay = new FakeDelay(timeProvider),
          DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
          Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/broken.json", "--report-json", "/tmp/report.json"]);
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(2, exitCode);
        Assert.Equal("/tmp/broken.json::broken", report.RootElement.GetProperty("scenarios")[0].GetProperty("scenario_id").GetString());
      }

    [Fact]
    public async Task RunAsync_Path_Writes_JUnit_Report_For_Mixed_Result()
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

        var exitCode = await app.RunAsync(["run", "--path", "/tmp/scenarios", "--report-junit", "/tmp/junit.xml"]);
        var report = XDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/junit.xml"));

        Assert.Equal(1, exitCode);
        Assert.Equal("testsuite", report.Root!.Name.LocalName);
        Assert.Equal("2", report.Root.Attribute("tests")!.Value);
        Assert.Equal("1", report.Root.Attribute("failures")!.Value);
        var failed = report.Root.Elements("testcase").Single(test => test.Attribute("name")!.Value == "fails");
        Assert.NotNull(failed.Element("failure"));
        Assert.Equal("/tmp/scenarios/fails.json", failed.Attribute("classname")!.Value);
        Assert.Equal("/tmp/scenarios/fails.json::fails", failed.Attribute("id")!.Value);
        Assert.Contains("metric: step_count=", failed.Element("system-out")!.Value, StringComparison.Ordinal);
        var suiteGovernance = report.Root.Element("properties")!.Elements("property").ToDictionary(
            static property => property.Attribute("name")!.Value,
            static property => property.Attribute("value")!.Value,
            StringComparer.Ordinal);
        Assert.Equal("scenario_observable_failure", suiteGovernance["luotsi.governance.kind"]);
        var failedGovernance = failed.Element("properties")!.Elements("property").ToDictionary(
            static property => property.Attribute("name")!.Value,
            static property => property.Attribute("value")!.Value,
            StringComparer.Ordinal);
        Assert.Equal("scenario_observable_failure", failedGovernance["luotsi.governance.kind"]);
        Assert.Equal("true", failedGovernance["luotsi.governance.regression_candidate"]);
    }

    [Fact]
    public async Task RunAsync_File_FailureArtifactCaptureFailure_Still_Emits_Step_Failure()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
            { "action": "waitVisible", "text": "Target" }
          ]
        }
        """);
        var host = new FakeDeviceHost
        {
            WaitVisibleException = new InvalidOperationException("not visible"),
            FailureArtifactException = new IOException("artifact disk full")
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

        var exitCode = await app.RunAsync([
            "run",
            "--file", "/tmp/scenario.json",
            "--events-jsonl", "/tmp/events.jsonl",
            "--report-json", "/tmp/report.json"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(1, exitCode);
        var failedStep = Assert.Single(events, static evt => evt.GetProperty("event").GetString() == "scenario_step_failed");
        Assert.Equal("not visible", failedStep.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain("artifact disk full", report.RootElement.GetProperty("scenarios")[0].GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_File_Failure_Report_Honors_AttachArtifacts_Never()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
            { "action": "waitVisible", "text": "Target" }
          ]
        }
        """);
        var host = CreateFailingHostWithArtifacts();
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--report-json", "/tmp/report.json", "--attach-artifacts", "never"]);
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(1, exitCode);
        Assert.Equal("failed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, report.RootElement.GetProperty("scenarios")[0].GetProperty("artifacts").GetArrayLength());
    }

    [Fact]
    public async Task RunAsync_File_Failure_Report_Attaches_Failure_Artifacts_By_Default()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
            { "action": "waitVisible", "text": "Target" }
          ]
        }
        """);
        var host = CreateFailingHostWithArtifacts();
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--report-json", "/tmp/report.json"]);
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));
        var artifacts = report.RootElement.GetProperty("scenarios")[0].GetProperty("artifacts");

        Assert.Equal(1, exitCode);
        Assert.Equal(2, artifacts.GetArrayLength());
        Assert.Equal("screenshot", artifacts[0].GetProperty("kind").GetString());
        Assert.Equal("failure.png", artifacts[0].GetProperty("file_name").GetString());
        Assert.Equal("metadata", artifacts[1].GetProperty("kind").GetString());
    }

      [Fact]
      public async Task RunAsync_File_Failure_Writes_Failure_Capsule_Manifest()
      {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
          { "action": "waitVisible", "text": "Target" }
          ]
        }
        """);
        var host = CreateFailingHostWithRichArtifacts();
        var app = new App(new AppDependencies
        {
          TimeProvider = timeProvider,
          FileSystem = fileSystem,
          ProcessRunner = new DefaultProcessRunner(),
          Delay = new FakeDelay(timeProvider),
          DeviceHostFactory = new FakeDeviceHostFactory(host),
          Console = console
        });

        var exitCode = await app.RunAsync([
          "run",
          "--file", "/tmp/scenario.json",
          "--artifacts", "/tmp/test-artifacts",
          "--report-json", "/tmp/report.json",
          "--report-junit", "/tmp/junit.xml"]);
        using var envelope = console.ParseSingleOutputAsJson();
        var artifactRoot = envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString();
        using var manifest = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(artifactRoot!, "failure-capsule.json")));

        Assert.Equal(1, exitCode);
        Assert.Equal(ResultSchemas.FailureCapsule, manifest.RootElement.GetProperty("schema").GetString());
        Assert.Equal("failed", manifest.RootElement.GetProperty("status").GetString());
        Assert.Equal("session-replay.json", manifest.RootElement.GetProperty("replayMetadataPath").GetString());
        Assert.Equal("session-timeline.jsonl", manifest.RootElement.GetProperty("replayTimelinePath").GetString());
        Assert.Equal("/tmp/report.json", manifest.RootElement.GetProperty("reports").GetProperty("jsonPath").GetString());
        Assert.Equal("/tmp/junit.xml", manifest.RootElement.GetProperty("reports").GetProperty("junitPath").GetString());
        Assert.Contains(manifest.RootElement.GetProperty("screenshots").EnumerateArray(), artifact => artifact.GetProperty("path").GetString() == "failure.png");
        Assert.Contains(manifest.RootElement.GetProperty("logcat").EnumerateArray(), artifact => artifact.GetProperty("path").GetString() == "failure-logcat.txt");
        Assert.Contains(manifest.RootElement.GetProperty("hierarchies").EnumerateArray(), artifact => artifact.GetProperty("path").GetString() == "failure-hierarchy.xml");
        Assert.Contains(manifest.RootElement.GetProperty("screenStates").EnumerateArray(), artifact => artifact.GetProperty("path").GetString() == "failure-screen-state.json");

        var failureBundle = Assert.Single(manifest.RootElement.GetProperty("failureBundles").EnumerateArray());
        Assert.Equal("failure.json", failureBundle.GetProperty("path").GetString());
        Assert.Contains(failureBundle.GetProperty("artifacts").EnumerateArray(), artifact => artifact.GetProperty("path").GetString() == "failure-logcat.txt");
      }

    [Fact]
    public async Task RunAsync_File_JUnit_Report_Attaches_Failure_Artifacts_In_SystemOut()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
            { "action": "waitVisible", "text": "Target" }
          ]
        }
        """);
        var host = CreateFailingHostWithArtifacts();
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--report-junit", "/tmp/junit.xml"]);
        var report = XDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/junit.xml"));
        var systemOut = Assert.Single(report.Root!.Elements("testcase")).Element("system-out")!.Value;

        Assert.Equal(1, exitCode);
        Assert.Contains("screenshot: failure.png", systemOut, StringComparison.Ordinal);
        Assert.Contains("metadata: failure.json", systemOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_File_CaptureOnNever_Skips_Failure_Artifact_Generation()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
            { "action": "waitVisible", "text": "Target" }
          ]
        }
        """);
        var host = CreateFailingHostWithArtifacts();
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--report-json", "/tmp/report.json", "--capture-on", "never"]);
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));

        Assert.Equal(1, exitCode);
        Assert.Empty(host.FailureArtifactRequests);
        Assert.Equal(0, report.RootElement.GetProperty("scenarios")[0].GetProperty("artifacts").GetArrayLength());
    }

    [Fact]
    public async Task RunAsync_File_Report_Attaches_Step_Artifacts_When_Always()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
            { "action": "captureArtifacts", "label": "checkpoint" }
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

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--report-json", "/tmp/report.json", "--attach-artifacts", "always"]);
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));
        var artifacts = report.RootElement.GetProperty("scenarios")[0].GetProperty("artifacts");

        Assert.Equal(0, exitCode);
        Assert.Equal(4, artifacts.GetArrayLength());
        Assert.Contains(artifacts.EnumerateArray(), artifact => artifact.GetProperty("kind").GetString() == "logcat");
    }

    [Fact]
    public async Task RunAsync_File_Failure_Report_Attaches_Prior_Step_Artifacts_When_Always()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "single",
          "steps": [
            { "action": "captureArtifacts", "label": "checkpoint" },
            { "action": "waitVisible", "text": "Target" }
          ]
        }
        """);
        var host = CreateFailingHostWithArtifacts();
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--report-json", "/tmp/report.json", "--attach-artifacts", "always"]);
        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));
        var artifacts = report.RootElement.GetProperty("scenarios")[0].GetProperty("artifacts");

        Assert.Equal(1, exitCode);
        Assert.Contains(artifacts.EnumerateArray(), artifact => artifact.GetProperty("file_name").GetString() == "checkpoint.png");
        Assert.Contains(artifacts.EnumerateArray(), artifact => artifact.GetProperty("file_name").GetString() == "failure.png");
    }

      [Fact]
      public async Task RunAsync_Path_Failure_Writes_Run_Plan_Metadata_In_Terminal_Event()
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
        var host = new FakeDeviceHost
        {
          DeviceFingerprintException = new UsageException("preflight fingerprint failed")
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

        var exitCode = await app.RunAsync(["run", "--path", "/tmp/scenarios", "--events-jsonl", "/tmp/events.jsonl"]);
        var events = ReadJsonlEvents(fileSystem, "/tmp/events.jsonl");

        Assert.Equal(2, exitCode);
        Assert.Equal("scenario_run_ended", events[^1].GetProperty("event").GetString());
        Assert.Equal("failed", events[^1].GetProperty("status").GetString());
        Assert.Equal(1, events[^1].GetProperty("total_count").GetInt32());
        Assert.Equal(1, events[^1].GetProperty("matched_count").GetInt32());
        Assert.Equal(1, events[^1].GetProperty("selected_count").GetInt32());
        Assert.Equal(0, events[^1].GetProperty("sharded_out_count").GetInt32());
        Assert.Equal("environment_failure", events[^1].GetProperty("governance").GetProperty("kind").GetString());
        Assert.Equal("high", events[^1].GetProperty("governance").GetProperty("confidence").GetString());
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

    [Fact]
    public async Task RunAsync_Path_DryRun_Can_Use_Hash_Shard_Strategy()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        foreach (var name in new[] { "a", "b", "c" })
        {
            fileSystem.AddFile($"/tmp/scenarios/{name}.json", $$"""
            {
              "name": "{{name}}",
              "steps": [
                { "action": "sleep", "milliseconds": 1 }
              ]
            }
            """);
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

        var exitCode = await app.RunAsync(["run", "--path", "/tmp/scenarios", "--dry-run", "--shard-count", "2", "--shard-index", "0", "--shard-strategy", "hash"]);
        using var envelope = console.ParseSingleOutputAsJson();
        var data = envelope.RootElement.GetProperty("data");

        Assert.Equal(0, exitCode);
        Assert.Equal("hash", data.GetProperty("shard_strategy").GetString());
        Assert.Equal(3, data.GetProperty("matched_count").GetInt32());
        Assert.Equal(3, data.GetProperty("selected_count").GetInt32() + data.GetProperty("sharded_out_count").GetInt32());
    }

    [Fact]
    public async Task RunAsync_Path_InvalidShardStrategy_Returns_Usage_Error()
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

        var exitCode = await app.RunAsync(["run", "--path", "/tmp/scenarios", "--dry-run", "--shard-strategy", "round-robin"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Contains("--shard-strategy", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
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
    public async Task RunScenarioAsync_Missing_Action_Throws_UsageException()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var runner = new DeviceRunner(new FakeAdbClient(), ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);
        var scenarios = new ScenarioExecutor(runner, fileSystem, timeProvider, new FakeDelay(timeProvider));
        var scenarioPath = "/tmp/missing-action.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "broken",
          "steps": [
            { "name": "missing action" }
          ]
        }
        """);

        var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

        Assert.Contains("must define a non-empty action", error.Message, StringComparison.Ordinal);
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
                adb.EnqueueShellResult(new ProcessResult(0, "Physical size: 1080x1920", string.Empty));
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
        adb.EnqueueShellResult(new ProcessResult(0, "Physical size: 1080x1920", string.Empty));
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
        adb.EnqueueShellResult(new ProcessResult(0, "Physical size: 1080x1920", string.Empty));
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

        var exitCode = await app.RunAsync(["run", "--file", scenarioPath, "--artifacts", "/tmp/test-artifacts", "--no-require-device-ready"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(1, exitCode);
        Assert.Equal("log_wait_timeout", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal("failed", envelope.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("wait for ready marker", envelope.RootElement.GetProperty("data").GetProperty("failed_step").GetProperty("name").GetString());
        Assert.Equal("scenario_observable_failure", envelope.RootElement.GetProperty("data").GetProperty("governance").GetProperty("kind").GetString());
        Assert.True(envelope.RootElement.GetProperty("data").GetProperty("governance").GetProperty("regression_candidate").GetBoolean());
        var failureArtifacts = envelope.RootElement.GetProperty("data").GetProperty("failure_artifacts");
        Assert.Equal(ResultSchemas.FailureBundle, failureArtifacts.GetProperty("schema").GetString());
        Assert.True(failureArtifacts.GetProperty("artifacts").GetArrayLength() >= 2);
    }

    [Fact]
    public async Task ScenarioList_Includes_Scenario_Metadata()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "coordinate smoke",
          "metadata": {
            "package": "dev.luotsi.app",
            "activity": ".MainActivity",
            "notes": "Uses fixed coordinates.",
            "device": {
              "model": "PDA3505",
              "androidRelease": "6.0",
              "sdk": "23"
            },
            "layout": {
              "width": 1920,
              "height": 1080,
              "orientation": "landscape"
            }
          },
          "steps": [
            { "action": "tapPoint", "x": 100, "y": 200 }
          ]
        }
        """);
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            Console = console
        });

        var exitCode = await app.RunAsync(["scenario-list", "--path", "/tmp/scenario.json"]);

        Assert.Equal(0, exitCode);
        using var envelope = console.ParseSingleOutputAsJson();
        var metadata = envelope.RootElement.GetProperty("data").GetProperty("scenarios")[0].GetProperty("metadata");
        Assert.Equal("dev.luotsi.app", metadata.GetProperty("package").GetString());
        Assert.Equal("PDA3505", metadata.GetProperty("device").GetProperty("model").GetString());
        Assert.Equal(1920, metadata.GetProperty("layout").GetProperty("width").GetInt32());
    }

    [Fact]
    public async Task RunAsync_File_Adds_Metadata_Warnings_When_Device_Context_Differs()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new SteppingTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind), TimeSpan.FromMilliseconds(1));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "coordinate smoke",
          "metadata": {
            "package": "dev.actual",
            "activity": ".ExpectedActivity",
            "device": {
              "model": "Expected Model",
              "androidRelease": "15",
              "sdk": "35"
            },
            "layout": {
              "width": 1080,
              "height": 2400,
              "orientation": "landscape"
            }
          },
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult(
                "Actual Model",
                "6.0",
                "23",
                "mCurrentFocus=Window{1 u0 dev.actual.debug/.MainActivity}",
                null,
                null,
                "fingerprint",
                "armeabi-v7a",
                "SER",
                "dev.actual.debug",
                720,
                1280,
                "portrait")
        };
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new SteppingDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--report-json", "/tmp/report.json"]);

        Assert.Equal(0, exitCode);
        Assert.Equal([null], host.ReadOnlyPreflightRequests);
        using var envelope = console.ParseSingleOutputAsJson();
        var warnings = envelope.RootElement.GetProperty("data").GetProperty("metadata_warnings");
        Assert.Contains(warnings.EnumerateArray(), warning => warning.GetProperty("code").GetString() == "package");
        Assert.Contains(warnings.EnumerateArray(), warning => warning.GetProperty("code").GetString() == "device_model");
        Assert.Contains(warnings.EnumerateArray(), warning => warning.GetProperty("code").GetString() == "layout_width");

        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));
        var reportWarnings = report.RootElement.GetProperty("scenarios")[0].GetProperty("metadata_warnings");
        Assert.Contains(reportWarnings.EnumerateArray(), warning => warning.GetProperty("code").GetString() == "android_sdk");
        Assert.Contains(reportWarnings.EnumerateArray(), warning => warning.GetProperty("code").GetString() == "layout_orientation");
    }

    [Fact]
    public async Task RunAsync_File_Failure_Preserves_Metadata_Warnings_When_Device_Context_Differs()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new SteppingTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind), TimeSpan.FromMilliseconds(1));
        var console = new FakeConsole();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "coordinate smoke",
          "metadata": {
            "package": "dev.actual",
            "device": {
              "model": "Expected Model"
            },
            "layout": {
              "width": 1080,
              "height": 2400
            }
          },
          "steps": [
            { "action": "waitVisible", "text": "Target" }
          ]
        }
        """);
        var host = new FakeDeviceHost
        {
            WaitVisibleException = new InvalidOperationException("not visible"),
            PreflightTemplate = new PreflightResult(
                "Actual Model",
                "6.0",
                "23",
                "mCurrentFocus=Window{1 u0 dev.actual.debug/.MainActivity}",
                null,
                null,
                "fingerprint",
                "armeabi-v7a",
                "SER",
                "dev.actual.debug",
                720,
                1280,
                "portrait")
        };
        var app = new App(new AppDependencies
        {
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            ProcessRunner = new DefaultProcessRunner(),
            Delay = new SteppingDelay(timeProvider),
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            Console = console
        });

        var exitCode = await app.RunAsync(["run", "--file", "/tmp/scenario.json", "--report-json", "/tmp/report.json"]);

        Assert.Equal(1, exitCode);
        using var envelope = console.ParseSingleOutputAsJson();
        var warnings = envelope.RootElement.GetProperty("data").GetProperty("metadata_warnings");
        Assert.Contains(warnings.EnumerateArray(), warning => warning.GetProperty("code").GetString() == "package");
        Assert.Contains(warnings.EnumerateArray(), warning => warning.GetProperty("code").GetString() == "layout_width");

        using var report = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/report.json"));
        var reportWarnings = report.RootElement.GetProperty("scenarios")[0].GetProperty("metadata_warnings");
        Assert.Contains(reportWarnings.EnumerateArray(), warning => warning.GetProperty("code").GetString() == "package");
        Assert.Contains(reportWarnings.EnumerateArray(), warning => warning.GetProperty("code").GetString() == "layout_height");
    }

    [Fact]
    public async Task RunScenarioAsync_AssertScreenshot_Action_Uses_Visual_Assertions()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var host = new FakeDeviceHost
        {
            AssertScreenshotObservedWidth = 320,
            AssertScreenshotObservedHeight = 241,
            AssertScreenshotObservedSha256 = "observed-home-sha"
        };
        var scenarioPath = "/tmp/assert-screenshot.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "visual smoke",
          "steps": [
            { "name": "home screenshot", "action": "assertScreenshot", "label": "home", "expectedWidth": 320 }
          ]
        }
        """);
        var scenarios = new ScenarioExecutor(host, fileSystem, timeProvider, new FakeDelay(timeProvider));

        var result = await scenarios.RunAsync(scenarioPath);
        var json = SerializeToJsonElement(result);
        var stepResult = json.GetProperty("steps")[0].GetProperty("result");

        Assert.Equal("assertScreenshot", json.GetProperty("steps")[0].GetProperty("action").GetString());
        Assert.Equal("home", stepResult.GetProperty("label").GetString());
        Assert.Equal(320, stepResult.GetProperty("width").GetInt32());
        Assert.Equal(241, stepResult.GetProperty("height").GetInt32());
        Assert.Equal("observed-home-sha", stepResult.GetProperty("sha256").GetString());
        Assert.Equal(320, stepResult.GetProperty("expected_width").GetInt32());
        var request = Assert.Single(host.AssertScreenshotRequests);
        Assert.Equal("home", request.Label);
        Assert.Equal(320, request.ExpectedWidth);
        Assert.Null(request.ExpectedHeight);
        Assert.Null(request.ExpectedSha256);
        Assert.Empty(host.TakeScreenshotRequests);
    }

    [Fact]
    public async Task RunScenarioAsync_AssertScreenshot_Requires_Baseline_When_UpdateBaseline_Is_True()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var host = new FakeDeviceHost();
        var scenarioPath = "/tmp/assert-screenshot-update-baseline.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "visual smoke",
          "steps": [
            { "name": "home screenshot", "action": "assertScreenshot", "label": "home", "updateBaseline": true }
          ]
        }
        """);
        var scenarios = new ScenarioExecutor(host, fileSystem, timeProvider, new FakeDelay(timeProvider));

        var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

        Assert.Contains("updateBaseline requires baselineFile", error.Message, StringComparison.Ordinal);
        Assert.Empty(host.AssertScreenshotRequests);
    }

    [Fact]
    public async Task RunScenarioAsync_AssertScreenshot_Region_Requires_Real_Assertion()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var host = new FakeDeviceHost();
        var scenarioPath = "/tmp/assert-screenshot-region-only.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "visual smoke",
          "steps": [
            { "name": "home screenshot", "action": "assertScreenshot", "label": "home", "regionX": 0, "regionY": 0, "regionWidth": 100, "regionHeight": 100 }
          ]
        }
        """);
        var scenarios = new ScenarioExecutor(host, fileSystem, timeProvider, new FakeDelay(timeProvider));

        var error = await Assert.ThrowsAsync<UsageException>(() => scenarios.RunAsync(scenarioPath));

        Assert.Contains("assertScreenshot requires expectedWidth", error.Message, StringComparison.Ordinal);
        Assert.Empty(host.AssertScreenshotRequests);
    }

    [Fact]
    public async Task RunScenarioAsync_AssertScreenshot_Action_Fails_When_Host_Reports_Mismatch()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var host = new FakeDeviceHost
        {
            AssertScreenshotException = new InvalidOperationException("Screenshot 'home-screenshot.png' width was 400; expected 320.")
        };
        var scenarioPath = "/tmp/assert-screenshot-fails.json";
        fileSystem.AddFile(scenarioPath, """
        {
          "name": "visual smoke",
          "steps": [
            { "name": "home screenshot", "action": "assertScreenshot", "label": "home", "expectedWidth": 320 }
          ]
        }
        """);
        var scenarios = new ScenarioExecutor(host, fileSystem, timeProvider, new FakeDelay(timeProvider));

        var error = await Assert.ThrowsAsync<ScenarioStepFailureException>(() => scenarios.RunAsync(scenarioPath));

        var failure = ScenarioFailureDetails.TryGetData(error);
        Assert.NotNull(failure);
        Assert.Equal("failed", failure.Status);
        Assert.Equal("home screenshot", failure.FailedStep.Name);
        Assert.Equal("assertScreenshot", failure.FailedStep.Action);
        Assert.Equal("Screenshot 'home-screenshot.png' width was 400; expected 320.", failure.FailureArtifacts.ErrorMessage);
        Assert.Equal("failed", failure.Steps[0].Status);
        Assert.Contains("expected 320", failure.Steps[0].Error?.Message, StringComparison.Ordinal);
        var request = Assert.Single(host.AssertScreenshotRequests);
        Assert.Equal("home", request.Label);
        Assert.Equal(320, request.ExpectedWidth);
    }

    private static JsonElement[] ReadJsonlEvents(FakeFileSystem fileSystem, string path) =>
        fileSystem.ReadAllTextAsync(path).GetAwaiter().GetResult()
        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();

    private static async Task AssertRunReplayArtifactsAsync(FakeFileSystem fileSystem, string artifactRoot, string target, string[] expectedTimelineTypes)
    {
      var timelinePath = Path.Join(artifactRoot, "session-timeline.jsonl");
      var replayPath = Path.Join(artifactRoot, "session-replay.json");

      Assert.True(fileSystem.FileExists(timelinePath));
      Assert.True(fileSystem.FileExists(replayPath));

      var timeline = ReadJsonlEvents(fileSystem, timelinePath);
      using var replay = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(replayPath));

      Assert.Equal(expectedTimelineTypes, timeline.Select(static evt => evt.GetProperty("type").GetString()!).ToArray());
      Assert.Equal(ResultSchemas.SessionReplay, replay.RootElement.GetProperty("schema").GetString());
      Assert.Equal("run", replay.RootElement.GetProperty("sessionKind").GetString());
      Assert.Equal(target, replay.RootElement.GetProperty("target").GetString());
      Assert.Equal("session-timeline.jsonl", replay.RootElement.GetProperty("timelineFileName").GetString());
      Assert.Equal(expectedTimelineTypes.Length, replay.RootElement.GetProperty("eventCount").GetInt32());
      Assert.Equal(
        expectedTimelineTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
        replay.RootElement.GetProperty("eventTypes").EnumerateArray().Select(static item => item.GetString()).ToArray());
    }

    private static FakeDeviceHost CreateFailingHostWithArtifacts() =>
        new()
        {
            WaitVisibleException = new InvalidOperationException("not visible"),
            FailureArtifacts = new FailureArtifactBundle(
                ResultSchemas.FailureBundle,
                DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                "scenario",
                "single",
                "/tmp/scenario.json",
                1,
                "waitVisible",
                "waitVisible",
                typeof(InvalidOperationException).FullName!,
                "not visible",
                [new FailureArtifact("screenshot", "failure.png")],
                [])
            {
                MetadataFile = "failure.json"
            }
        };

        private static FakeDeviceHost CreateFailingHostWithRichArtifacts() =>
          new()
          {
            WaitVisibleException = new InvalidOperationException("not visible"),
            FailureArtifacts = new FailureArtifactBundle(
              ResultSchemas.FailureBundle,
              DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
              "scenario",
              "single",
              "/tmp/scenario.json",
              1,
              "waitVisible",
              "waitVisible",
              typeof(InvalidOperationException).FullName!,
              "not visible",
              [
                new FailureArtifact("screenshot", "failure.png"),
                new FailureArtifact("logcat", "failure-logcat.txt"),
                new FailureArtifact("hierarchy", "failure-hierarchy.xml"),
                new FailureArtifact("screen_state", "failure-screen-state.json")
              ],
              [])
            {
              MetadataFile = "failure.json"
            }
          };

          private sealed class ThrowingScenarioEventSink(Exception exception) : IScenarioEventSink
          {
            public Task EmitAsync(ScenarioEvent scenarioEvent) => Task.FromException(exception);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
          }

}
