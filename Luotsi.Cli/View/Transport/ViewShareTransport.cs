using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Transport;

internal enum ViewShareObserverEventKind
{
    Connected,
    Disconnected
}

internal static class ViewShareObserverDisconnectReasons
{
    public const string SourceReset = "source_reset";
    public const string ObserverBackpressure = "observer_backpressure";
    public const string ServerDisposed = "server_disposed";
    public const string ObserverDisconnected = "observer_disconnected";
}

internal sealed record ViewShareObserverEvent(ViewShareObserverEventKind Kind, string? RemoteEndpoint, int ObserverCount, string? Reason = null);

internal sealed class TcpViewShareServer(string bindEndpoint) : IAsyncDisposable
{
    private const int ListenerBacklog = 128;
    private const int SocketBufferSize = 256 * 1024;

    private readonly string _bindEndpoint = string.IsNullOrWhiteSpace(bindEndpoint)
        ? throw new ArgumentException("Share bind endpoint is required.", nameof(bindEndpoint))
        : bindEndpoint;
    private readonly Lock _gate = new();
    private readonly ViewPacketStreamWriter _writer = new();
    private readonly List<ObserverConnection> _connections = [];

    private TcpListener? _listener;
    private CancellationTokenSource? _acceptCancellation;
    private Task? _acceptLoop;
    private ViewStreamHeader? _currentHeader;
    private IReadOnlyList<ViewPacket> _bootstrapPackets = [];

    public string? BoundEndpoint { get; private set; }

    public event Action<ViewShareObserverEvent>? ObserverChanged;

    public int ObserverCount
    {
        get
        {
            lock (_gate)
            {
                return _connections.Count;
            }
        }
    }

    public async Task<string> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
        {
            return BoundEndpoint ?? _bindEndpoint;
        }

        var endpoint = ViewShareEndpointParser.ParseBindable(_bindEndpoint);
        var listener = new TcpListener(endpoint.Address, endpoint.Port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start(ListenerBacklog);

        _listener = listener;
        _acceptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var bound = (IPEndPoint)listener.LocalEndpoint;
        BoundEndpoint = $"{bound.Address}:{bound.Port}";
        _acceptLoop = AcceptLoopAsync(listener, _acceptCancellation.Token);
        return BoundEndpoint;
    }

    public async Task BeginStreamAsync(ViewStreamHeader header, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(header);

        _currentHeader = header;
        _bootstrapPackets = [];
        await DisconnectObserversAsync(ViewShareObserverDisconnectReasons.SourceReset, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishPacketAsync(ViewPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        List<ObserverConnection>? disconnectedConnections = null;
        lock (_gate)
        {
            _bootstrapPackets = UpdateBootstrapPackets(_bootstrapPackets, packet);
            for (var index = 0; index < _connections.Count; index++)
            {
                var connection = _connections[index];
                if (connection.TryQueue(packet)) continue;
                disconnectedConnections ??= [];
                disconnectedConnections.Add(connection);
            }
        }

        if (disconnectedConnections is null)
        {
            return;
        }

        foreach (var connection in disconnectedConnections)
        {
            await RemoveConnectionAsync(connection, ViewShareObserverDisconnectReasons.ObserverBackpressure, disposeConnection: true).ConfigureAwait(false);
        }
    }

    private async Task DisconnectObserversAsync(string reason, CancellationToken cancellationToken = default)
    {
        List<ObserverConnection> snapshot;
        lock (_gate)
        {
            snapshot = [.. _connections];
        }

        foreach (var connection in snapshot)
        {
            await RemoveConnectionAsync(connection, reason, disposeConnection: true).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_acceptCancellation is not null)
        {
            await _acceptCancellation.CancelAsync();
        }

        if (_listener is not null)
        {
            _listener.Stop();
            _listener = null;
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        await DisconnectObserversAsync(ViewShareObserverDisconnectReasons.ServerDisposed).ConfigureAwait(false);
        _acceptCancellation?.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                ConfigureObserverSocket(client);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            var header = _currentHeader;
            if (header is null)
            {
                client.Dispose();
                continue;
            }

            var connection = new ObserverConnection(client, header, _bootstrapPackets, NotifyConnectionClosedAsync);
            lock (_gate)
            {
                _connections.Add(connection);
            }

            RaiseObserverChanged(ViewShareObserverEventKind.Connected, connection.RemoteEndpoint, ObserverCount);
            connection.Start();
        }
    }

    private Task NotifyConnectionClosedAsync(ObserverConnection connection) =>
        RemoveConnectionAsync(connection, ViewShareObserverDisconnectReasons.ObserverDisconnected, disposeConnection: false);

    private async Task RemoveConnectionAsync(ObserverConnection connection, string reason, bool disposeConnection)
    {
        bool removed;
        lock (_gate)
        {
            removed = _connections.Remove(connection);
        }

        if (!removed)
        {
            return;
        }

        if (disposeConnection)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        RaiseObserverChanged(ViewShareObserverEventKind.Disconnected, connection.RemoteEndpoint, ObserverCount, reason);
    }

    private void RaiseObserverChanged(ViewShareObserverEventKind kind, string? remoteEndpoint, int observerCount, string? reason = null) =>
        ObserverChanged?.Invoke(new ViewShareObserverEvent(kind, remoteEndpoint, observerCount, reason));

    private static IReadOnlyList<ViewPacket> UpdateBootstrapPackets(IReadOnlyList<ViewPacket> currentBootstrapPackets, ViewPacket packet)
    {
        if (packet.PacketType != ViewPacketType.Config &&
            packet is not { PacketType: ViewPacketType.Frame, IsKeyFrame: true })
        {
            return currentBootstrapPackets;
        }

        var configs = currentBootstrapPackets.Where(existing => existing.PacketType == ViewPacketType.Config).ToList();
        configs.Add(packet);
        return configs;
    }

    private static void ConfigureObserverSocket(TcpClient client)
    {
        client.NoDelay = true;
        client.ReceiveBufferSize = SocketBufferSize;
        client.SendBufferSize = SocketBufferSize;
        try
        {
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }
        catch (SocketException)
        {
            // Keep-alive can be unsupported on some platforms/transports.
        }
    }

    private sealed class ObserverConnection(
        TcpClient client,
        ViewStreamHeader header,
        IReadOnlyList<ViewPacket> bootstrapPackets,
        Func<ObserverConnection, Task> onClosedAsync) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();

        private readonly Channel<ViewPacket> _packets = Channel.CreateBounded<ViewPacket>(new BoundedChannelOptions(128)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        private Task? _writerTask;

        public string? RemoteEndpoint { get; } = client.Client.RemoteEndPoint?.ToString();

        public void Start() => _writerTask = WriteLoopAsync();

        public bool TryQueue(ViewPacket packet) => _packets.Writer.TryWrite(packet);

        public async ValueTask DisposeAsync()
        {
            _packets.Writer.TryComplete();
            await _cancellation.CancelAsync();
            client.Dispose();

            if (_writerTask is not null)
            {
                try
                {
                    await _writerTask.ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }
            }

            _cancellation.Dispose();
        }

        private async Task WriteLoopAsync()
        {
            try
            {
                await using var stream = client.GetStream();
                await ViewPacketStreamWriter.WriteHeaderAsync(stream, header, _cancellation.Token).ConfigureAwait(false);
                foreach (var packet in bootstrapPackets)
                {
                    await ViewPacketStreamWriter.WritePacketAsync(stream, packet, _cancellation.Token).ConfigureAwait(false);
                }

                await foreach (var packet in _packets.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
                {
                    await ViewPacketStreamWriter.WritePacketAsync(stream, packet, _cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
            catch
            {
                // ignored
            }
            finally
            {
                client.Dispose();
                await onClosedAsync(this).ConfigureAwait(false);
            }
        }
    }
}

internal sealed class ViewPacketStreamWriter
{
    public static async Task WriteHeaderAsync(Stream stream, ViewStreamHeader header, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(header);

        var buffer = new byte[ViewTransportConstants.StreamHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), ViewTransportConstants.Magic);
        buffer[4] = checked((byte)header.ProtocolVersion);
        buffer[5] = EncodeCodec(header.Codec);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6, 2), header.Flags);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8, 4), header.Width);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(12, 4), header.Height);
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task WritePacketAsync(Stream stream, ViewPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(packet);

        var header = new byte[ViewTransportConstants.PacketHeaderSize];
        header[0] = EncodePacketType(packet.PacketType);
        header[1] = packet.IsKeyFrame ? ViewTransportConstants.KeyFrameFlag : (byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(4, 8), packet.Sequence);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(12, 8), packet.PresentationTimestampUs);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20, 4), packet.Payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!packet.Payload.IsEmpty)
        {
            await stream.WriteAsync(packet.Payload, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static byte EncodeCodec(string codec) => codec.ToLowerInvariant() switch
    {
        "h264" => ViewTransportConstants.H264CodecId,
        "h265" => ViewTransportConstants.H265CodecId,
        _ => throw new InvalidOperationException($"Unsupported shared-stream codec '{codec}'.")
    };

    private static byte EncodePacketType(ViewPacketType packetType) => packetType switch
    {
        ViewPacketType.Config => ViewTransportConstants.ConfigPacketTypeId,
        ViewPacketType.Frame => ViewTransportConstants.FramePacketTypeId,
        ViewPacketType.RotationReset => ViewTransportConstants.RotationResetPacketTypeId,
        ViewPacketType.StreamEnd => ViewTransportConstants.StreamEndPacketTypeId,
        ViewPacketType.ServerError => ViewTransportConstants.ServerErrorPacketTypeId,
        _ => throw new InvalidOperationException($"Unsupported shared-stream packet type '{packetType}'.")
    };
}

internal static class ViewShareEndpointParser
{
    public static (string Host, int Port) ParseConnect(string endpoint)
    {
        var uri = Parse(endpoint, allowZeroPort: false);
        return (uri.Host, uri.Port);
    }

    public static (IPAddress Address, int Port) ParseBindable(string endpoint)
    {
        var uri = Parse(endpoint, allowZeroPort: true);
        var host = uri.Host;
        if (string.Equals(host, "*", StringComparison.Ordinal) || string.Equals(host, "+", StringComparison.Ordinal))
        {
            return (IPAddress.Any, uri.Port);
        }

        if (IPAddress.TryParse(host, out var address))
        {
            return (address, uri.Port);
        }

        var addresses = Dns.GetHostAddresses(host);
        return addresses.Length == 0 ? throw new InvalidOperationException($"Share bind endpoint '{endpoint}' did not resolve to a host address.") : (addresses[0], uri.Port);
    }

    private static Uri Parse(string endpoint, bool allowZeroPort)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("Share endpoint is required.");
        }

        var normalized = endpoint.Contains("://", StringComparison.Ordinal) ? endpoint : $"tcp://{endpoint}";
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException($"Invalid share endpoint '{endpoint}'. Expected host:port.");
        }

        if (uri.Port < 0 || (!allowZeroPort && uri.Port == 0))
        {
            throw new InvalidOperationException($"Invalid share endpoint '{endpoint}'. Expected a positive TCP port.");
        }

        return uri;
    }
}
