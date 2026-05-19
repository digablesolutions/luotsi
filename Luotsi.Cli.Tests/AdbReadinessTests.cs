using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class AdbReadinessTests
{
    [Fact]
    public async Task RunAsync_Adb_Mdns_Check_Routes_To_DeviceHost()
    {
        var host = new FakeDeviceHost();
        var console = new FakeConsole();
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = new FakeFileSystem(),
            TimeProvider = DateTimeOffset.Parse("2026-05-15T12:00:00Z").ToTimeProvider(),
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["adb", "mdns", "check"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(["mdns check"], host.AdbDiagnostics);
        using var output = console.ParseSingleOutputAsJson();
        Assert.Equal("adb", output.RootElement.GetProperty("command").GetString());
        Assert.Equal(ResultSchemas.AdbDiagnostic, output.RootElement.GetProperty("data").GetProperty("schema").GetString());
    }

    [Fact]
    public async Task RunAsync_DeviceWait_Routes_To_DeviceHost_With_Timeout()
    {
        var host = new FakeDeviceHost();
        var console = new FakeConsole();
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = new FakeFileSystem(),
            TimeProvider = DateTimeOffset.Parse("2026-05-15T12:00:00Z").ToTimeProvider(),
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["device-wait", "--timeout-sec", "22"]);

        Assert.Equal(0, exitCode);
        Assert.Equal([22], host.WaitForDeviceRequests);
        using var output = console.ParseSingleOutputAsJson();
        Assert.Equal(ResultSchemas.AdbReadiness, output.RootElement.GetProperty("data").GetProperty("schema").GetString());
        Assert.True(output.RootElement.GetProperty("data").GetProperty("device_selected").GetBoolean());
        Assert.True(output.RootElement.GetProperty("data").GetProperty("ping_verified").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_Adb_Reconnect_Offline_Routes_To_DeviceHost()
    {
        var host = new FakeDeviceHost();
        var app = new App(new AppDependencies
        {
            Console = new FakeConsole(),
            FileSystem = new FakeFileSystem(),
            TimeProvider = DateTimeOffset.Parse("2026-05-15T12:00:00Z").ToTimeProvider(),
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["adb", "reconnect", "offline"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(["offline"], host.AdbReconnectTargets);
    }

    [Fact]
    public async Task RunAsync_Passes_AdbTimeout_Option_To_AdbClientFactory()
    {
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, "List of devices attached\n", string.Empty));
        var adbFactory = new FakeAdbClientFactory(adb);
        var app = new App(new AppDependencies
        {
            Console = new FakeConsole(),
            FileSystem = new FakeFileSystem(),
            TimeProvider = DateTimeOffset.Parse("2026-05-15T12:00:00Z").ToTimeProvider(),
            AdbClientFactory = adbFactory
        });

        var exitCode = await app.RunAsync(["devices", "--adb-timeout-sec", "5"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(adbFactory.CommandTimeouts));
    }

    [Fact]
    public async Task RunAsync_Passes_AdbTimeout_Environment_To_AdbClientFactory()
    {
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, "List of devices attached\n", string.Empty));
        var adbFactory = new FakeAdbClientFactory(adb);
        var app = new App(new AppDependencies
        {
            Console = new FakeConsole(),
            FileSystem = new FakeFileSystem(),
            TimeProvider = DateTimeOffset.Parse("2026-05-15T12:00:00Z").ToTimeProvider(),
            Environment = new FakeEnvironmentVariables(new Dictionary<string, string>
            {
                [CliDefaults.AdbCommandTimeoutEnvironmentVariable] = "7"
            }),
            AdbClientFactory = adbFactory
        });

        var exitCode = await app.RunAsync(["devices"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(adbFactory.CommandTimeouts));
    }

    [Fact]
    public async Task RunAsync_Devices_Reports_Derived_Device_Inventory()
    {
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, """
        List of devices attached
        USB123 device usb:1 product:oriole model:Pixel_6 device:oriole transport_id:1
        192.168.0.44:5555 device product:panther model:Pixel_7 device:panther
        emulator-5554 offline product:sdk_gphone64_x86_64 model:Android_SDK_built_for_x86_64 device:emu64
        """, string.Empty));
        var console = new FakeConsole();
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = new FakeFileSystem(),
            TimeProvider = DateTimeOffset.Parse("2026-05-15T12:00:00Z").ToTimeProvider(),
            AdbClientFactory = new FakeAdbClientFactory(adb)
        });

        var exitCode = await app.RunAsync(["devices"]);
        using var output = console.ParseSingleOutputAsJson();
        var devices = output.RootElement.GetProperty("data").GetProperty("devices");

        Assert.Equal(0, exitCode);
        Assert.Equal(["devices", "-l"], adb.RunCommands.Single());
        Assert.Equal("online", devices[0].GetProperty("state").GetString());
        Assert.Equal("usb", devices[0].GetProperty("transport").GetString());
        Assert.Equal("wifi", devices[1].GetProperty("transport").GetString());
        Assert.Equal("emulator", devices[2].GetProperty("type").GetString());
        Assert.Equal("unavailable", devices[2].GetProperty("availability").GetString());
    }

    [Fact]
    public async Task RunAsync_DeviceStatus_Writes_State_For_Selected_Device()
    {
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Pixel 9", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER123")
        };
        host.ConnectedDevices.Add(new DeviceInfo("SER123", "device", "product:panther model:Pixel_9 device:panther"));
        var console = new FakeConsole();
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = new FakeFileSystem(),
            TimeProvider = DateTimeOffset.Parse("2026-05-15T12:00:00Z").ToTimeProvider(),
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["device-status", "--device", "SER123"]);
        using var output = console.ParseSingleOutputAsJson();
        var device = output.RootElement.GetProperty("data").GetProperty("device");
        var readiness = output.RootElement.GetProperty("data").GetProperty("readiness");

        Assert.Equal(0, exitCode);
        Assert.Equal("SER123", device.GetProperty("serial").GetString());
        Assert.Equal("online", device.GetProperty("state").GetString());
        Assert.Equal("physical", device.GetProperty("type").GetString());
        Assert.Equal("Pixel_9", device.GetProperty("model").GetString());
        Assert.Equal("16", readiness.GetProperty("android_release").GetString());
        Assert.Equal("36", readiness.GetProperty("sdk").GetString());
        Assert.Equal("focus", readiness.GetProperty("current_focus").GetString());
    }

    [Fact]
    public async Task RunAsync_DeviceStatus_Reuses_Inventory_Metadata_When_Available()
    {
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Pixel 7", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "192.168.0.44:5555")
        };
        host.ConnectedDevices.Add(new DeviceInfo("192.168.0.44:5555", "device", "product:panther model:Pixel_7 device:panther"));
        var console = new FakeConsole();
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = new FakeFileSystem(),
            TimeProvider = DateTimeOffset.Parse("2026-05-15T12:00:00Z").ToTimeProvider(),
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["device-status", "--device", "192.168.0.44:5555"]);
        using var output = console.ParseSingleOutputAsJson();
        var device = output.RootElement.GetProperty("data").GetProperty("device");

        Assert.Equal(0, exitCode);
        Assert.Equal("wifi", device.GetProperty("transport").GetString());
        Assert.Equal("panther", device.GetProperty("product").GetString());
        Assert.Equal("panther", device.GetProperty("device").GetString());
    }

    [Fact]
    public async Task RunAsync_DeviceStatus_Writes_Exact_Command_Envelope()
    {
        var started = DateTimeOffset.Parse("2026-05-15T12:00:00Z");
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Pixel 9", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER123")
        };
        host.ConnectedDevices.Add(new DeviceInfo("SER123", "device", "product:panther model:Pixel_9 device:panther"));
        var console = new FakeConsole();
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = new FakeFileSystem(),
            TimeProvider = started.ToTimeProvider(),
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["device-status", "--device", "SER123"]);

        var expectedJson = JsonSerializer.Serialize(new
        {
            ok = true,
            command = "device-status",
            started_at = started,
            ended_at = started,
            data = new
            {
                device = new
                {
                    serial = "SER123",
                    state = "online",
                    transport = "unknown",
                    type = "physical",
                    model = "Pixel_9",
                    product = "panther",
                    device = "panther",
                    details = "product:panther model:Pixel_9 device:panther",
                    availability = "available"
                },
                readiness = new
                {
                    model = "Pixel 9",
                    android_release = "16",
                    sdk = "36",
                    current_focus = "focus",
                    fingerprint = "fingerprint",
                    abi = "arm64-v8a",
                    serial = "SER123"
                }
            },
            artifacts = new
            {
                artifact_root = Path.Combine(Path.Combine("/tmp", "luotsi"), "20260515-120000-device-status"),
                poll_artifacts = "final"
            },
            schema = ResultSchemas.CommandEnvelope,
            duration_ms = 0
        });

        Assert.Equal(0, exitCode);
        Assert.Equal(expectedJson, Assert.Single(console.OutputLines));
    }

    [Fact]
    public async Task RunAsync_DeviceStatus_Requires_Selected_Device_To_Exist_In_Inventory()
    {
        var host = new FakeDeviceHost
        {
            PreflightTemplate = new PreflightResult("Pixel 9", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER123")
        };
        var console = new FakeConsole();
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = new FakeFileSystem(),
            TimeProvider = DateTimeOffset.Parse("2026-05-15T12:00:00Z").ToTimeProvider(),
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["device-status", "--device", "SER123"]);
        using var output = console.ParseSingleOutputAsJson();

        Assert.Equal(1, exitCode);
        Assert.Contains("was not present in `adb devices -l` output", output.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_DeviceQuery_Selects_Exactly_One_Device_Before_Command_Host_Creation()
    {
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("USB123", "device", "product:oriole model:Pixel_6 device:oriole"));
        host.ConnectedDevices.Add(new DeviceInfo("192.168.0.44:5555", "device", "product:panther model:Pixel_7 device:panther"));
        var factory = new FakeDeviceHostFactory(host);
        var app = new App(new AppDependencies
        {
            Console = new FakeConsole(),
            FileSystem = new FakeFileSystem(),
            TimeProvider = DateTimeOffset.Parse("2026-05-15T12:00:00Z").ToTimeProvider(),
            DeviceHostFactory = factory
        });

        var exitCode = await app.RunAsync(["preflight", "--device-query", "transport=wifi,model=Pixel_7"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, factory.CreateCallCount);
        Assert.Null(factory.Configurations[0].DeviceSerial);
        Assert.Equal("192.168.0.44:5555", factory.Configurations[1].DeviceSerial);
        Assert.Equal([null], host.CommandPreflightRequests);
    }

    [Fact]
    public async Task RunAsync_DeviceQuery_Multiple_Matches_Returns_Usage_Error()
    {
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("USB123", "device", "product:oriole model:Pixel_6 device:oriole"));
        host.ConnectedDevices.Add(new DeviceInfo("USB456", "device", "product:panther model:Pixel_7 device:panther"));
        var console = new FakeConsole();
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = new FakeFileSystem(),
            TimeProvider = DateTimeOffset.Parse("2026-05-15T12:00:00Z").ToTimeProvider(),
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["preflight", "--device-query", "type=physical"]);
        using var output = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Contains("matched multiple devices", output.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Devices_With_DeviceQuery_Returns_Usage_Error()
    {
        var host = new FakeDeviceHost();
        host.ConnectedDevices.Add(new DeviceInfo("USB123", "device", "product:oriole model:Pixel_6 device:oriole"));
        var console = new FakeConsole();
        var app = new App(new AppDependencies
        {
            Console = console,
            FileSystem = new FakeFileSystem(),
            TimeProvider = DateTimeOffset.Parse("2026-05-15T12:00:00Z").ToTimeProvider(),
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["devices", "--device-query", "model=Pixel_6"]);
        using var output = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.Contains("not supported with `devices`", output.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForDeviceAsync_Pings_When_Device_Is_Selected()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z"));
        var adb = new FakeAdbClient("SER123");
        adb.EnqueueRunResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "ping\n", string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["wait-for-device"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        var result = await runner.WaitForDeviceAsync(10);

        Assert.True(result.Ready);
        Assert.True(result.PingVerified);
        Assert.Equal("SER123", result.Serial);
        Assert.Equal("ping", result.PingOutput);
        Assert.Equal(["wait-for-device"], adb.RunCommands[0]);
        Assert.Equal(["echo ping"], adb.ShellCommands);
    }

    [Fact]
    public async Task WaitForDeviceAsync_Pings_When_Default_Device_Is_Selected()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z"));
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "ping\n", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "SER456\n", string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["wait-for-device"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        var result = await runner.WaitForDeviceAsync(10);

        Assert.True(result.Ready);
        Assert.True(result.DeviceSelected);
        Assert.True(result.PingVerified);
        Assert.Equal("SER456", result.Serial);
        Assert.Equal("ping", result.PingOutput);
        Assert.Equal(["wait-for-device"], adb.RunCommands[0]);
        Assert.Equal(["echo ping", "getprop ro.serialno"], adb.ShellCommands);
    }

    [Fact]
    public async Task GetAdbVersionAsync_Returns_Structured_Command_Output()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z"));
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, "Android Debug Bridge version 1.0.41", string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["adb", "version"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        var result = await runner.GetAdbVersionAsync();

        Assert.Equal(ResultSchemas.AdbDiagnostic, result.Schema);
        Assert.Equal("version", result.Name);
        Assert.True(result.Command.Succeeded);
        Assert.Equal(["version"], result.Command.Args);
        Assert.Contains("Android Debug Bridge", result.Command.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdbClient_Retries_Safe_Command_After_Protocol_Fault()
    {
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(1, string.Empty, "protocol fault (no status)"));
        processRunner.EnqueueResult(new ProcessResult(0, string.Empty, string.Empty));
        processRunner.EnqueueResult(new ProcessResult(0, "Android Debug Bridge version 1.0.41", string.Empty));
        var adb = new AdbClient("adb", null, processRunner, TimeSpan.FromSeconds(5));

        var result = await adb.RunAsync(["version"]);

        Assert.Equal(2, result.AttemptCount);
        Assert.Equal("adb protocol fault", result.Retry?.Reason);
        Assert.Equal(["version"], processRunner.Calls[0].Args);
        Assert.Equal(["start-server"], processRunner.Calls[1].Args);
        Assert.Equal(["version"], processRunner.Calls[2].Args);
    }

    [Fact]
    public async Task AdbClient_Retries_ReadOnly_Composite_Shell_Command_After_Protocol_Fault()
    {
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(1, string.Empty, "protocol fault (no status)"));
        processRunner.EnqueueResult(new ProcessResult(0, string.Empty, string.Empty));
        processRunner.EnqueueResult(new ProcessResult(0, "SER123", string.Empty));
        var adb = new AdbClient("adb", null, processRunner, TimeSpan.FromSeconds(5));

        var result = await adb.RunAsync(["shell", "echo __LUOTSI_DEVICE_FINGERPRINT_SERIAL__; getprop ro.serialno"]);

        Assert.Equal(2, result.AttemptCount);
        Assert.Equal("adb protocol fault", result.Retry?.Reason);
        Assert.Equal(["shell", "echo __LUOTSI_DEVICE_FINGERPRINT_SERIAL__; getprop ro.serialno"], processRunner.Calls[0].Args);
        Assert.Equal(["start-server"], processRunner.Calls[1].Args);
        Assert.Equal(["shell", "echo __LUOTSI_DEVICE_FINGERPRINT_SERIAL__; getprop ro.serialno"], processRunner.Calls[2].Args);
    }

    [Fact]
    public async Task AdbClient_Retries_ReadOnly_Composite_Shell_Command_After_Device_Not_Found_By_Waiting_For_Device()
    {
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(1, string.Empty, "error: device not found"));
        processRunner.EnqueueResult(new ProcessResult(0, string.Empty, string.Empty));
        processRunner.EnqueueResult(new ProcessResult(0, string.Empty, string.Empty));
        processRunner.EnqueueResult(new ProcessResult(0, "SER123", string.Empty));
        var adb = new AdbClient("adb", null, processRunner, TimeSpan.FromSeconds(5));

        var result = await adb.RunAsync(["shell", "echo __LUOTSI_DEVICE_FINGERPRINT_SERIAL__; getprop ro.serialno"]);

        Assert.Equal(2, result.AttemptCount);
        Assert.Equal("adb device not found", result.Retry?.Reason);
        Assert.Equal(["shell", "echo __LUOTSI_DEVICE_FINGERPRINT_SERIAL__; getprop ro.serialno"], processRunner.Calls[0].Args);
        Assert.Equal(["start-server"], processRunner.Calls[1].Args);
        Assert.Equal(["wait-for-device"], processRunner.Calls[2].Args);
        Assert.Equal(["shell", "echo __LUOTSI_DEVICE_FINGERPRINT_SERIAL__; getprop ro.serialno"], processRunner.Calls[3].Args);
    }

    [Fact]
    public async Task AdbClient_Does_Not_Retry_Mutating_WmSize_Reset_Shell_Command()
    {
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(1, string.Empty, "protocol fault (no status)"));
        var adb = new AdbClient("adb", null, processRunner, TimeSpan.FromSeconds(5));

        var result = await adb.RunAsync(["shell", "wm size reset"]);

        Assert.Equal(1, result.AttemptCount);
        Assert.Null(result.Retry);
        Assert.Single(processRunner.Calls);
    }

    [Fact]
    public async Task AdbClient_Does_Not_Retry_Mutating_Install()
    {
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(1, string.Empty, "protocol fault (no status)"));
        var adb = new AdbClient("adb", null, processRunner, TimeSpan.FromSeconds(5));

        var result = await adb.RunAsync(["install", "-r", "app.apk"]);

        Assert.Equal(1, result.AttemptCount);
        Assert.Null(result.Retry);
        Assert.Single(processRunner.Calls);
    }

    [Fact]
    public async Task AdbClient_Throws_TimeoutException_When_Command_Timeout_Expires()
    {
        var adb = new AdbClient("adb", null, new BlockingProcessRunner(), TimeSpan.FromMilliseconds(1));

        var error = await Assert.ThrowsAsync<TimeoutException>(() => adb.RunAsync(["version"]));

        Assert.Contains("adb command timed out", error.Message, StringComparison.Ordinal);
        Assert.Contains("adb version", error.Message, StringComparison.Ordinal);
    }

    private sealed class BlockingProcessRunner : IProcessRunner
    {
        public async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> args, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new ProcessResult(0, string.Empty, string.Empty);
        }
    }
}

internal static class DateTimeOffsetTestExtensions
{
    public static TimeProvider ToTimeProvider(this DateTimeOffset value) => new ManualTimeProvider(value);
}
