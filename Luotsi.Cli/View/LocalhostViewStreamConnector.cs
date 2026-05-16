using System.Net;
using System.Net.Sockets;

namespace Luotsi.Cli.View;

/// <summary>
/// Connects to the mirrored stream over a TCP endpoint.
/// </summary>
public sealed class LocalhostViewStreamConnector : IViewStreamConnector
{
    private const int MaxAttempts = 20;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    /// <inheritdoc />
    public async Task<IViewStreamConnection> ConnectAsync(ViewConnectionInfo connectionInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);

        if (connectionInfo.LocalPort <= 0)
        {
            throw new InvalidOperationException($"View connection info did not provide a valid local port: {connectionInfo.LocalPort}");
        }

        SocketException? lastSocketException = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var client = new TcpClient();
            try
            {
                var host = string.IsNullOrWhiteSpace(connectionInfo.Host) ? IPAddress.Loopback.ToString() : connectionInfo.Host;
                await client.ConnectAsync(host, connectionInfo.LocalPort, cancellationToken).ConfigureAwait(false);
                return new TcpViewStreamConnection(client);
            }
            catch (SocketException ex) when (attempt < MaxAttempts)
            {
                lastSocketException = ex;
                client.Dispose();
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        throw new InvalidOperationException($"Failed to connect to view transport on localhost:{connectionInfo.LocalPort}", lastSocketException);
    }

    private sealed class TcpViewStreamConnection(TcpClient client) : IViewStreamConnection
    {
        private readonly TcpClient _client = client;

        public Stream Stream => _client.GetStream();

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}