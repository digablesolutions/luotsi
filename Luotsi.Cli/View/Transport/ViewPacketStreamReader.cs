using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Luotsi.Cli.View;

/// <summary>
/// Reads the private mirrored-stream transport protocol from a raw stream.
/// </summary>
public sealed class ViewPacketStreamReader : IViewPacketStreamReader
{
    public const uint Magic = ViewTransportConstants.Magic;
    public const int CurrentProtocolVersion = ViewTransportConstants.CurrentProtocolVersion;
    public const int StreamHeaderSize = ViewTransportConstants.StreamHeaderSize;
    public const int PacketHeaderSize = ViewTransportConstants.PacketHeaderSize;

    /// <inheritdoc />
    public async Task<ViewStreamHeader> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = new byte[StreamHeaderSize];
        await ReadExactAsync(stream, buffer, cancellationToken).ConfigureAwait(false);

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(0, 4));
        if (magic != Magic)
        {
            throw new InvalidOperationException($"Unsupported view stream magic '0x{magic:X8}'.");
        }

        var protocolVersion = buffer[4];
        if (protocolVersion != CurrentProtocolVersion)
        {
            throw new InvalidOperationException($"Unsupported view stream protocol version '{protocolVersion}'.");
        }

        var codec = DecodeCodec(buffer[5]);
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(6, 2));
        var width = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8, 4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(12, 4));

        return new ViewStreamHeader(protocolVersion, codec, width, height, flags);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ViewPacket> ReadPacketsAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var headerBuffer = new byte[PacketHeaderSize];
        while (true)
        {
            var bytesRead = await ReadAtLeastOnceAsync(stream, headerBuffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                yield break;
            }

            if (bytesRead < PacketHeaderSize)
            {
                throw new InvalidOperationException("Unexpected end of stream while reading a view packet header.");
            }

            var packetType = DecodePacketType(headerBuffer[0]);
            var flags = headerBuffer[1];
            var isKeyFrame = (flags & ViewTransportConstants.KeyFrameFlag) != 0;
            var sequence = BinaryPrimitives.ReadInt64LittleEndian(headerBuffer.AsSpan(4, 8));
            var pts = BinaryPrimitives.ReadInt64LittleEndian(headerBuffer.AsSpan(12, 8));
            var payloadSize = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.AsSpan(20, 4));

            if (payloadSize < 0)
            {
                throw new InvalidOperationException($"Invalid negative view packet payload size '{payloadSize}'.");
            }

            var payload = payloadSize == 0 ? Array.Empty<byte>() : new byte[payloadSize];
            if (payloadSize > 0)
            {
                try
                {
                    await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("view packet payload", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unexpected end of stream while reading payload for '{packetType}' packet sequence {sequence} with advertised size {payloadSize}.",
                        ex);
                }
            }

            yield return new ViewPacket(packetType, sequence, pts, isKeyFrame, payload);
        }
    }

    private static async Task<int> ReadAtLeastOnceAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return totalRead;
            }

            totalRead += read;
        }

        return totalRead;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = await ReadAtLeastOnceAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
        if (totalRead != buffer.Length)
        {
            throw new InvalidOperationException("Unexpected end of stream while reading a view packet payload.");
        }
    }

    private static string DecodeCodec(byte value) => value switch
    {
        ViewTransportConstants.H264CodecId => "h264",
        ViewTransportConstants.H265CodecId => "h265",
        _ => throw new InvalidOperationException($"Unsupported view stream codec identifier '{value}'.")
    };

    private static ViewPacketType DecodePacketType(byte value) => value switch
    {
        ViewTransportConstants.ConfigPacketTypeId => ViewPacketType.Config,
        ViewTransportConstants.FramePacketTypeId => ViewPacketType.Frame,
        ViewTransportConstants.RotationResetPacketTypeId => ViewPacketType.RotationReset,
        ViewTransportConstants.StreamEndPacketTypeId => ViewPacketType.StreamEnd,
        ViewTransportConstants.ServerErrorPacketTypeId => ViewPacketType.ServerError,
        _ => throw new InvalidOperationException($"Unsupported view packet type '{value}'.")
    };
}