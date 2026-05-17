using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Backends.Ffmpeg;

namespace Luotsi.Cli.View;

internal sealed class NullViewTransportBootstrap : IViewTransportBootstrap
{
    public Task<ViewConnectionInfo> StartAsync(ViewStartRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ViewConnectionInfo(Guid.NewGuid().ToString("N"), request.Codec, ViewPacketStreamReader.CurrentProtocolVersion, 0, 0, 0, "phase-1-stub", "stub"));

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class NullViewBackend : IViewBackend
{
    public string Name => "stub";

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
