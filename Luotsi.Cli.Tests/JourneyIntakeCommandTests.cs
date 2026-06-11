using System.Text.Json;
using Luotsi.Cli.Cli;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class JourneyIntakeCommandTests
{
    [Fact]
    public async Task RunAsync_JourneyIntakeInit_Writes_Reviewable_Intake_And_Markdown_Without_Creating_Runner()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var deviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost());
        using var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = deviceHostFactory,
            ViewProfileStore = new FakeViewProfileStore()
        });

        var exitCode = await app.RunAsync([
            "journey-intake",
            "init",
            "--output",
            "/tmp/journey-intake.json",
            "--name",
            "checkout-smoke",
            "--package",
            "com.example.shop",
            "--activity",
            ".CheckoutActivity",
            "--device",
            "emulator-5554",
            "--scenario",
            "scenarios/checkout smoke.json",
            "--artifacts",
            "artifacts/checkout-intake",
            "--run-artifacts",
            "artifacts/from journey run",
            "--write-markdown"
        ]);
        using var envelope = console.ParseSingleOutputAsJson();
        using var intake = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/journey-intake.json"));

        Assert.Equal(0, exitCode);
        Assert.Equal(0, deviceHostFactory.CreateCallCount);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("initialized", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("written").GetBoolean());
        Assert.Equal("/tmp/journey-intake.json", data.GetProperty("output").GetString());
        var markdownPath = data.GetProperty("markdown_path").GetString();
        Assert.EndsWith("journey-intake.md", markdownPath, StringComparison.Ordinal);
        Assert.Equal("com.example.shop", data.GetProperty("package").GetString());
        Assert.Equal("emulator-5554", data.GetProperty("device_serial").GetString());
        Assert.Equal("luotsi run --file \"scenarios/checkout smoke.json\" --device emulator-5554 --dry-run", data.GetProperty("handoff").GetProperty("dry_run_command").GetString());
        Assert.Contains(
            "luotsi replay capsule --artifacts \"artifacts/from journey run\" --write-readme --write-json",
            data.GetProperty("next_commands").EnumerateArray().Select(static command => command.GetString()));
        Assert.Equal("luotsi-journey-intake.v1", intake.RootElement.GetProperty("schema").GetString());
        Assert.Equal("https://digablesolutions.github.io/luotsi/schemas/luotsi-journey-intake.v1.schema.json", intake.RootElement.GetProperty("$schema").GetString());
        Assert.True(intake.RootElement.GetProperty("guardrails").GetProperty("reviewRequired").GetBoolean());
        Assert.True(intake.RootElement.GetProperty("guardrails").GetProperty("doNotExecuteAsNaturalLanguage").GetBoolean());
        Assert.Equal("com.example.shop", intake.RootElement.GetProperty("app").GetProperty("package").GetString());
        Assert.Equal("luotsi replay scenario-draft --artifacts artifacts/checkout-intake/<run-id> --output \"scenarios/checkout smoke.json\" --validate --write-markdown", intake.RootElement.GetProperty("luotsiHandoff").GetProperty("draftCommand").GetString());
        var markdown = await fileSystem.ReadAllTextAsync(markdownPath!);
        Assert.Contains("Keep `reviewRequired` true.", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi journey-intake validate --file /tmp/journey-intake.json", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi replay packet --artifacts \"artifacts/from journey run\"", markdown, StringComparison.Ordinal);
        Assert.Contains("luotsi replay capsule --artifacts \"artifacts/from journey run\" --write-readme --write-json", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_JourneyIntakeInit_Output_Validates_And_Drafts_Scenario()
    {
        var fileSystem = new FakeFileSystem();
        var deviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost());
        using (var app = new App(new AppDependencies
        {
            Console = new FakeConsole(),
            FileSystem = fileSystem,
            DeviceHostFactory = deviceHostFactory,
            ViewProfileStore = new FakeViewProfileStore()
        }))
        {
            var initExitCode = await app.RunAsync([
                "journey-intake",
                "init",
                "--output",
                "/tmp/journey-intake.json",
                "--package",
                "com.example.shop",
                "--device",
                "emulator-5554",
                "--scenario",
                "/tmp/scenarios/checkout.json"
            ]);

            Assert.Equal(0, initExitCode);
        }

        var validateConsole = new FakeConsole();
        using (var app = new App(new AppDependencies
        {
            Console = validateConsole,
            FileSystem = fileSystem,
            DeviceHostFactory = deviceHostFactory,
            ViewProfileStore = new FakeViewProfileStore()
        }))
        {
            var validateExitCode = await app.RunAsync(["journey-intake", "validate", "--file", "/tmp/journey-intake.json"]);
            using var envelope = validateConsole.ParseSingleOutputAsJson();

            Assert.Equal(0, validateExitCode);
            Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("validated", envelope.RootElement.GetProperty("data").GetProperty("status").GetString());
        }

        var draftConsole = new FakeConsole();
        using (var app = new App(new AppDependencies
        {
            Console = draftConsole,
            FileSystem = fileSystem,
            DeviceHostFactory = deviceHostFactory,
            ViewProfileStore = new FakeViewProfileStore()
        }))
        {
            var draftExitCode = await app.RunAsync(["journey-intake", "draft-scenario", "--file", "/tmp/journey-intake.json", "--output", "/tmp/scenarios/checkout.json"]);
            using var envelope = draftConsole.ParseSingleOutputAsJson();
            using var scenario = JsonDocument.Parse(await fileSystem.ReadAllTextAsync("/tmp/scenarios/checkout.json"));

            Assert.Equal(0, draftExitCode);
            Assert.Equal("drafted", envelope.RootElement.GetProperty("data").GetProperty("status").GetString());
            Assert.Equal("evidence-backed-journey", scenario.RootElement.GetProperty("name").GetString());
            Assert.Contains("journey-intake", scenario.RootElement.GetProperty("tags").EnumerateArray().Select(static tag => tag.GetString()));
        }

        Assert.Equal(0, deviceHostFactory.CreateCallCount);
    }

    [Fact]
    public async Task RunAsync_JourneyIntakeInit_Existing_Output_Returns_Usage_Error_Without_Overwrite()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile("/tmp/journey-intake.json", "{}");
        using var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            ViewProfileStore = new FakeViewProfileStore()
        });

        var exitCode = await app.RunAsync(["journey-intake", "init", "--output", "/tmp/journey-intake.json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("already exists", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_JourneyIntakeInit_Existing_Markdown_Returns_Usage_Error_Without_Overwrite()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var markdownPath = Path.Join(Path.GetDirectoryName("/tmp/journey-intake.json"), "journey-intake.md");
        fileSystem.AddFile(markdownPath, "existing markdown");
        using var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            ViewProfileStore = new FakeViewProfileStore()
        });

        var exitCode = await app.RunAsync(["journey-intake", "init", "--output", "/tmp/journey-intake.json", "--write-markdown"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("markdown file", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists("/tmp/journey-intake.json"));
    }

    [Fact]
    public async Task RunAsync_JourneyIntakeInit_Blank_Device_Uses_Normalized_Device_In_Next_Commands()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        using var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            ViewProfileStore = new FakeViewProfileStore()
        });

        var exitCode = await app.RunAsync(["journey-intake", "init", "--output", "/tmp/journey-intake.json", "--device", " "]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var commands = envelope.RootElement.GetProperty("data").GetProperty("next_commands").EnumerateArray().Select(static command => command.GetString()).ToArray();
        Assert.Contains("luotsi run --file scenarios/from-journey.json --device <serial> --dry-run", commands);
    }

    [Fact]
    public async Task RunAsync_JourneyIntakeInit_WriteReadme_Returns_Usage_Error()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        using var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            ViewProfileStore = new FakeViewProfileStore()
        });

        var exitCode = await app.RunAsync(["journey-intake", "init", "--output", "/tmp/journey-intake.json", "--write-readme"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("--write-markdown", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists("/tmp/journey-intake.json"));
    }

    [Fact]
    public async Task RunAsync_JourneyIntakeValidate_Returns_Handoff_Without_Creating_Runner()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var deviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost());
        fileSystem.AddFile("/tmp/journey.json", ValidJourneyIntake);
        using var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = deviceHostFactory,
            ViewProfileStore = new FakeViewProfileStore()
        });

        var exitCode = await app.RunAsync(["journey-intake", "validate", "--file", "/tmp/journey.json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal(0, deviceHostFactory.CreateCallCount);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("validated", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("valid").GetBoolean());
        Assert.Equal("luotsi inspect --device <serial> --artifacts artifacts/journey-intake", data.GetProperty("handoff").GetProperty("explore_command").GetString());
        Assert.Empty(data.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task RunAsync_JourneyIntakeValidate_Invalid_Contract_Returns_Nonzero_With_Errors()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile("/tmp/journey.json", """
        {
          "schema": "luotsi-journey-intake.v1",
          "journey": {
            "userGoal": "Open settings",
            "startingState": "App starts closed.",
            "steps": [],
            "assertions": []
          },
          "guardrails": {
            "reviewRequired": false,
            "doNotExecuteAsNaturalLanguage": false,
            "unsafeActionsToAvoid": [],
            "preferredSelectors": []
          },
          "luotsiHandoff": {
            "readinessCommand": "luotsi doctor --device <serial>",
            "exploreCommand": "inspect --device <serial>",
            "discoveryCommand": "luotsi discover --device <serial>",
            "draftCommand": "luotsi replay scenario-draft --artifacts <root> --output scenario.json",
            "dryRunCommand": "luotsi run --file scenario.json",
            "runCommand": "luotsi run --file scenario.json",
            "claimedRunCommand": "luotsi run --file scenario.json",
            "replayCommand": "luotsi replay open --artifacts artifacts"
          },
          "review": {
            "owner": "",
            "approvedAt": "",
            "notes": ""
          }
        }
        """);
        using var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            ViewProfileStore = new FakeViewProfileStore()
        });

        var exitCode = await app.RunAsync(["journey-intake", "validate", "--file", "/tmp/journey.json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(1, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("failed", data.GetProperty("status").GetString());
        Assert.False(data.GetProperty("valid").GetBoolean());
        var errors = data.GetProperty("errors").EnumerateArray().Select(static error => error.GetString()).ToArray();
        Assert.Contains("$.name must be a string.", errors);
        Assert.Contains("$.source must be an object.", errors);
        Assert.Contains("$.app must be an object.", errors);
        Assert.Contains("$.guardrails.reviewRequired must be true.", errors);
        Assert.Contains("$.luotsiHandoff.exploreCommand must start with 'luotsi inspect '.", errors);
        Assert.Contains("$.luotsiHandoff.dryRunCommand must include ' --dry-run'.", errors);
        Assert.Contains("$.luotsiHandoff.claimedRunCommand must include ' --claim-device'.", errors);
    }

    [Fact]
    public async Task RunAsync_JourneyIntakeValidate_Rejects_Prefix_Matched_Handoff_Flags()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile(
            "/tmp/journey.json",
            ValidJourneyIntake
                .Replace(" --dry-run", " --dry-runner", StringComparison.Ordinal)
                .Replace(" --claim-device", " --claim-deviceX", StringComparison.Ordinal));
        using var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            ViewProfileStore = new FakeViewProfileStore()
        });

        var exitCode = await app.RunAsync(["journey-intake", "validate", "--file", "/tmp/journey.json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(1, exitCode);
        var errors = envelope.RootElement.GetProperty("data").GetProperty("errors").EnumerateArray().Select(static error => error.GetString()).ToArray();
        Assert.Contains("$.luotsiHandoff.dryRunCommand must include ' --dry-run'.", errors);
        Assert.Contains("$.luotsiHandoff.claimedRunCommand must include ' --claim-device'.", errors);
    }

    [Fact]
    public async Task RunAsync_JourneyIntakeValidate_Missing_File_Returns_Usage_Error()
    {
        var console = new FakeConsole();
        using var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = new FakeFileSystem(),
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            ViewProfileStore = new FakeViewProfileStore()
        });

        var exitCode = await app.RunAsync(["journey-intake", "validate", "--file", "/tmp/missing.json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task RunAsync_JourneyIntakeDraftScenario_Writes_Review_Required_Scenario_Without_Creating_Runner()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var deviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost());
        fileSystem.AddFile("/tmp/journey.json", ValidJourneyIntake);
        using var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = deviceHostFactory,
            ViewProfileStore = new FakeViewProfileStore()
        });

        var output = "/tmp/scenarios/settings \"draft\".json";
        var exitCode = await app.RunAsync(["journey-intake", "Draft-Scenario", "--file", "/tmp/journey.json", "--output", output]);
        using var envelope = console.ParseSingleOutputAsJson();
        using var scenario = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(output));

        Assert.Equal(0, exitCode);
        Assert.Equal(0, deviceHostFactory.CreateCallCount);
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("drafted", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("written").GetBoolean());
        Assert.Equal(output, data.GetProperty("output").GetString());
        Assert.Contains(
            "luotsi scenario-validate --file \"/tmp/scenarios/settings \\\"draft\\\".json\"",
            data.GetProperty("next_commands").EnumerateArray().Select(static command => command.GetString()));
        Assert.Equal("settings-smoke", scenario.RootElement.GetProperty("name").GetString());
        Assert.Contains("review-required", scenario.RootElement.GetProperty("tags").EnumerateArray().Select(static tag => tag.GetString()));
        Assert.Equal("com.example.app", scenario.RootElement.GetProperty("metadata").GetProperty("package").GetString());
        var notes = scenario.RootElement.GetProperty("metadata").GetProperty("notes").GetString();
        Assert.Contains("does not execute natural-language", notes, StringComparison.Ordinal);
        Assert.Contains("Source notes: Imported from Android CLI Journey intent.", notes, StringComparison.Ordinal);
        Assert.Equal("startApp", scenario.RootElement.GetProperty("setup")[0].GetProperty("action").GetString());
        Assert.Equal("takeScreenshot", scenario.RootElement.GetProperty("steps")[0].GetProperty("action").GetString());
    }

    [Fact]
    public async Task RunAsync_JourneyIntakeDraftScenario_Existing_Output_Returns_Usage_Error_Without_Overwrite()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile("/tmp/journey.json", ValidJourneyIntake);
        fileSystem.AddFile("/tmp/scenarios/settings.json", "{}");
        using var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost()),
            ViewProfileStore = new FakeViewProfileStore()
        });

        var exitCode = await app.RunAsync(["journey-intake", "draft-scenario", "--file", "/tmp/journey.json", "--output", "/tmp/scenarios/settings.json"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("already exists", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private const string ValidJourneyIntake = """
    {
      "schema": "luotsi-journey-intake.v1",
      "name": "settings-smoke",
      "source": {
        "kind": "android-cli-journey-intent",
        "notes": "Imported from Android CLI Journey intent."
      },
      "app": {
        "package": "com.example.app",
        "activity": ".MainActivity"
      },
      "device": {
        "query": "state=online,type=physical,availability=available",
        "orientation": "portrait"
      },
      "journey": {
        "userGoal": "Open settings",
        "startingState": "App starts closed.",
        "steps": [
          "Launch the app"
        ],
        "assertions": [
          "Settings is visible"
        ]
      },
      "guardrails": {
        "reviewRequired": true,
        "doNotExecuteAsNaturalLanguage": true,
        "unsafeActionsToAvoid": [
          "Do not use production accounts."
        ],
        "preferredSelectors": [
          "Prefer resourceId selectors."
        ]
      },
      "luotsiHandoff": {
        "readinessCommand": "luotsi doctor --device <serial> --fix",
        "exploreCommand": "luotsi inspect --device <serial> --artifacts artifacts/journey-intake",
        "discoveryCommand": "luotsi discover --device <serial> --package com.example.app --artifacts artifacts/journey-intake",
        "draftCommand": "luotsi replay scenario-draft --artifacts artifacts/journey-intake --output scenarios/settings.json --validate",
        "dryRunCommand": "luotsi run --file scenarios/settings.json --device <serial> --dry-run",
        "runCommand": "luotsi run --file scenarios/settings.json --device <serial>",
        "claimedRunCommand": "luotsi run --file scenarios/settings.json --device-query \"state=online,type=physical,availability=available\" --claim-device",
        "replayCommand": "luotsi replay open --artifacts artifacts/settings-run --dry-run"
      },
      "review": {
        "owner": "",
        "approvedAt": "",
        "notes": ""
      }
    }
    """;
}
