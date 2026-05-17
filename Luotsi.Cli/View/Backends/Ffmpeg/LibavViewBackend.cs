using System.Diagnostics;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Luotsi.Cli.Infrastructure;

namespace Luotsi.Cli.View.Backends.Ffmpeg;

/// <summary>
/// Binds libav native libraries for the current process.
/// </summary>
public interface ILibavNativeLibraryBinder
{
    /// <summary>
    /// Binds FFmpeg native libraries from the provided root path.
    /// </summary>
    /// <param name="rootPath">Directory containing FFmpeg native libraries, or <see langword="null"/> to use the process path.</param>
    void Bind(string? rootPath);
}

/// <summary>
/// Default libav native library binder.
/// </summary>
public sealed class DefaultLibavNativeLibraryBinder : ILibavNativeLibraryBinder
{
    /// <inheritdoc />
    public void Bind(string? rootPath)
    {
        ffmpeg.RootPath = rootPath ?? string.Empty;
        _ = ffmpeg.avutil_version();
        _ = ffmpeg.avcodec_version();
        _ = ffmpeg.swscale_version();
    }
}

/// <summary>
/// Resolves and probes libav native libraries for the current host.
/// </summary>
public sealed class LibavNativeLibraryLoader(IEnvironmentVariables environment, ILibavNativeLibraryBinder? binder = null)
{
    private readonly ILibavNativeLibraryBinder _binder = binder ?? new DefaultLibavNativeLibraryBinder();
    private readonly ViewHostPathResolver _pathResolver = new(environment ?? throw new ArgumentNullException(nameof(environment)));
    private bool _loaded;
    private string? _loadedRootPath;

    /// <summary>
    /// Ensures FFmpeg native libraries can be loaded.
    /// </summary>
    /// <returns>The resolved root path, or an empty string when the process path was used.</returns>
    public string EnsureLoaded()
    {
        if (_loaded)
        {
            return _loadedRootPath ?? string.Empty;
        }

        Exception? lastError = null;
        var candidates = _pathResolver.GetFfmpegLibraryRootCandidates().ToArray();
        foreach (var candidate in candidates)
        {
            try
            {
                _binder.Bind(candidate);
                _loaded = true;
                _loadedRootPath = candidate;
                return candidate ?? string.Empty;
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                lastError = ex;
            }
        }

        var renderedCandidates = string.Join(", ",
            candidates.Select(static candidate => string.IsNullOrWhiteSpace(candidate) ? "<process-path>" : candidate));
        throw new InvalidOperationException(
            $"Unable to load FFmpeg native libraries. Set DEVICE_E2E_FFMPEG_ROOT to a directory containing the host-native FFmpeg shared libraries or place them under ffmpeg/bin next to the repo or published app. Probed: {renderedCandidates}.",
            lastError);
    }
}

/// <summary>
/// Creates libav video decoders for a view session.
/// </summary>
public interface ILibavVideoDecoderFactory
{
    /// <summary>
    /// Creates a decoder for the negotiated connection.
    /// </summary>
    /// <param name="connectionInfo">Negotiated connection metadata.</param>
    /// <returns>Decoder instance.</returns>
    ILibavVideoDecoder Create(ViewConnectionInfo connectionInfo);
}

/// <summary>
/// Decodes compressed view packets into view frames.
/// </summary>
public interface ILibavVideoDecoder : IDisposable
{
    /// <summary>
    /// Decodes a compressed packet.
    /// </summary>
    /// <param name="packet">Packet to decode.</param>
    /// <returns>Decoded frames.</returns>
    IReadOnlyList<ViewFrame> Decode(ViewPacket packet);

    /// <summary>
    /// Flushes delayed frames from the decoder.
    /// </summary>
    /// <returns>Decoded frames.</returns>
    IReadOnlyList<ViewFrame> Flush();

    /// <summary>
    /// Resets the decoder state after a stream restart.
    /// </summary>
    void Reset();
}

/// <summary>
/// Default factory for native libav decoders.
/// </summary>
public sealed class DefaultLibavVideoDecoderFactory(LibavNativeLibraryLoader libraryLoader) : ILibavVideoDecoderFactory
{
    private readonly LibavNativeLibraryLoader _libraryLoader = libraryLoader ?? throw new ArgumentNullException(nameof(libraryLoader));

    /// <inheritdoc />
    public ILibavVideoDecoder Create(ViewConnectionInfo connectionInfo)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);

        _libraryLoader.EnsureLoaded();
        return new LibavVideoDecoder(connectionInfo.Codec);
    }
}

/// <summary>
/// Native libav-backed decoder backend.
/// </summary>
public sealed class LibavViewBackend(ILibavVideoDecoderFactory decoderFactory) : IViewBackend
{
    private readonly ILibavVideoDecoderFactory _decoderFactory = decoderFactory ?? throw new ArgumentNullException(nameof(decoderFactory));
    private readonly ViewStatsTracker _statsTracker = new();
    private ViewConnectionInfo? _connectionInfo;
    private IViewRenderer? _renderer;
    private IViewRecorder? _recorder;
    private ILibavVideoDecoder? _decoder;
    private bool _rendererInitialized;

    public string Name => "ffmpeg-native";

    public async Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default)
    {
        _connectionInfo = connectionInfo ?? throw new ArgumentNullException(nameof(connectionInfo));
        _renderer = renderer;
        _recorder = recorder;
        _decoder = _decoderFactory.Create(connectionInfo);

        if (_recorder is not null)
        {
            await _recorder.InitializeAsync(connectionInfo, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default)
    {
        if (_decoder is null || _connectionInfo is null)
        {
            throw new InvalidOperationException("Libav backend was not initialized.");
        }

        await foreach (var packet in packets.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            switch (packet.PacketType)
            {
                case ViewPacketType.Config:
                case ViewPacketType.Frame:
                    if (_recorder is not null)
                    {
                        await _recorder.WritePacketAsync(packet, cancellationToken).ConfigureAwait(false);
                    }

                    if (packet.Payload.IsEmpty)
                    {
                        break;
                    }

                    await PresentFramesAsync(_decoder.Decode(packet), cancellationToken).ConfigureAwait(false);
                    break;

                case ViewPacketType.RotationReset:
                    await PresentFramesAsync(_decoder.Flush(), cancellationToken).ConfigureAwait(false);
                    _decoder.Reset();
                    break;

                case ViewPacketType.StreamEnd:
                    await PresentFramesAsync(_decoder.Flush(), cancellationToken).ConfigureAwait(false);
                    if (_recorder is not null)
                    {
                        await _recorder.CompleteAsync(cancellationToken).ConfigureAwait(false);
                    }

                    return;

                case ViewPacketType.ServerError:
                    throw new InvalidOperationException($"View server error: {System.Text.Encoding.UTF8.GetString(packet.Payload.Span)}");

                default:
                    throw new InvalidOperationException($"Unsupported view packet type '{packet.PacketType}'.");
            }
        }

        await PresentFramesAsync(_decoder.Flush(), cancellationToken).ConfigureAwait(false);
        if (_recorder is not null)
        {
            await _recorder.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        _decoder?.Dispose();
        _decoder = null;
        return ValueTask.CompletedTask;
    }

    private async Task PresentFramesAsync(IReadOnlyList<ViewFrame> frames, CancellationToken cancellationToken)
    {
        if (frames.Count == 0)
        {
            return;
        }

        _statsTracker.RecordDecoded(frames);

        if (_renderer is null)
        {
            return;
        }

        if (!_rendererInitialized)
        {
            await _renderer.InitializeAsync(
                    new ViewDisplayInfo(frames[0].Width, frames[0].Height, _connectionInfo!.Codec, frames[0].PixelFormat),
                    cancellationToken)
                .ConfigureAwait(false);
            _rendererInitialized = true;
        }

        foreach (var frame in frames)
        {
            await _renderer.PresentAsync(frame, cancellationToken).ConfigureAwait(false);
            await _renderer.UpdateStatsAsync(_statsTracker.RecordPresented(frame), cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed class ViewStatsTracker
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private long? _originElapsedUs;
    private long? _originPtsUs;
    private int _decodedFrames;
    private int _presentedFrames;

    public void RecordDecoded(IReadOnlyList<ViewFrame> frames)
    {
        foreach (var frame in frames)
        {
            _decodedFrames++;
            EnsureOrigin(frame.PresentationTimestampUs);
        }
    }

    public ViewStats RecordPresented(ViewFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        _presentedFrames++;
        EnsureOrigin(frame.PresentationTimestampUs);

        var elapsedUs = GetElapsedUs();
        var originElapsedUs = _originElapsedUs ?? elapsedUs;
        var originPtsUs = _originPtsUs ?? frame.PresentationTimestampUs;
        var runtimeUs = Math.Max(1L, elapsedUs - originElapsedUs);
        var mediaUs = Math.Max(0L, frame.PresentationTimestampUs - originPtsUs);
        var decodeFps = _decodedFrames * 1_000_000d / runtimeUs;
        var presentFps = _presentedFrames * 1_000_000d / runtimeUs;
        var latencyMs = Math.Max(0L, (runtimeUs - mediaUs) / 1_000L);

        return new ViewStats(_decodedFrames, _presentedFrames, 0, decodeFps, presentFps, latencyMs);
    }

    private void EnsureOrigin(long ptsUs)
    {
        if (_originElapsedUs.HasValue)
        {
            return;
        }

        _originElapsedUs = GetElapsedUs();
        _originPtsUs = ptsUs;
    }

    private long GetElapsedUs() => (long)(_clock.ElapsedTicks * 1_000_000d / Stopwatch.Frequency);
}

internal sealed unsafe class LibavVideoDecoder : ILibavVideoDecoder
{
    private const int SwscaleBilinear = 2;
    private const int SwscaleFixedPointUnit = 1 << 16;

    private AVCodecContext* _codecContext;
    private AVFrame* _frame;
    private AVFrame* _convertedFrame;
    private AVPacket* _packet;
    private SwsContext* _scaleContext;
    private int _convertedFrameBufferSize;
    private AVPixelFormat _convertedSourcePixelFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    private AVColorRange _convertedSourceColorRange = AVColorRange.AVCOL_RANGE_UNSPECIFIED;
    private AVColorSpace _convertedSourceColorSpace = AVColorSpace.AVCOL_SPC_UNSPECIFIED;
    private long _frameSequence;

    public LibavVideoDecoder(string codec)
    {
        var codecId = ResolveCodecId(codec);
        var codecDefinition = ffmpeg.avcodec_find_decoder(codecId);
        if (codecDefinition == null)
        {
            throw new InvalidOperationException($"FFmpeg decoder for codec '{codec}' is not available.");
        }

        _codecContext = ffmpeg.avcodec_alloc_context3(codecDefinition);
        if (_codecContext == null)
        {
            throw new InvalidOperationException($"Failed to allocate FFmpeg decoder context for codec '{codec}'.");
        }

        _codecContext->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
        _codecContext->flags2 |= ffmpeg.AV_CODEC_FLAG2_CHUNKS;

        ThrowIfError(ffmpeg.avcodec_open2(_codecContext, codecDefinition, null), $"open decoder for codec '{codec}'");

        _frame = ffmpeg.av_frame_alloc();
        if (_frame == null)
        {
            throw new InvalidOperationException("Failed to allocate FFmpeg frame.");
        }

        _convertedFrame = ffmpeg.av_frame_alloc();
        if (_convertedFrame == null)
        {
            throw new InvalidOperationException("Failed to allocate FFmpeg output frame.");
        }

        _packet = ffmpeg.av_packet_alloc();
        if (_packet == null)
        {
            throw new InvalidOperationException("Failed to allocate FFmpeg packet.");
        }
    }

    public IReadOnlyList<ViewFrame> Decode(ViewPacket packet)
    {
        EnsureNotDisposed();

        ffmpeg.av_packet_unref(_packet);
        ThrowIfError(ffmpeg.av_new_packet(_packet, packet.Payload.Length), $"allocate FFmpeg packet for sequence {packet.Sequence}");
        packet.Payload.Span.CopyTo(new Span<byte>(_packet->data, packet.Payload.Length));
        _packet->pts = packet.PresentationTimestampUs;
        _packet->dts = packet.PresentationTimestampUs;
        _packet->flags = packet.IsKeyFrame ? ffmpeg.AV_PKT_FLAG_KEY : 0;

        ThrowIfError(ffmpeg.avcodec_send_packet(_codecContext, _packet), $"send packet sequence {packet.Sequence}");
        var frames = ReceiveFrames(packet.PresentationTimestampUs);
        ffmpeg.av_packet_unref(_packet);
        return frames;
    }

    public IReadOnlyList<ViewFrame> Flush()
    {
        EnsureNotDisposed();

        var error = ffmpeg.avcodec_send_packet(_codecContext, null);
        if (error < 0 && error != ffmpeg.AVERROR_EOF)
        {
            ThrowIfError(error, "flush decoder");
        }

        return ReceiveFrames(0);
    }

    public void Reset()
    {
        EnsureNotDisposed();
        ffmpeg.avcodec_flush_buffers(_codecContext);
    }

    public void Dispose()
    {
        if (_packet != null)
        {
            var packet = _packet;
            ffmpeg.av_packet_free(&packet);
            _packet = null;
        }

        if (_frame != null)
        {
            var frame = _frame;
            ffmpeg.av_frame_free(&frame);
            _frame = null;
        }

        if (_convertedFrame != null)
        {
            var convertedFrame = _convertedFrame;
            ffmpeg.av_frame_free(&convertedFrame);
            _convertedFrame = null;
        }

        if (_scaleContext != null)
        {
            ffmpeg.sws_freeContext(_scaleContext);
            _scaleContext = null;
        }

        if (_codecContext != null)
        {
            var codecContext = _codecContext;
            ffmpeg.avcodec_free_context(&codecContext);
            _codecContext = null;
        }
    }

    private List<ViewFrame> ReceiveFrames(long fallbackPresentationTimestampUs)
    {
        var frames = new List<ViewFrame>();
        while (true)
        {
            var error = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
            if (error == ffmpeg.AVERROR(ffmpeg.EAGAIN) || error == ffmpeg.AVERROR_EOF)
            {
                break;
            }

            ThrowIfError(error, "receive decoded frame");
            PrepareConvertedFrame();
            ThrowIfError(ffmpeg.av_frame_make_writable(_convertedFrame), "prepare BGRA frame buffer");

            var scaledHeight = ffmpeg.sws_scale(
                _scaleContext,
                _frame->data,
                _frame->linesize,
                0,
                _frame->height,
                _convertedFrame->data,
                _convertedFrame->linesize);
            if (scaledHeight <= 0)
            {
                throw new InvalidOperationException("FFmpeg failed to scale the decoded frame to BGRA.");
            }

            var pixelData = new byte[_convertedFrameBufferSize];
            fixed (byte* pixelDataPointer = pixelData)
            {
                var convertedFrameData = NarrowDataPointers(_convertedFrame->data);
                var convertedFrameLineSizes = NarrowLineSizes(_convertedFrame->linesize);
                ThrowIfError(
                    ffmpeg.av_image_copy_to_buffer(
                        pixelDataPointer,
                        _convertedFrameBufferSize,
                        convertedFrameData,
                        convertedFrameLineSizes,
                        AVPixelFormat.AV_PIX_FMT_BGRA,
                        _convertedFrame->width,
                        _convertedFrame->height,
                        1),
                    "copy BGRA frame data");
            }

            var presentationTimestampUs = _frame->pts != ffmpeg.AV_NOPTS_VALUE ? _frame->pts : fallbackPresentationTimestampUs;
            frames.Add(new ViewFrame(
                ++_frameSequence,
                presentationTimestampUs,
                _convertedFrame->width,
                _convertedFrame->height,
                AVPixelFormat.AV_PIX_FMT_BGRA.ToString(),
                null)
            {
                PixelData = pixelData,
                RowStride = _convertedFrame->linesize[0]
            });
            ffmpeg.av_frame_unref(_frame);
        }

        return frames;
    }

    private void PrepareConvertedFrame()
    {
        var sourcePixelFormat = NormalizeScaleInputPixelFormat((AVPixelFormat)_frame->format);
        var sourceColorRange = ResolveSourceColorRange((AVPixelFormat)_frame->format, (AVColorRange)_frame->color_range);
        var sourceColorSpace = ResolveSourceColorSpace((AVColorSpace)_frame->colorspace, _frame->height);

        if (_convertedFrame != null &&
            _convertedFrame->width == _frame->width &&
            _convertedFrame->height == _frame->height &&
            _convertedSourcePixelFormat == sourcePixelFormat &&
            _convertedSourceColorRange == sourceColorRange &&
            _convertedSourceColorSpace == sourceColorSpace &&
            _scaleContext != null)
        {
            return;
        }

        ffmpeg.av_frame_unref(_convertedFrame);
        _convertedFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        _convertedFrame->width = _frame->width;
        _convertedFrame->height = _frame->height;
        ThrowIfError(ffmpeg.av_frame_get_buffer(_convertedFrame, 1), "allocate BGRA output frame");

        _scaleContext = ffmpeg.sws_getCachedContext(
            _scaleContext,
            _frame->width,
            _frame->height,
            sourcePixelFormat,
            _frame->width,
            _frame->height,
            AVPixelFormat.AV_PIX_FMT_BGRA,
            SwscaleBilinear,
            null,
            null,
            null);
        if (_scaleContext == null)
        {
            throw new InvalidOperationException("FFmpeg failed to create a BGRA conversion context.");
        }

        ConfigureScaleContext(sourceColorSpace, sourceColorRange);

        _convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(
            AVPixelFormat.AV_PIX_FMT_BGRA,
            _convertedFrame->width,
            _convertedFrame->height,
            1);
        if (_convertedFrameBufferSize < 0)
        {
            ThrowIfError(_convertedFrameBufferSize, "size BGRA output frame buffer");
        }

        _convertedSourcePixelFormat = sourcePixelFormat;
        _convertedSourceColorRange = sourceColorRange;
        _convertedSourceColorSpace = sourceColorSpace;
    }

    private void ConfigureScaleContext(AVColorSpace sourceColorSpace, AVColorRange sourceColorRange)
    {
        var coefficients = ffmpeg.sws_getCoefficients(ResolveSwscaleColorSpace(sourceColorSpace));
        if (coefficients == null)
        {
            throw new InvalidOperationException($"FFmpeg did not provide swscale coefficients for colorspace '{sourceColorSpace}'.");
        }

        var coefficientArray = CopyCoefficients(coefficients);

        var sourceRange = sourceColorRange == AVColorRange.AVCOL_RANGE_JPEG ? 1 : 0;
        ThrowIfError(
            ffmpeg.sws_setColorspaceDetails(
                _scaleContext,
                coefficientArray,
                sourceRange,
                coefficientArray,
                1,
                0,
                SwscaleFixedPointUnit,
                SwscaleFixedPointUnit),
            $"configure swscale colorspace '{sourceColorSpace}' and range '{sourceColorRange}'");
    }

    private static AVPixelFormat NormalizeScaleInputPixelFormat(AVPixelFormat sourcePixelFormat) => sourcePixelFormat switch
    {
        AVPixelFormat.AV_PIX_FMT_YUVJ420P => AVPixelFormat.AV_PIX_FMT_YUV420P,
        AVPixelFormat.AV_PIX_FMT_YUVJ422P => AVPixelFormat.AV_PIX_FMT_YUV422P,
        AVPixelFormat.AV_PIX_FMT_YUVJ444P => AVPixelFormat.AV_PIX_FMT_YUV444P,
        AVPixelFormat.AV_PIX_FMT_YUVJ440P => AVPixelFormat.AV_PIX_FMT_YUV440P,
        AVPixelFormat.AV_PIX_FMT_YUVJ411P => AVPixelFormat.AV_PIX_FMT_YUV411P,
        _ => sourcePixelFormat
    };

    private static AVColorRange ResolveSourceColorRange(AVPixelFormat originalPixelFormat, AVColorRange sourceColorRange)
    {
        if (sourceColorRange is AVColorRange.AVCOL_RANGE_JPEG or AVColorRange.AVCOL_RANGE_MPEG)
        {
            return sourceColorRange;
        }

        return originalPixelFormat switch
        {
            AVPixelFormat.AV_PIX_FMT_YUVJ420P or
            AVPixelFormat.AV_PIX_FMT_YUVJ422P or
            AVPixelFormat.AV_PIX_FMT_YUVJ444P or
            AVPixelFormat.AV_PIX_FMT_YUVJ440P or
            AVPixelFormat.AV_PIX_FMT_YUVJ411P => AVColorRange.AVCOL_RANGE_JPEG,
            _ => AVColorRange.AVCOL_RANGE_MPEG
        };
    }

    private static AVColorSpace ResolveSourceColorSpace(AVColorSpace sourceColorSpace, int frameHeight)
    {
        if (sourceColorSpace is not AVColorSpace.AVCOL_SPC_UNSPECIFIED and not AVColorSpace.AVCOL_SPC_RESERVED)
        {
            return sourceColorSpace;
        }

        return frameHeight > 576 ? AVColorSpace.AVCOL_SPC_BT709 : AVColorSpace.AVCOL_SPC_SMPTE170M;
    }

    private static int ResolveSwscaleColorSpace(AVColorSpace sourceColorSpace) => sourceColorSpace switch
    {
        AVColorSpace.AVCOL_SPC_BT709 => ffmpeg.SWS_CS_ITU709,
        AVColorSpace.AVCOL_SPC_FCC => ffmpeg.SWS_CS_FCC,
        AVColorSpace.AVCOL_SPC_BT470BG => ffmpeg.SWS_CS_ITU601,
        AVColorSpace.AVCOL_SPC_SMPTE170M => ffmpeg.SWS_CS_SMPTE170M,
        AVColorSpace.AVCOL_SPC_SMPTE240M => ffmpeg.SWS_CS_SMPTE240M,
        AVColorSpace.AVCOL_SPC_BT2020_NCL or AVColorSpace.AVCOL_SPC_BT2020_CL => ffmpeg.SWS_CS_BT2020,
        _ => ffmpeg.SWS_CS_DEFAULT
    };

    private static int_array4 CopyCoefficients(int* coefficients)
    {
        var copied = new int_array4();
        for (uint index = 0; index < 4; index++)
        {
            copied[index] = coefficients[index];
        }

        return copied;
    }

    private static byte_ptrArray4 NarrowDataPointers(byte_ptrArray8 value)
    {
        var narrowed = new byte_ptrArray4();
        for (uint index = 0; index < 4; index++)
        {
            narrowed[index] = value[index];
        }

        return narrowed;
    }

    private static int_array4 NarrowLineSizes(int_array8 value)
    {
        var narrowed = new int_array4();
        for (uint index = 0; index < 4; index++)
        {
            narrowed[index] = value[index];
        }

        return narrowed;
    }

    private static AVCodecID ResolveCodecId(string codec) =>
        codec.ToLowerInvariant() switch
        {
            "h264" => AVCodecID.AV_CODEC_ID_H264,
            "h265" or "hevc" => AVCodecID.AV_CODEC_ID_HEVC,
            _ => throw new InvalidOperationException($"Unsupported FFmpeg codec '{codec}'.")
        };

    private static void ThrowIfError(int error, string operation)
    {
        if (error >= 0)
        {
            return;
        }

        throw new InvalidOperationException($"FFmpeg failed to {operation}: error {error}.");
    }

    private void EnsureNotDisposed()
    {
        if (_codecContext == null)
        {
            throw new ObjectDisposedException(nameof(LibavVideoDecoder));
        }
    }
}