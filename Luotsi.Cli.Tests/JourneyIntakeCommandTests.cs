using Luotsi.Cli.Cli;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class JourneyIntakeCommandTests
{
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
