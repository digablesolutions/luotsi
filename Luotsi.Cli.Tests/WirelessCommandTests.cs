using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Models;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task RunAsync_Wireless_Enables_Tcpip_And_Connects()
    {
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind), "Sign in"));
        var app = new App(new AppDependencies
        {
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["wireless", "--device", "usb-device", "--host", "192.168.0.44", "--port", "5556"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal([("192.168.0.44", 5556)], host.WirelessRequests);
        Assert.Equal("192.168.0.44:5556", envelope.RootElement.GetProperty("data").GetProperty("endpoint").GetString());
    }

    [Fact]
    public async Task RunAsync_WirelessScan_Returns_Structured_Mdns_Services()
    {
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind), "Sign in"));
        host.WirelessServices.Add(new WirelessMdnsService(
            "adb-14141FDF600081-QXjCrW",
            "_adb-tls-pairing._tcp",
            "192.168.86.38",
            33861,
            "192.168.86.38:33861",
            "adb-14141FDF600081-QXjCrW._adb-tls-pairing._tcp",
            "pairing"));
        var app = new App(new AppDependencies
        {
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["wireless-scan"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal("wireless-scan", envelope.RootElement.GetProperty("command").GetString());
        var data = envelope.RootElement.GetProperty("data");
        Assert.Equal("adb-14141FDF600081-QXjCrW", data.GetProperty("services")[0].GetProperty("service_name").GetString());
        Assert.Equal("192.168.86.38:33861", data.GetProperty("pairing_services")[0].GetProperty("endpoint").GetString());
    }

    [Fact]
    public async Task RunAsync_WirelessConnect_SaveProfile_Writes_Device_Selector_Profile()
    {
        var console = new FakeConsole();
        var profiles = new FakeViewProfileStore();
        var host = new FakeDeviceHost(CreateScreenState(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind), "Sign in"))
        {
            WirelessConnectResponse = new WirelessMdnsConnectResult(
                "192.168.86.38:33015",
                "adb-14141FDF600081-TnSdi9",
                "_adb-tls-connect._tcp",
                "adb-14141FDF600081-TnSdi9._adb-tls-connect._tcp",
                "adb-14141FDF600081-TnSdi9._adb-tls-connect._tcp",
                "adb-14141FDF600081-TnSdi9._adb-tls-connect._tcp",
                true,
                "connected to adb-14141FDF600081-TnSdi9._adb-tls-connect._tcp",
                "connected to adb-14141FDF600081-TnSdi9._adb-tls-connect._tcp")
        };
        var app = new App(new AppDependencies
        {
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewProfileStore = profiles
        });

        var exitCode = await app.RunAsync(["wireless-connect", "--service", "adb-14141FDF600081-TnSdi9", "--save-profile", "desk"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal([(null, "adb-14141FDF600081-TnSdi9")], host.WirelessConnectRequests);
        Assert.Equal("adb-14141FDF600081-TnSdi9._adb-tls-connect._tcp", profiles.Profiles["desk"].Device);
        Assert.Equal("adb-14141FDF600081-TnSdi9._adb-tls-connect._tcp", envelope.RootElement.GetProperty("data").GetProperty("device_selector").GetString());
    }

    [Fact]
    public async Task RunAsync_WirelessConnect_SaveProfile_Persists_Resolved_Adb_And_Default_PollArtifacts()
    {
        var console = new FakeConsole();
        var profiles = new FakeViewProfileStore();
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            [CliDefaults.AdbExecutableEnvironmentVariable] = "platform-tools/adb"
        });
        var host = new FakeDeviceHost(CreateScreenState(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind), "Sign in"))
        {
            WirelessConnectResponse = new WirelessMdnsConnectResult(
                "192.168.86.38:33015",
                "adb-14141FDF600081-TnSdi9",
                "_adb-tls-connect._tcp",
                "adb-14141FDF600081-TnSdi9._adb-tls-connect._tcp",
                "192.168.86.38:33015",
                "adb-14141FDF600081-TnSdi9._adb-tls-connect._tcp",
                true,
                "connected to 192.168.86.38:33015",
                "connected to 192.168.86.38:33015")
        };
        var app = new App(new AppDependencies
        {
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewProfileStore = profiles,
            Environment = environment
        });

        var exitCode = await app.RunAsync(["wireless-connect", "--service", "adb-14141FDF600081-TnSdi9", "--save-profile", "desk"]);

        Assert.Equal(0, exitCode);
        Assert.Equal("platform-tools/adb", profiles.Profiles["desk"].Adb);
        Assert.Equal(CliDefaults.DefaultPollArtifactsPolicy, profiles.Profiles["desk"].PollArtifacts);
    }

    [Fact]
    public async Task DeviceRunner_EnableWirelessAsync_AutoDetects_Host_When_Not_Provided()
    {
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, "8.8.8.8 via 192.168.0.1 dev wlan0 src 192.168.0.44 uid 2000\n", string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "restarting in TCP mode port: 5555\n", string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "connected to 192.168.0.44:5555\n", string.Empty));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["wireless"]), new FakeFileSystem(), new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind)));
        var runner = new DeviceRunner(adb, artifacts);

        var result = await runner.EnableWirelessAsync(null, 5555);

        Assert.Equal("192.168.0.44", result.Host);
        Assert.Equal("192.168.0.44:5555", result.Endpoint);
        Assert.Equal(["ip route get 8.8.8.8"], adb.ShellCommands);
        Assert.Equal(["tcpip", "5555"], adb.RunCommands[0]);
        Assert.Equal(["connect", "192.168.0.44:5555"], adb.RunCommands[1]);
    }

    [Fact]
    public void DeviceRunner_ParseWirelessMdnsServices_Parses_Adb_Service_Types()
    {
        var services = DeviceRunner.ParseWirelessMdnsServices("""
List of discovered mdns services
adb-14141FDF600081         _adb._tcp                  192.168.86.38:5555
adb-14141FDF600081-QXjCrW  _adb-tls-pairing._tcp.    192.168.86.38:33861
adb-14141FDF600081-TnSdi9  _adb-tls-connect._tcp     192.168.86.38:33015
studio-g@<xeYnap/          _adb-tls-pairing._tcp     192.168.86.39:55861
""");

        Assert.Equal(4, services.Count);
        Assert.Equal("legacy", services[0].Kind);
        Assert.Equal("pairing", services[1].Kind);
        Assert.Equal("_adb-tls-pairing._tcp", services[1].ServiceType);
        Assert.Equal("adb-14141FDF600081-QXjCrW._adb-tls-pairing._tcp", services[1].Selector);
        Assert.Equal("connect", services[2].Kind);
        Assert.Equal("studio-g@<xeYnap/", services[3].ServiceName);
    }

    [Fact]
    public async Task DeviceRunner_ScanWirelessServicesAsync_Groups_Known_Service_Types()
    {
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, """
List of discovered mdns services
adb-14141FDF600081         _adb._tcp                 192.168.86.38:5555
adb-14141FDF600081-QXjCrW  _adb-tls-pairing._tcp    192.168.86.38:33861
adb-14141FDF600081-TnSdi9  _adb-tls-connect._tcp    192.168.86.38:33015
""", string.Empty));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["wireless-scan"]), new FakeFileSystem(), new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind)));
        var runner = new DeviceRunner(adb, artifacts);

        var result = await runner.ScanWirelessServicesAsync();

        Assert.Equal(["mdns", "services"], adb.RunCommands[0]);
        Assert.Equal(3, result.Services.Count);
        Assert.Single(result.PairingServices);
        Assert.Single(result.ConnectServices);
        Assert.Single(result.LegacyServices);
    }

    [Fact]
    public async Task DeviceRunner_PairWirelessAsync_Passes_Pairing_Code_NonInteractively()
    {
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, "Successfully paired to 192.168.86.38:33861\n", string.Empty));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["wireless-pair"]), new FakeFileSystem(), new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind)));
        var runner = new DeviceRunner(adb, artifacts);

        var result = await runner.PairWirelessAsync("192.168.86.38:33861", null, "515109");

        Assert.True(result.Paired);
        Assert.False(result.InteractiveRequired);
        Assert.Equal("192.168.86.38:33861", result.Endpoint);
        Assert.Equal(["pair", "192.168.86.38:33861", "515109"], adb.RunCommands[0]);
    }

    [Fact]
    public async Task DeviceRunner_PairWirelessAsync_Without_Code_Returns_Interactive_Limitation()
    {
        var adb = new FakeAdbClient();
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["wireless-pair"]), new FakeFileSystem(), new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind)));
        var runner = new DeviceRunner(adb, artifacts);

        var result = await runner.PairWirelessAsync("192.168.86.38:33861", null, null);

        Assert.False(result.Paired);
        Assert.True(result.InteractiveRequired);
        Assert.Contains("--code", result.Message, StringComparison.Ordinal);
        Assert.Empty(adb.RunCommands);
    }

    [Fact]
    public async Task DeviceRunner_ConnectWirelessAsync_Resolves_Service_Name_From_Scan()
    {
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, """
List of discovered mdns services
adb-14141FDF600081-TnSdi9  _adb-tls-connect._tcp    192.168.86.38:33015
""", string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "connected to adb-14141FDF600081-TnSdi9._adb-tls-connect._tcp\n", string.Empty));
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["wireless-connect"]), new FakeFileSystem(), new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind)));
        var runner = new DeviceRunner(adb, artifacts);

        var result = await runner.ConnectWirelessAsync(null, "adb-14141FDF600081-TnSdi9");

        Assert.Equal(["mdns", "services"], adb.RunCommands[0]);
        Assert.Equal(["connect", "192.168.86.38:33015"], adb.RunCommands[1]);
        Assert.Equal("192.168.86.38:33015", result.Endpoint);
        Assert.Equal("192.168.86.38:33015", result.ConnectTarget);
        Assert.Equal("adb-14141FDF600081-TnSdi9._adb-tls-connect._tcp", result.DeviceSelector);
        Assert.Equal("connected to adb-14141FDF600081-TnSdi9._adb-tls-connect._tcp", result.Stdout);
    }
}
