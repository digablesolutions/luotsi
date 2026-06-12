using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Infrastructure.Devices;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Transport;
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
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = deviceHostFactory,
            ViewSessionFactory = factory
        });

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
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewSessionFactory = factory
        });

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
        var session = CreateViewSession(
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

    [Fact]
    public async Task RunAsync_View_SourceShare_Relays_Helper_Diagnostics_To_Observers_Before_Filtering_Backend_Packets()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var backend = new PausingViewBackend();
        var session = CreateViewSession(
            new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in")),
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(backend),
            new FakeViewStreamConnector(
                new ViewPacketStreamHarness()
                    .WriteHeader("h264", 1080, 1920)
                    .WritePacket(ViewPacketType.Diagnostic, 1, 1_000, false, """{"phase":"socket_client_connected","status":"succeeded","message":"helper ready"}"""u8.ToArray())
                    .WritePacket(ViewPacketType.StreamEnd, 2, 2_000, false, [])
                    .Build()),
            new ViewPacketStreamReader());

        var runTask = session.RunAsync(new ViewOptions("device-1", "adb", "h264", "ffmpeg", true, null, 1600, 60, "8M", false, false, ShareBindEndpoint: "127.0.0.1:0"));
        var shareStartedLine = await WaitForConsoleLineAsync(console, SessionEventTypes.View.ShareStarted);
        var endpoint = JsonDocument.Parse(shareStartedLine).RootElement.GetProperty("endpoint").GetString()!;
        var (host, port) = ViewShareEndpointParser.ParseConnect(endpoint);
        using var observer = new System.Net.Sockets.TcpClient();
        await observer.ConnectAsync(host, port);
        backend.Resume();

        var reader = new ViewPacketStreamReader();
        var stream = observer.GetStream();
        await reader.ReadHeaderAsync(stream);
        await using var packets = reader.ReadPacketsAsync(stream).GetAsyncEnumerator();

        Assert.True(await packets.MoveNextAsync());
        Assert.Equal(ViewPacketType.Diagnostic, packets.Current.PacketType);
        Assert.Contains("socket_client_connected", System.Text.Encoding.UTF8.GetString(packets.Current.Payload.Span), StringComparison.Ordinal);

        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.Single(backend.Packets);
        Assert.Equal(ViewPacketType.StreamEnd, backend.Packets[0].PacketType);
        Assert.Contains(console.OutputLines, line => line.Contains(SessionEventTypes.View.Diagnostic, StringComparison.Ordinal));
    }

    private static async Task<string> WaitForConsoleLineAsync(FakeConsole console, string eventType)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var line = console.OutputLines.FirstOrDefault(candidate => candidate.Contains(eventType, StringComparison.Ordinal));
            if (line is not null)
            {
                return line;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out waiting for console event '{eventType}'.");
    }

    [Fact]
    public async Task RunAsync_View_JoinShare_Blocks_Recording_Command()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var console = new FakeConsole();
        var renderer = new ClosingViewRenderer();
        var rendererFactory = new FakeViewRendererFactory(renderer);
        var session = CreateViewSession(
            new UnsupportedDeviceHost(),
            ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider),
            console,
            timeProvider,
            new FakeViewTransportBootstrap(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward")),
            new FakeViewBackendFactory(new BlockingViewBackend()),
            new FakeViewStreamConnector(new ViewPacketStreamHarness().WriteHeader("h264", 1080, 1920).Build()),
            new ViewPacketStreamReader(),
            rendererFactory);

        var runTask = session.RunAsync(new ViewOptions("127.0.0.1:45123", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false, 1000, 0, "balanced", true, null, "127.0.0.1:45123"));
        var interactionHandler = await ViewTestWaitHelpers.WaitForInteractionHandlerAsync(rendererFactory);
        await interactionHandler(new ViewWindowCommandRequest(ViewWindowCommand.ToggleRecording));
        renderer.Close();
        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.Contains(console.OutputLines, line => line.Contains("observer_session", StringComparison.Ordinal));
        Assert.Contains(console.OutputLines, line => line.Contains("view_input_blocked", StringComparison.Ordinal));
    }



}

internal sealed class PausingViewBackend : IViewBackend
{
    private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<ViewPacket> Packets { get; } = [];

    public string Name => "pausing";

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void Resume() => _resume.TrySetResult();

    public async Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default)
    {
        await _resume.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var packet in packets.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            Packets.Add(packet);
            if (packet.PacketType == ViewPacketType.StreamEnd)
            {
                return;
            }
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
