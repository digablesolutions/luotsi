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
}
