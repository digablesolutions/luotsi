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
    /// Codec identifier for H.264 payloads.
    /// </summary>
    public const byte H264CodecId = 1;

    /// <summary>
    /// Codec identifier for H.265 payloads.
    /// </summary>
    public const byte H265CodecId = 2;
}