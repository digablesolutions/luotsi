using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Session;

/// <summary>
/// Describes the runtime collaborators used by a view session.
/// </summary>
public sealed record ViewSessionRuntime
{
    public required IConsoleIo Console { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required IViewTransportBootstrap TransportBootstrap { get; init; }

    public required IViewBackendFactory ViewBackendFactory { get; init; }

    public required IViewStreamConnector StreamConnector { get; init; }

    public required IViewPacketStreamReader PacketStreamReader { get; init; }

    public IViewRendererFactory? ViewRendererFactory { get; init; }

    public IViewRecorderFactory? ViewRecorderFactory { get; init; }

    public IArtifactFolderOpener? ArtifactFolderOpener { get; init; }

    public TimeSpan? AutoReconnectAfter { get; init; }
}