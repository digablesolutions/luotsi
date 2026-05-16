using VisitLab.Cli.Artifacts;
using VisitLab.Cli.Infrastructure;

namespace VisitLab.Cli.View;

/// <summary>
/// Runs a long-lived device mirror session.
/// </summary>
public interface IViewSession
{
    /// <summary>
    /// Runs the view session.
    /// </summary>
    /// <param name="options">View session options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Process exit code.</returns>
    Task<int> RunAsync(ViewOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates view sessions for the CLI.
/// </summary>
public interface IViewSessionFactory
{
    /// <summary>
    /// Creates a view session.
    /// </summary>
    /// <param name="deviceHost">Device host for future control and overlay integration.</param>
    /// <param name="artifacts">Artifact session for the command run.</param>
    /// <returns>View session instance.</returns>
    IViewSession Create(IDeviceHost deviceHost, ArtifactSession artifacts);
}

/// <summary>
/// Options for a view session.
/// </summary>
/// <param name="DeviceSelector">ADB device selector.</param>
/// <param name="AdbExecutable">ADB executable path.</param>
/// <param name="Decoder">Requested host decoder backend.</param>
/// <param name="Headless">Whether presentation is disabled.</param>
/// <param name="RecordPath">Optional recording output path.</param>
/// <param name="MaxSize">Maximum mirrored size.</param>
/// <param name="MaxFps">Maximum frame rate.</param>
/// <param name="VideoBitRate">Requested video bit rate.</param>
/// <param name="OverlayScreenState">Whether screen-state overlays are enabled.</param>
/// <param name="OverlayTelemetry">Whether telemetry overlays are enabled.</param>
/// <param name="StatsIntervalMs">Minimum interval between emitted JSONL stats updates. Set to zero to disable JSONL stats emission.</param>
/// <param name="RendererStatsIntervalMs">Minimum interval between forwarded renderer stats updates. Set to zero to forward every renderer stats update.</param>
public sealed record ViewOptions(
    string DeviceSelector,
    string AdbExecutable,
    string Codec,
    string Decoder,
    bool Headless,
    string? RecordPath,
    int MaxSize,
    int MaxFps,
    string VideoBitRate,
    bool OverlayScreenState,
    bool OverlayTelemetry,
    int StatsIntervalMs = 1000,
    int RendererStatsIntervalMs = 0);

/// <summary>
/// Bootstraps the device-side stream transport.
/// </summary>
public interface IViewTransportBootstrap
{
    /// <summary>
    /// Starts the transport and returns connection metadata.
    /// </summary>
    /// <param name="request">Transport start request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connection metadata.</returns>
    Task<ViewConnectionInfo> StartAsync(ViewStartRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the transport.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Parameters used to start the device-side stream transport.
/// </summary>
/// <param name="AdbExecutable">ADB executable path.</param>
/// <param name="DeviceSelector">ADB device selector.</param>
/// <param name="MaxSize">Maximum mirrored size.</param>
/// <param name="MaxFps">Maximum frame rate.</param>
/// <param name="VideoBitRate">Requested video bit rate.</param>
/// <param name="Codec">Requested codec.</param>
public sealed record ViewStartRequest(
    string AdbExecutable,
    string DeviceSelector,
    int MaxSize,
    int MaxFps,
    string VideoBitRate,
    string Codec);

/// <summary>
/// Connection metadata for a view session.
/// </summary>
/// <param name="SessionId">Transport session identifier.</param>
/// <param name="Codec">Negotiated codec.</param>
/// <param name="Width">Reported stream width.</param>
/// <param name="Height">Reported stream height.</param>
/// <param name="LocalPort">Local forwarded port.</param>
/// <param name="ServerVersion">Device-side server version.</param>
/// <param name="Transport">Transport description.</param>
public sealed record ViewConnectionInfo(
    string SessionId,
    string Codec,
    int ProtocolVersion,
    int Width,
    int Height,
    int LocalPort,
    string ServerVersion,
    string Transport);

/// <summary>
/// Decodes and optionally presents a stream of view packets.
/// </summary>
public interface IViewBackend : IAsyncDisposable
{
    /// <summary>
    /// Gets the backend name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Initializes the backend.
    /// </summary>
    /// <param name="connectionInfo">Connection metadata.</param>
    /// <param name="renderer">Optional renderer.</param>
    /// <param name="recorder">Optional recorder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the backend over a packet stream.
    /// </summary>
    /// <param name="packets">Incoming packet stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates decoder backends for a view session.
/// </summary>
public interface IViewBackendFactory
{
    /// <summary>
    /// Creates a backend for the requested session options.
    /// </summary>
    /// <param name="options">View session options.</param>
    /// <returns>Backend instance.</returns>
    IViewBackend Create(ViewOptions options);
}

/// <summary>
/// Creates optional renderers for a view session.
/// </summary>
public interface IViewRendererFactory
{
    /// <summary>
    /// Creates a renderer for the requested session options.
    /// </summary>
    /// <param name="options">View session options.</param>
    /// <param name="deviceHost">Device host used for interactive input routing.</param>
    /// <returns>Renderer instance, or <see langword="null"/> when presentation is disabled.</returns>
    IViewRenderer? Create(ViewOptions options, IDeviceHost deviceHost);
}

/// <summary>
/// Creates optional recorders for a view session.
/// </summary>
public interface IViewRecorderFactory
{
    /// <summary>
    /// Creates a recorder for the requested session options.
    /// </summary>
    /// <param name="options">View session options.</param>
    /// <returns>Recorder instance, or <see langword="null"/> when recording is disabled.</returns>
    IViewRecorder? Create(ViewOptions options);
}

/// <summary>
/// Reads the private mirrored-stream transport format.
/// </summary>
public interface IViewPacketStreamReader
{
    /// <summary>
    /// Reads the startup header from a packet stream.
    /// </summary>
    /// <param name="stream">Readable transport stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed startup header.</returns>
    Task<ViewStreamHeader> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads transport packets until stream end.
    /// </summary>
    /// <param name="stream">Readable transport stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Packet stream.</returns>
    IAsyncEnumerable<ViewPacket> ReadPacketsAsync(Stream stream, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an active host-side stream connection.
/// </summary>
public interface IViewStreamConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets the readable stream.
    /// </summary>
    Stream Stream { get; }
}

/// <summary>
/// Connects to the local host-side transport endpoint.
/// </summary>
public interface IViewStreamConnector
{
    /// <summary>
    /// Connects to the local transport endpoint for a view session.
    /// </summary>
    /// <param name="connectionInfo">Connection metadata from the bootstrap phase.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Active stream connection.</returns>
    Task<IViewStreamConnection> ConnectAsync(ViewConnectionInfo connectionInfo, CancellationToken cancellationToken = default);
}

/// <summary>
/// Presents decoded frames to a local surface.
/// </summary>
public interface IViewRenderer : IAsyncDisposable
{
    /// <summary>
    /// Initializes the renderer.
    /// </summary>
    /// <param name="displayInfo">Display metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task InitializeAsync(ViewDisplayInfo displayInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Presents a decoded frame.
    /// </summary>
    /// <param name="frame">Decoded frame.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task PresentAsync(ViewFrame frame, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates renderer-visible statistics.
    /// </summary>
    /// <param name="stats">Current view statistics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task UpdateStatsAsync(ViewStats stats, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until the renderer window is closed by the operator.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task WaitForCloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Records a mirrored stream.
/// </summary>
public interface IViewRecorder : IAsyncDisposable
{
    /// <summary>
    /// Initializes recording.
    /// </summary>
    /// <param name="connectionInfo">Connection metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task InitializeAsync(ViewConnectionInfo connectionInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a packet to the recording target.
    /// </summary>
    /// <param name="packet">Compressed packet.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task WritePacketAsync(ViewPacket packet, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes recording.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task CompleteAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Display metadata for a view renderer.
/// </summary>
/// <param name="Width">Display width.</param>
/// <param name="Height">Display height.</param>
/// <param name="Codec">Stream codec.</param>
/// <param name="PixelFormat">Decoded pixel format.</param>
public sealed record ViewDisplayInfo(int Width, int Height, string Codec, string PixelFormat);

/// <summary>
/// Startup metadata for the private mirrored-stream transport.
/// </summary>
/// <param name="ProtocolVersion">Protocol version.</param>
/// <param name="Codec">Negotiated codec.</param>
/// <param name="Width">Initial width.</param>
/// <param name="Height">Initial height.</param>
/// <param name="Flags">Transport flags.</param>
public sealed record ViewStreamHeader(int ProtocolVersion, string Codec, int Width, int Height, ushort Flags);

/// <summary>
/// Transport packet type.
/// </summary>
public enum ViewPacketType : byte
{
    Config = 1,
    Frame = 2,
    RotationReset = 3,
    StreamEnd = 4,
    ServerError = 5
}

/// <summary>
/// Transport packet for mirrored media.
/// </summary>
/// <param name="PacketType">Transport packet type.</param>
/// <param name="Sequence">Packet sequence number.</param>
/// <param name="PresentationTimestampUs">Presentation timestamp in microseconds.</param>
/// <param name="IsKeyFrame">Whether the packet carries a key frame.</param>
/// <param name="Payload">Packet payload bytes.</param>
public sealed record ViewPacket(
    ViewPacketType PacketType,
    long Sequence,
    long PresentationTimestampUs,
    bool IsKeyFrame,
    ReadOnlyMemory<byte> Payload)
{
    /// <summary>
    /// Gets whether the packet carries codec configuration.
    /// </summary>
    public bool IsConfig => PacketType == ViewPacketType.Config;
}

/// <summary>
/// Decoded frame metadata.
/// </summary>
/// <param name="Sequence">Frame sequence number.</param>
/// <param name="PresentationTimestampUs">Presentation timestamp in microseconds.</param>
/// <param name="Width">Frame width.</param>
/// <param name="Height">Frame height.</param>
/// <param name="PixelFormat">Decoded pixel format.</param>
/// <param name="Surface">Renderer-specific surface object.</param>
public sealed record ViewFrame(
    long Sequence,
    long PresentationTimestampUs,
    int Width,
    int Height,
    string PixelFormat,
    object? Surface)
{
    /// <summary>
    /// Gets the tightly packed renderer-ready pixel buffer for the frame.
    /// </summary>
    public ReadOnlyMemory<byte> PixelData { get; init; } = ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// Gets the number of bytes per output row in <see cref="PixelData"/>.
    /// </summary>
    public int RowStride { get; init; }
}

/// <summary>
/// Rolling view statistics.
/// </summary>
/// <param name="DecodedFrames">Decoded frame count.</param>
/// <param name="PresentedFrames">Presented frame count.</param>
/// <param name="DroppedFrames">Dropped frame count.</param>
/// <param name="DecodeFps">Current decode FPS.</param>
/// <param name="PresentFps">Current present FPS.</param>
/// <param name="EndToEndLatencyMs">Estimated end-to-end latency in milliseconds.</param>
public sealed record ViewStats(
    int DecodedFrames,
    int PresentedFrames,
    int DroppedFrames,
    double DecodeFps,
    double PresentFps,
    long EndToEndLatencyMs);