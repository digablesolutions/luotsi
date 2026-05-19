using Luotsi.Cli.Cli;
using Luotsi.Cli.Cli.Composition;
using Luotsi.Cli.Cli.Inspect;
using Luotsi.Cli.Cli.Routing;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Models;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class AppCommandFamilyRouterTests
{
    [Fact]
    public async Task DispatchAsync_ProfileList_Bootstraps_Artifacts_Without_Creating_Runner()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-19T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var deviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost());
        var router = CreateRouter(new AppDependencies
        {
            TimeProvider = timeProvider,
            Console = console,
            FileSystem = fileSystem,
            DeviceHostFactory = deviceHostFactory,
            ViewProfileStore = new FakeViewProfileStore()
        });
        var context = new AppExecutionContext(timeProvider.GetUtcNow(), CliOptions.Parse(["profile-list", "--artifacts", "/tmp/artifacts"]));

        var exitCode = await router.DispatchAsync(context);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.NotNull(context.Artifacts);
        Assert.Equal(context.Artifacts!.Root, envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString());
        Assert.Null(context.Runner);
        Assert.Equal(0, deviceHostFactory.CreateCallCount);
    }

    [Fact]
    public async Task DispatchAsync_HostedCommand_Uses_Bootstrapped_Adb_Executable_When_Creating_Runner()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-19T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("SER123", "device", "product:panther model:Pixel_9 device:panther"));
        var deviceHostFactory = new FakeDeviceHostFactory(host);
        var router = CreateRouter(new AppDependencies
        {
            TimeProvider = timeProvider,
            Console = console,
            FileSystem = new FakeFileSystem(),
            Environment = new FakeEnvironmentVariables(new Dictionary<string, string>
            {
                [CliDefaults.AdbExecutableEnvironmentVariable] = "custom-adb"
            }),
            DeviceHostFactory = deviceHostFactory,
            ViewProfileStore = new FakeViewProfileStore()
        });
        var context = new AppExecutionContext(timeProvider.GetUtcNow(), CliOptions.Parse(["devices"]));

        var exitCode = await router.DispatchAsync(context);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Same(host, context.Runner);
        Assert.Equal("devices", envelope.RootElement.GetProperty("command").GetString());
        Assert.Equal("custom-adb", Assert.Single(deviceHostFactory.Configurations).Executable);
    }

    [Fact]
    public async Task DispatchAsync_RunValidateOnly_Uses_UnsupportedRunner_Without_DeviceHost_Creation()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-19T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        fileSystem.AddFile("/tmp/scenario.json", """
        {
          "name": "validate only",
          "steps": [
            { "action": "sleep", "milliseconds": 1 }
          ]
        }
        """);
        var deviceHostFactory = new FakeDeviceHostFactory(new FakeDeviceHost());
        var router = CreateRouter(new AppDependencies
        {
            TimeProvider = timeProvider,
            Console = console,
            FileSystem = fileSystem,
            Delay = new FakeDelay(timeProvider),
            DeviceHostFactory = deviceHostFactory,
            ViewProfileStore = new FakeViewProfileStore()
        });
        var context = new AppExecutionContext(timeProvider.GetUtcNow(), CliOptions.Parse(["run", "--file", "/tmp/scenario.json", "--validate-only"]));

        var exitCode = await router.DispatchAsync(context);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.IsType<UnsupportedDeviceHost>(context.Runner);
        Assert.Equal(0, deviceHostFactory.CreateCallCount);
        Assert.Equal("validated", envelope.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    private static AppCommandFamilyRouter CreateRouter(AppDependencies dependencies)
    {
        var infrastructure = AppInfrastructureCompositionBuilder.Build(dependencies);
        var hostedCommands = AppHostedCommandCompositionBuilder.Build(new(
            infrastructure.TimeProvider,
            infrastructure.Console,
            infrastructure.FileSystem,
            infrastructure.Environment,
            infrastructure.Delay,
            infrastructure.ProfileCoordinator));
        var viewCommands = AppViewCommandCompositionBuilder.Build(new(
            dependencies,
            infrastructure.TimeProvider,
            infrastructure.Console,
            infrastructure.Environment,
            infrastructure.FileSystem,
            infrastructure.ProcessRunner,
            infrastructure.AdbClientFactory,
            infrastructure.IdGenerator,
            hostedCommands.EnvelopeWriter,
            infrastructure.ProfileCoordinator,
            infrastructure.DeviceHostLauncher));

        return new AppCommandFamilyRouter(new AppCommandFamilyRouterDependencies
        {
            RouteBootstrapper = new AppCommandRouteBootstrapper(new AppCommandRouteBootstrapperDependencies
            {
                TimeProvider = infrastructure.TimeProvider,
                FileSystem = infrastructure.FileSystem,
                Environment = infrastructure.Environment,
                ProfileCoordinator = infrastructure.ProfileCoordinator,
                DeviceHostLauncher = infrastructure.DeviceHostLauncher
            }),
            CommandHost = hostedCommands.CommandHost,
            ViewSessionCommandPreparer = viewCommands.ViewSessionCommandPreparer,
            InspectSessionLauncher = new InspectSessionLauncher(infrastructure.DeviceHostLauncher, infrastructure.Console, infrastructure.TimeProvider),
            ViewDiagnosticsLauncher = viewCommands.ViewDiagnosticsLauncher
        });
    }
}