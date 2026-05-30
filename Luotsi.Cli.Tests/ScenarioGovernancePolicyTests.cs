using System.Text.Json;
using System.Xml.Linq;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Routing;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class ScenarioGovernancePolicyTests
{
    [Fact]
    public async Task ApplyAsync_AutoQuarantines_Device_And_Requires_PassThreshold_To_Release()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-28T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LOCALAPPDATA"] = @"C:\Users\Test\AppData\Local"
        });
        var quarantineStore = new LabQuarantineStore(fileSystem, timeProvider, environment);
        var registry = new ScenarioDeviceHealthRegistry(fileSystem, timeProvider, environment);
        var coordinator = new ScenarioGovernancePolicyCoordinator(registry, quarantineStore);
        var configuration = CreateConfiguration(ScenarioCiPolicyMode.Enforced, retryBudget: 1, passThreshold: 2);
        var allocation = CreateAllocation("usb-1", "offline", "unavailable");

        var firstFailure = await coordinator.ApplyAsync(
            CreateRunResult("failed", allocation, CreateLabInfrastructureFailure(quarantineCandidate: false)),
            configuration);

        Assert.Equal("suspect", firstFailure.DeviceHealth!.State);
        Assert.False(firstFailure.DeviceHealth.AutoQuarantined);
        Assert.Null(quarantineStore.TryGetBySerial("usb-1"));
        Assert.Equal(20, firstFailure.CiPolicy!.RecommendedExitCode);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var secondFailure = await coordinator.ApplyAsync(
            CreateRunResult("failed", allocation, CreateLabInfrastructureFailure(quarantineCandidate: false)),
            configuration);

        Assert.Equal("quarantined", secondFailure.DeviceHealth!.State);
        Assert.True(secondFailure.DeviceHealth.AutoQuarantined);
        Assert.NotNull(secondFailure.DeviceHealth.RegistryFile);
        Assert.Contains(@"C:\Users\Test\AppData\Local\Luotsi\lab\device-health", secondFailure.DeviceHealth.RegistryFile!, StringComparison.Ordinal);
        var quarantine = quarantineStore.TryGetBySerial("usb-1");
        Assert.NotNull(quarantine);
        Assert.Equal("automatic", quarantine!.Source);
        Assert.Contains(@"C:\Users\Test\AppData\Local\Luotsi\lab\quarantines", quarantine.QuarantineFile, StringComparison.Ordinal);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var firstPass = await coordinator.ApplyAsync(
            CreateRunResult("passed", allocation with { Device = allocation.Device! with { State = "device", Availability = "available" } }, ScenarioGovernanceClassifier.FromStatus("passed", allocation)),
            configuration);

        Assert.Equal("recovering", firstPass.DeviceHealth!.State);
        Assert.False(firstPass.DeviceHealth.PassThresholdSatisfied);
        Assert.NotNull(quarantineStore.TryGetBySerial("usb-1"));

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var secondPass = await coordinator.ApplyAsync(
            CreateRunResult("passed", allocation with { Device = allocation.Device! with { State = "device", Availability = "available" } }, ScenarioGovernanceClassifier.FromStatus("passed", allocation)),
            configuration);

        Assert.Equal("healthy", secondPass.DeviceHealth!.State);
        Assert.True(secondPass.DeviceHealth.PassThresholdSatisfied);
        Assert.Null(quarantineStore.TryGetBySerial("usb-1"));
    }

    [Fact]
    public async Task RecordAsync_Writes_Registry_Without_Leaving_Temporary_Files()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-28T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LOCALAPPDATA"] = @"C:\Users\Test\AppData\Local"
        });
        var registry = new ScenarioDeviceHealthRegistry(fileSystem, timeProvider, environment);

        var snapshot = await registry.RecordAsync(
            "usb-1",
            "failed",
            CreateLabInfrastructureFailure(),
            CreateConfiguration(ScenarioCiPolicyMode.Advisory, retryBudget: 1, passThreshold: 2));

        var files = fileSystem.GetFiles(@"C:\Users\Test\AppData\Local\Luotsi\lab\device-health", "*", SearchOption.AllDirectories);
        Assert.Contains(files, file => string.Equals(file, snapshot.RegistryFile, StringComparison.Ordinal));
        Assert.DoesNotContain(files, file => file.Contains(".tmp-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecordAsync_Uses_Shared_Lab_State_Root_When_Configured()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-28T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            [LabStateStoreFactory.SharedRootEnvironmentVariable] = @"C:\lab-state"
        });
        var registry = new ScenarioDeviceHealthRegistry(fileSystem, timeProvider, environment);

        var snapshot = await registry.RecordAsync(
            "usb-1",
            "failed",
            CreateLabInfrastructureFailure(),
            CreateConfiguration(ScenarioCiPolicyMode.Advisory, retryBudget: 1, passThreshold: 2));

        Assert.Equal(Path.Join(@"C:\lab-state", "device-health", "usb-1.json"), snapshot.RegistryFile);
        Assert.True(fileSystem.FileExists(snapshot.RegistryFile!));
    }

    [Fact]
    public async Task JUnitScenarioRunReportWriter_Writes_DeviceHealth_And_CiPolicy_Properties()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.CreateDirectory(@"C:\tmp");
        var writer = new JUnitScenarioRunReportWriter(fileSystem, @"C:\tmp\junit.xml");
        var scenarioDeviceHealth = new ScenarioDeviceHealthSnapshot(
            "luotsi-device-health.v1",
            "usb-1",
            "recovering",
            DateTimeOffset.Parse("2026-05-28T09:00:01Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            30,
            2,
            1,
            2,
            1,
            1,
            1,
            2,
            false,
            true);
        var scenarioCiPolicy = new ScenarioCiPolicyResult(
            "advisory",
            "device_recovering",
            10,
            false,
            1,
            1,
            2,
            false,
            true,
            false,
            "Device is recovering.");
        var report = new ScenarioRunReport(
            "luotsi-scenario-run-report.v1",
            "/tmp/scenarios",
            "failed",
            DateTimeOffset.Parse("2026-05-28T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse("2026-05-28T09:00:01Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            1000,
            1,
            1,
            1,
            0,
            1,
            0,
            null,
            null,
            null,
            ScenarioMetrics.Empty,
            CreateAllocation("usb-1", "offline", "unavailable"),
            CreateProvenance(),
            [
                new ScenarioReportScenario(
                    "login smoke",
                    "scenarios/login.json::login smoke",
                    "failed",
                    "/tmp/scenarios/login.json",
                    1000,
                    new ScenarioRunTiming(1000, 50, 900, 50),
                    ScenarioMetrics.Empty,
                    [],
                    null,
                    [],
                    new ErrorInfo("System.InvalidOperationException", "device offline", "configuration_error"),
                    Governance: CreateLabInfrastructureFailure(),
                    DeviceHealth: scenarioDeviceHealth,
                    CiPolicy: scenarioCiPolicy)
            ],
            Governance: CreateLabInfrastructureFailure(),
            DeviceHealth: new ScenarioDeviceHealthSnapshot(
                "luotsi-device-health.v1",
                "usb-1",
                "quarantined",
                DateTimeOffset.Parse("2026-05-28T09:00:01Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                30,
                2,
                2,
                2,
                0,
                1,
                0,
                2,
                false,
                true),
            CiPolicy: new ScenarioCiPolicyResult(
                "enforced",
                "device_quarantined",
                20,
                true,
                1,
                0,
                2,
                false,
                true,
                true,
                "Device is quarantined."));

        await writer.WriteAsync(report);

        var xml = XDocument.Parse(await fileSystem.ReadAllTextAsync(@"C:\tmp\junit.xml"));
        var properties = xml.Root!.Element("properties")!.Elements("property").ToArray();
        var scenarioProperties = xml.Root!.Element("testcase")!.Element("properties")!.Elements("property").ToArray();
        Assert.Contains(properties, property => property.Attribute("name")?.Value == "luotsi.device_health.state" && property.Attribute("value")?.Value == "quarantined");
        Assert.Contains(properties, property => property.Attribute("name")?.Value == "luotsi.policy.outcome" && property.Attribute("value")?.Value == "device_quarantined");
        Assert.Contains(properties, property => property.Attribute("name")?.Value == "luotsi.policy.recommended_exit_code" && property.Attribute("value")?.Value == "20");
        Assert.Contains(scenarioProperties, property => property.Attribute("name")?.Value == "luotsi.device_health.state" && property.Attribute("value")?.Value == "recovering");
        Assert.Contains(scenarioProperties, property => property.Attribute("name")?.Value == "luotsi.policy.outcome" && property.Attribute("value")?.Value == "device_recovering");
    }

    [Fact]
    public void ScenarioBatchItemResult_FromSuccess_Preserves_DeviceHealth_And_CiPolicy()
    {
        var deviceHealth = new ScenarioDeviceHealthSnapshot(
            "luotsi-device-health.v1",
            "usb-1",
            "suspect",
            DateTimeOffset.Parse("2026-05-28T09:00:01Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            30,
            2,
            1,
            0,
            0,
            1,
            0,
            2,
            false,
            false);
        var ciPolicy = new ScenarioCiPolicyResult(
            "advisory",
            "watch_device",
            10,
            false,
            1,
            1,
            2,
            true,
            true,
            false,
            "Watch the device.");

        var result = CreateRunResult("failed", CreateAllocation("usb-1", "offline", "unavailable"), CreateLabInfrastructureFailure()) with
        {
            DeviceHealth = deviceHealth,
            CiPolicy = ciPolicy
        };

        var item = ScenarioBatchItemResult.FromSuccess(result);

        Assert.Same(deviceHealth, item.DeviceHealth);
        Assert.Same(ciPolicy, item.CiPolicy);
    }

    [Fact]
    public async Task ApplyAsync_Writes_DeviceHealth_And_CiPolicy_Artifacts()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-28T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LOCALAPPDATA"] = @"C:\Users\Test\AppData\Local"
        });
        var quarantineStore = new LabQuarantineStore(fileSystem, timeProvider, environment);
        var registry = new ScenarioDeviceHealthRegistry(fileSystem, timeProvider, environment);
        var coordinator = new ScenarioGovernancePolicyCoordinator(registry, quarantineStore);
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider, environment, preferWorkspaceHome: true);

        var result = await coordinator.ApplyAsync(
            CreateRunResult("failed", CreateAllocation("usb-1", "offline", "unavailable"), CreateLabInfrastructureFailure()),
            CreateConfiguration(ScenarioCiPolicyMode.Advisory, retryBudget: 1, passThreshold: 2),
            artifacts);

        var deviceHealthJson = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(artifacts.Root, "device-health.json")));
        var ciPolicyJson = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(artifacts.Root, "ci-policy.json")));
        Assert.Equal(result.DeviceHealth!.State, deviceHealthJson.RootElement.GetProperty("state").GetString());
        Assert.Equal(result.CiPolicy!.Outcome, ciPolicyJson.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task ApplyAsync_WhenPolicyStateWriteFails_Preserves_Result_With_Metadata_Warning()
    {
        var fileSystem = new ThrowingPolicyFileSystem(
            new FakeFileSystem(),
            static path => path.Contains(@"Luotsi\lab\device-health", StringComparison.Ordinal));
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-28T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LOCALAPPDATA"] = @"C:\Users\Test\AppData\Local"
        });
        var quarantineStore = new LabQuarantineStore(fileSystem, timeProvider, environment);
        var registry = new ScenarioDeviceHealthRegistry(fileSystem, timeProvider, environment);
        var coordinator = new ScenarioGovernancePolicyCoordinator(registry, quarantineStore);

        var result = await coordinator.ApplyAsync(
            CreateRunResult("failed", CreateAllocation("usb-1", "offline", "unavailable"), CreateLabInfrastructureFailure()),
            CreateConfiguration(ScenarioCiPolicyMode.Advisory, retryBudget: 1, passThreshold: 2));

        Assert.Null(result.DeviceHealth);
        Assert.Null(result.CiPolicy);
        var warning = Assert.Single(result.MetadataWarnings!);
        Assert.Equal("device_health_policy_io", warning.Code);
        Assert.Contains("device_health and ci_policy were omitted", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_WhenPolicyStateWriteFails_Preserves_Original_Failure_Exception()
    {
        var fileSystem = new ThrowingPolicyFileSystem(
            new FakeFileSystem(),
            static path => path.Contains(@"Luotsi\lab\device-health", StringComparison.Ordinal));
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-28T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LOCALAPPDATA"] = @"C:\Users\Test\AppData\Local"
        });
        var quarantineStore = new LabQuarantineStore(fileSystem, timeProvider, environment);
        var registry = new ScenarioDeviceHealthRegistry(fileSystem, timeProvider, environment);
        var coordinator = new ScenarioGovernancePolicyCoordinator(registry, quarantineStore);
        var failureData = CreateFailureData(
            CreateAllocation("usb-1", "offline", "unavailable"),
            CreateLabInfrastructureFailure(),
            new ScenarioCiPolicyResult(
                "enforced",
                "device_quarantined",
                20,
                true,
                1,
                0,
                2,
                false,
                true,
                true,
                "Device is quarantined.")) with
        {
            DeviceHealth = null,
            CiPolicy = null
        };
        var exception = ScenarioFailureDetails.AttachDeviceAllocation(
            new ScenarioStepFailureException(
            "device offline",
            "configuration_error",
            failureData,
            new InvalidOperationException("device offline")),
            CreateAllocation("usb-1", "offline", "unavailable"));

        var returned = await coordinator.ApplyAsync(
            exception,
            CreateConfiguration(ScenarioCiPolicyMode.Advisory, retryBudget: 1, passThreshold: 2));

        Assert.Same(exception, returned);
        var updated = Assert.IsType<ScenarioRunFailureData>(ScenarioFailureDetails.TryGetData(exception));
        Assert.Null(updated.DeviceHealth);
        Assert.Null(updated.CiPolicy);
        var warning = Assert.Single(updated.MetadataWarnings!);
        Assert.Equal("device_health_policy_io", warning.Code);
        Assert.Contains("device_health and ci_policy were omitted", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunFileAsync_ReplayMetadata_Uses_Enforced_CiPolicy_ExitCode_For_Result()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-28T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LOCALAPPDATA"] = @"C:\Users\Test\AppData\Local"
        });
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider, environment, preferWorkspaceHome: true);
        var factory = new ScenarioRunEventCoordinatorFactory(fileSystem, timeProvider, CreateProvenance(), new FakeConsole());
        await using var coordinator = factory.Create(null, artifacts, "/tmp/scenarios/login.json", ScenarioProgressMode.Quiet);

        await coordinator.RunFileAsync("/tmp/scenarios/login.json", _ => Task.FromResult(
            CreateRunResult("failed", CreateAllocation("usb-1", "offline", "unavailable"), CreateLabInfrastructureFailure()) with
            {
                CiPolicy = new ScenarioCiPolicyResult(
                    "enforced",
                    "device_quarantined",
                    20,
                    true,
                    1,
                    0,
                    2,
                    false,
                    true,
                    true,
                    "Device is quarantined.")
            }));

        using var replayMetadata = await ReadReplayMetadataAsync(fileSystem, artifacts.Root);
        Assert.Equal(20, replayMetadata.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task RunFileAsync_ReplayMetadata_Uses_Enforced_CiPolicy_ExitCode_For_Failure_Exception()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-28T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LOCALAPPDATA"] = @"C:\Users\Test\AppData\Local"
        });
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider, environment, preferWorkspaceHome: true);
        var factory = new ScenarioRunEventCoordinatorFactory(fileSystem, timeProvider, CreateProvenance(), new FakeConsole());
        await using var coordinator = factory.Create(null, artifacts, "/tmp/scenarios/login.json", ScenarioProgressMode.Quiet);

        var exception = new ScenarioStepFailureException(
            "device offline",
            "configuration_error",
            CreateFailureData(
                CreateAllocation("usb-1", "offline", "unavailable"),
                CreateLabInfrastructureFailure(),
                new ScenarioCiPolicyResult(
                    "enforced",
                    "device_quarantined",
                    20,
                    true,
                    1,
                    0,
                    2,
                    false,
                    true,
                    true,
                    "Device is quarantined.")),
            new InvalidOperationException("device offline"));

        await Assert.ThrowsAsync<ScenarioStepFailureException>(() => coordinator.RunFileAsync(
            "/tmp/scenarios/login.json",
            _ => Task.FromException<ScenarioRunResult>(exception)));

        using var replayMetadata = await ReadReplayMetadataAsync(fileSystem, artifacts.Root);
        Assert.Equal(20, replayMetadata.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task ScenarioLifecycleCoordinator_Emits_DeviceHealth_And_CiPolicy_On_ScenarioEnded_Success()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-28T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var sink = new CollectingScenarioEventSink();
        var coordinator = new ScenarioLifecycleCoordinator(timeProvider, sink);
        var allocation = CreateAllocation("usb-1", "device", "available");
        var deviceHealth = new ScenarioDeviceHealthSnapshot(
            "luotsi-device-health.v1",
            "usb-1",
            "recovering",
            DateTimeOffset.Parse("2026-05-28T09:00:01Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            30,
            2,
            1,
            0,
            1,
            1,
            1,
            2,
            false,
            false);
        var ciPolicy = new ScenarioCiPolicyResult(
            "advisory",
            "device_recovering",
            10,
            false,
            1,
            1,
            2,
            false,
            true,
            false,
            "Device is recovering.");

        await coordinator.RunAsync(
            new ScenarioLifecycleContext("/tmp/scenarios/login.json", "scenarios/login.json::login smoke", "login smoke", timeProvider.GetUtcNow()),
            "running",
            _ => Task.FromResult(new ScenarioLifecycleCompletion(
                CreateRunResult("passed", allocation, ScenarioGovernanceClassifier.FromStatus("passed", allocation)) with
                {
                    DeviceHealth = deviceHealth,
                    CiPolicy = ciPolicy
                },
                timeProvider.GetUtcNow().AddSeconds(1))));

        var ended = Assert.Single(sink.Events, static evt => evt.Event == "scenario_ended");
        Assert.Same(deviceHealth, ended.DeviceHealth);
        Assert.Same(ciPolicy, ended.CiPolicy);
    }

    [Fact]
    public async Task ScenarioLifecycleCoordinator_Emits_DeviceHealth_And_CiPolicy_On_ScenarioEnded_Failure()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-28T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var sink = new CollectingScenarioEventSink();
        var coordinator = new ScenarioLifecycleCoordinator(timeProvider, sink);
        var ciPolicy = new ScenarioCiPolicyResult(
            "enforced",
            "device_quarantined",
            20,
            true,
            1,
            0,
            2,
            false,
            true,
            true,
            "Device is quarantined.");
        var exception = new ScenarioStepFailureException(
            "device offline",
            "configuration_error",
            CreateFailureData(CreateAllocation("usb-1", "offline", "unavailable"), CreateLabInfrastructureFailure(), ciPolicy),
            new InvalidOperationException("device offline"));

        await Assert.ThrowsAsync<ScenarioStepFailureException>(() => coordinator.RunAsync(
            new ScenarioLifecycleContext("/tmp/scenarios/login.json", "scenarios/login.json::login smoke", "login smoke", timeProvider.GetUtcNow()),
            "running",
            _ => Task.FromException<ScenarioLifecycleCompletion>(exception)));

        var ended = Assert.Single(sink.Events, static evt => evt.Event == "scenario_ended");
        Assert.NotNull(ended.DeviceHealth);
        Assert.Equal("quarantined", ended.DeviceHealth!.State);
        Assert.Same(ciPolicy, ended.CiPolicy);
    }

    [Fact]
    public void FromSingleFailure_Propagates_DeviceHealth_And_CiPolicy_To_Scenario_Report()
    {
        var ciPolicy = new ScenarioCiPolicyResult(
            "enforced",
            "device_quarantined",
            20,
            true,
            1,
            0,
            2,
            false,
            true,
            true,
            "Device is quarantined.");
        var exception = new ScenarioStepFailureException(
            "device offline",
            "configuration_error",
            CreateFailureData(CreateAllocation("usb-1", "offline", "unavailable"), CreateLabInfrastructureFailure(), ciPolicy),
            new InvalidOperationException("device offline"));

        var report = ScenarioRunReportFactory.FromSingleFailure(
            "/tmp/scenarios/login.json",
            exception,
            DateTimeOffset.Parse("2026-05-28T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse("2026-05-28T09:00:01Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            ScenarioArtifactAttachmentPolicy.OnFailure,
            CreateProvenance());

        var scenario = Assert.Single(report.Scenarios);
        Assert.NotNull(report.DeviceHealth);
        Assert.Same(report.DeviceHealth, scenario.DeviceHealth);
        Assert.Same(ciPolicy, report.CiPolicy);
        Assert.Same(ciPolicy, scenario.CiPolicy);
    }

    private static ScenarioRunConfiguration CreateConfiguration(
        ScenarioCiPolicyMode mode,
        int retryBudget,
        int passThreshold) =>
        new(
            null,
            null,
            null,
            ScenarioFailureArtifactCapturePolicy.Failure,
            ScenarioArtifactAttachmentPolicy.OnFailure,
            false,
            true,
            15,
            null,
            ScenarioProgressMode.Quiet,
            null,
            mode,
            30,
            retryBudget,
            passThreshold);

    private static ScenarioRunResult CreateRunResult(
        string status,
        ScenarioDeviceAllocation allocation,
        ScenarioGovernanceVerdict governance) =>
        new(
            "login smoke",
            status,
            new ScenarioRunTiming(1000, 100, 850, 50),
            ScenarioMetrics.Empty,
            [],
            allocation,
            "scenarios/login.json::login smoke",
            "/tmp/scenarios/login.json",
            null,
            null,
            "line",
            [],
            governance);

    private static ScenarioRunFailureData CreateFailureData(
        ScenarioDeviceAllocation allocation,
        ScenarioGovernanceVerdict governance,
        ScenarioCiPolicyResult ciPolicy) =>
        new ScenarioRunFailureData(
            "login smoke",
            "/tmp/scenarios/login.json",
            "failed",
            new ScenarioRunTiming(1000, 100, 850, 50),
            ScenarioMetrics.Empty,
            new ScenarioFailedStepResult(0, "open app", "launchApp", 100, new ScenarioStepTiming(100, 10, 80, 10)),
            [],
            new FailureArtifactBundle(
                "luotsi-failure-bundle.v1",
                DateTimeOffset.Parse("2026-05-28T09:00:01Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                "scenario",
                "login smoke",
                "/tmp/scenarios/login.json",
                0,
                "open app",
                "launchApp",
                "System.InvalidOperationException",
                "device offline",
                [],
                []),
            ScenarioId: "scenarios/login.json::login smoke",
            Governance: governance,
            CiPolicy: ciPolicy) with
        {
            DeviceHealth = new ScenarioDeviceHealthSnapshot(
                "luotsi-device-health.v1",
                allocation.Serial!,
                "quarantined",
                DateTimeOffset.Parse("2026-05-28T09:00:01Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                30,
                2,
                2,
                2,
                0,
                1,
                0,
                2,
                false,
                true)
        };

    private static async Task<JsonDocument> ReadReplayMetadataAsync(FakeFileSystem fileSystem, string artifactRoot) =>
        JsonDocument.Parse(await fileSystem.ReadAllTextAsync(Path.Join(artifactRoot, SessionReplayArtifacts.MetadataFileName)));

    private static ScenarioDeviceAllocation CreateAllocation(string serial, string deviceState, string availability) =>
        new(
            "allocated",
            serial,
            new DeviceState(
                serial,
                deviceState,
                "usb",
                "physical",
                "Pixel 9",
                "pixel",
                "tokay",
                "model:Pixel 9",
                availability,
                "Reconnect adb."),
            new PreflightResult("Pixel 9", "15", "35", "com.example/.MainActivity", "com.example", "com.example versionName=1.0", "fingerprint", "arm64-v8a", serial),
            true,
            15);

    private static ScenarioGovernanceVerdict CreateLabInfrastructureFailure(bool quarantineCandidate = true) =>
        new(
            "lab_infrastructure_failure",
            "high",
            "The run failed before a trustworthy product verdict because the selected device or ADB transport was not healthy for device 'usb-1'.",
            false,
            true,
            quarantineCandidate,
            "Repair or quarantine the unhealthy device before rerunning.");

    private static BuildProvenance CreateProvenance() =>
        new("luotsi", "1.0.0", "abc123", "main", "digablesolutions/luotsi", "github-actions", "123", "windows", "x64", "net10.0");

    private sealed class CollectingScenarioEventSink : IScenarioEventSink
    {
        public List<ScenarioEvent> Events { get; } = [];

        public Task EmitAsync(ScenarioEvent scenarioEvent)
        {
            Events.Add(scenarioEvent);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingPolicyFileSystem(FakeFileSystem inner, Func<string, bool> shouldThrowOnOpenWrite) : IFileSystem
    {
        private readonly FakeFileSystem _inner = inner;
        private readonly Func<string, bool> _shouldThrowOnOpenWrite = shouldThrowOnOpenWrite;

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption) =>
            _inner.GetFiles(path, searchPattern, searchOption);

        public Task WriteAllTextAsync(string path, string text, System.Text.Encoding encoding, CancellationToken cancellationToken = default) =>
            _inner.WriteAllTextAsync(path, text, encoding, cancellationToken);

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
            _inner.ReadAllTextAsync(path, cancellationToken);

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default) =>
            _inner.ReadAllBytesAsync(path, cancellationToken);

        public Stream OpenRead(string path) => _inner.OpenRead(path);

        public Stream OpenWrite(string path, bool overwrite = true) =>
            _shouldThrowOnOpenWrite(path)
                ? throw new IOException($"Injected write failure for '{path}'.")
                : _inner.OpenWrite(path, overwrite);

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
            _inner.CopyFile(sourcePath, destinationPath, overwrite);

        public string GetTempPath() => _inner.GetTempPath();
    }
}
