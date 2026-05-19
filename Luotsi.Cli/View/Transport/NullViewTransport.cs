using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Transport;

internal sealed class NullViewTransportBootstrap : IViewTransportBootstrap
{
    public Task<ViewConnectionInfo> StartAsync(ViewStartRequest request, Action<ViewStartupPhase>? reportPhase = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ViewConnectionInfo(
            Guid.NewGuid().ToString("N"),
            request.Codec,
            ViewPacketStreamReader.CurrentProtocolVersion,
            0,
            0,
            0,
            ViewTransportConstants.NullTransportVersion,
            ViewTransportConstants.NullTransport));

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class NullViewBackend : IViewBackend
{
    public string Name => ViewTransportConstants.NullTransport;

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
