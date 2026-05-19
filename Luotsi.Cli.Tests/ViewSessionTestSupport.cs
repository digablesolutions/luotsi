using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Session;
using Luotsi.Cli.View.Transport;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    private static ViewSession CreateViewSession(
        IDeviceHost deviceHost,
        ArtifactSession artifacts,
        IConsoleIo console,
        TimeProvider timeProvider,
        IViewTransportBootstrap transportBootstrap,
        IViewBackendFactory viewBackendFactory,
        IViewStreamConnector streamConnector,
        IViewPacketStreamReader? packetStreamReader = null,
        IViewRendererFactory? viewRendererFactory = null,
        IViewRecorderFactory? viewRecorderFactory = null,
        IArtifactFolderOpener? artifactFolderOpener = null,
        TimeSpan? autoReconnectAfter = null) =>
        new(
            deviceHost,
            artifacts,
            new ViewSessionRuntime
            {
                Console = console,
                TimeProvider = timeProvider,
                TransportBootstrap = transportBootstrap,
                ViewBackendFactory = viewBackendFactory,
                StreamConnector = streamConnector,
                PacketStreamReader = packetStreamReader ?? new ViewPacketStreamReader(),
                ViewRendererFactory = viewRendererFactory,
                ViewRecorderFactory = viewRecorderFactory,
                ArtifactFolderOpener = artifactFolderOpener,
                AutoReconnectAfter = autoReconnectAfter
            });
}