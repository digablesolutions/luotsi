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
    public async Task RunAsync_View_JoinShare_Uses_Injected_ViewSessionFactory_And_Skips_Device_Host_Creation()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var deviceHostFactory = new FakeDeviceHostFactory(host);
        var session = new FakeViewSession(23);
        var factory = new FakeViewSessionFactory(session);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: deviceHostFactory,
            viewSessionFactory: factory);

        var exitCode = await app.RunAsync([
            "view",
            "--join-share", "127.0.0.1:45123",
            "--headless"]);

        Assert.Equal(23, exitCode);
        Assert.Equal(0, deviceHostFactory.CreateCallCount);
        Assert.NotNull(factory.LastDeviceHost);
        Assert.NotSame(host, factory.LastDeviceHost);
        var options = Assert.Single(session.Options);
        Assert.Equal("127.0.0.1:45123", options.JoinShareEndpoint);
        Assert.True(options.ReadOnly);
        Assert.Equal("127.0.0.1:45123", options.DeviceSelector);
    }



    [Fact]
    public async Task RunAsync_View_Uses_ShareBind_On_Source_Session()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(23);
        var factory = new FakeViewSessionFactory(session);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: factory);

        var exitCode = await app.RunAsync([
            "view",
            "--device", "192.168.0.134:5555",
            "--share-bind", "127.0.0.1:0"]);

        Assert.Equal(23, exitCode);
        var options = Assert.Single(session.Options);
        Assert.Equal("127.0.0.1:0", options.ShareBindEndpoint);
        Assert.Null(options.JoinShareEndpoint);
        Assert.False(options.ReadOnly);
    }



    [Fact]
    public async Task RunAsync_View_JoinShare_Consumes_Shared_Tcp_Stream_Without_Starting_Device_Bootstrap()
    {
        await using var shareServer = new TcpViewShareServer("127.0.0.1:0");
        var endpoint = await shareServer.StartAsync();
        await shareServer.BeginStreamAsync(new ViewStreamHeader(1, "h264", 1080, 1920, 0));

        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var backend = new FakeViewBackend();
        var bootstrap = new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward"));
        var session = new ViewSession(
            new UnsupportedDeviceHost(),
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            bootstrap,
            new FakeViewBackendFactory(backend),
            new LocalhostViewStreamConnector(),
            new ViewPacketStreamReader());

        var runTask = session.RunAsync(new ViewOptions(endpoint, "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false, 1000, 0, "balanced", true, null, endpoint));
        await ViewTestWaitHelpers.WaitForShareObserverAsync(shareServer, 1);
        await shareServer.PublishPacketAsync(new ViewPacket(ViewPacketType.Frame, 1, 33_000, true, new byte[] { 0x10, 0x20, 0x30 }));
        await shareServer.PublishPacketAsync(new ViewPacket(ViewPacketType.StreamEnd, 2, 66_000, false, Array.Empty<byte>()));

        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.Equal(0, bootstrap.StartCallCount);
        Assert.Equal(2, backend.Packets.Count);
        Assert.Contains(console.OutputLines, line => line.Contains("shared-tcp", StringComparison.Ordinal));
    }



}
