namespace Luotsi.Cli.View;

/// <summary>
/// Shared literals for the private mirrored view transport.
/// </summary>
public static class ViewTransportConstants
{
    /// <summary>
    /// Transport stream magic value.
    /// </summary>
    public const uint Magic = 0x42414C56; // VLAB

    /// <summary>
    /// Current view transport protocol version.
    /// </summary>
    public const int CurrentProtocolVersion = 1;

    /// <summary>
    /// Binary size of the stream header in bytes.
    /// </summary>
    public const int StreamHeaderSize = 16;

    /// <summary>
    /// Binary size of the packet header in bytes.
    /// </summary>
    public const int PacketHeaderSize = 24;

    /// <summary>
    /// Packet flag bit indicating a key frame.
    /// </summary>
    public const byte KeyFrameFlag = 0x1;

    /// <summary>
    /// Transport name reported by the adb-forward bootstrap.
    /// </summary>
    public const string AdbForwardTransport = "adb-forward";

    /// <summary>
    /// Transport name reported by the in-process null transport.
    /// </summary>
    public const string NullTransport = "null";

    /// <summary>
    /// Server version label reported by the in-process null transport.
    /// </summary>
    public const string NullTransportVersion = "null-transport";

    /// <summary>
    /// Codec identifier for H.264 payloads.
    /// </summary>
    public const byte H264CodecId = 1;

    /// <summary>
    /// Codec identifier for H.265 payloads.
    /// </summary>
    public const byte H265CodecId = 2;

    /// <summary>
    /// Packet type identifier for codec/config bootstrap packets.
    /// </summary>
    public const byte ConfigPacketTypeId = 1;

    /// <summary>
    /// Packet type identifier for video frame packets.
    /// </summary>
    public const byte FramePacketTypeId = 2;

    /// <summary>
    /// Packet type identifier for rotation reset packets.
    /// </summary>
    public const byte RotationResetPacketTypeId = 3;

    /// <summary>
    /// Packet type identifier for end-of-stream packets.
    /// </summary>
    public const byte StreamEndPacketTypeId = 4;

    /// <summary>
    /// Packet type identifier for server-side error packets.
    /// </summary>
    public const byte ServerErrorPacketTypeId = 5;
}