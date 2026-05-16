using System.Text.Json;
using Luotsi.Cli;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Luotsi.Cli.Telemetry;
using Luotsi.Cli.View;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task RunAsync_Wireless_Enables_Tcpip_And_Connects()
    {
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind), "Sign in"));
        var app = new App(console: console, deviceHostFactory: new FakeDeviceHostFactory(host));

        var exitCode = await app.RunAsync(["wireless", "--device", "usb-device", "--host", "192.168.0.44", "--port", "5556"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.Equal([("192.168.0.44", 5556)], host.WirelessRequests);
        Assert.Equal("192.168.0.44:5556", envelope.RootElement.GetProperty("data").GetProperty("endpoint").GetString());
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
}
