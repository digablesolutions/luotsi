package dev.luotsi.view

import android.content.Context
import android.content.Intent
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.util.DisplayMetrics
import android.view.Surface
import android.view.WindowManager
import java.io.OutputStream
import java.nio.ByteBuffer
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.max
import kotlin.math.roundToInt

internal class MediaProjectionCaptureSession(
    private val context: Context,
    private val options: Options,
    private val output: OutputStream,
) {
    private val stopped = AtomicBoolean(false)
    private var mediaProjection: MediaProjection? = null
    private var virtualDisplay: VirtualDisplay? = null
    private var encoder: MediaCodec? = null
    private var inputSurface: Surface? = null

    fun run() {
        MediaCodecPacketizer(output).use { packetizer ->
            val captureSize = DisplayCaptureSize.resolve(context, options.maxSize)
            Log.i(TAG, "MediaProjection session starting ${captureSize.width}x${captureSize.height}@${options.maxFps} bitrate=${options.videoBitRate}")
            packetizer.writeHeader("h264", captureSize.width, captureSize.height)
            packetizer.writeDiagnostic(
                phase = "socket_client_connected",
                status = "succeeded",
                message = "Host stream client connected to the MediaProjection helper.",
                captureBackend = "mediaprojection",
                socketName = options.socketName,
                codec = options.codec,
                width = captureSize.width,
                height = captureSize.height,
                maxFps = options.maxFps,
                videoBitRate = options.videoBitRate,
            )
            packetizer.writeDiagnostic(
                phase = "foreground_service",
                status = "succeeded",
                message = "MediaProjection foreground service is running.",
                captureBackend = "mediaprojection",
            )

            if (!options.codec.equals("h264", ignoreCase = true)) {
                packetizer.writeDiagnostic(
                    phase = "codec_preflight",
                    status = "failed",
                    message = "The MediaProjection helper rejected the requested codec.",
                    captureBackend = "mediaprojection",
                    codec = options.codec,
                    error = "unsupported_codec",
                )
                packetizer.writeServerError("The Android MediaProjection helper currently supports only h264 capture.")
                packetizer.writeStreamEnd()
                packetizer.flush()
                return
            }

            try {
                startProjection(captureSize, packetizer)
                drainEncoder(packetizer)
            } catch (error: Exception) {
                Log.e(TAG, "MediaProjection session failed", error)
                packetizer.writeDiagnostic(
                    phase = "mediaprojection_session",
                    status = "failed",
                    message = "MediaProjection capture failed.",
                    captureBackend = "mediaprojection",
                    error = error.message ?: error::class.java.simpleName,
                )
                packetizer.writeServerError(error.message ?: error::class.java.simpleName)
            } finally {
                stop()
                packetizer.writeStreamEnd()
                packetizer.flush()
            }
        }
    }

    fun stop() {
        if (!stopped.compareAndSet(false, true)) {
            return
        }

        runCatching { encoder?.signalEndOfInputStream() }
        runCatching { virtualDisplay?.release() }
        virtualDisplay = null
        runCatching { inputSurface?.release() }
        inputSurface = null
        runCatching { encoder?.stop() }
        runCatching { encoder?.release() }
        encoder = null
        runCatching { mediaProjection?.stop() }
        mediaProjection = null
    }

    private fun startProjection(captureSize: DisplayCaptureSize, packetizer: MediaCodecPacketizer) {
        val projectionManager = context.getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        val projection = projectionManager.getMediaProjection(options.resultCode, options.resultData)
            ?: throw IllegalStateException("MediaProjection consent data was rejected by Android.")
        mediaProjection = projection
        Log.i(TAG, "MediaProjection object acquired")
        packetizer.writeDiagnostic(
            phase = "mediaprojection_consent",
            status = "succeeded",
            message = "Android accepted the MediaProjection consent token.",
            captureBackend = "mediaprojection",
        )

        projection.registerCallback(object : MediaProjection.Callback() {
            override fun onStop() {
                stopped.set(true)
            }
        }, Handler(Looper.getMainLooper()))

        val mediaFormat = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, captureSize.width, captureSize.height).apply {
            setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface)
            setInteger(MediaFormat.KEY_BIT_RATE, parseBitRate(options.videoBitRate))
            setInteger(MediaFormat.KEY_FRAME_RATE, options.maxFps.coerceIn(1, 120))
            setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1)
            setInteger(MediaFormat.KEY_BITRATE_MODE, MediaCodecInfo.EncoderCapabilities.BITRATE_MODE_CBR)
        }

        val codec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
        encoder = codec
        Log.i(TAG, "Configuring AVC encoder ${captureSize.width}x${captureSize.height}")
        packetizer.writeDiagnostic(
            phase = "encoder_setup",
            status = "starting",
            message = "Configuring the Android AVC encoder.",
            captureBackend = "mediaprojection",
            codec = "h264",
            width = captureSize.width,
            height = captureSize.height,
            maxFps = options.maxFps,
            videoBitRate = options.videoBitRate,
        )
        codec.configure(mediaFormat, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
        inputSurface = codec.createInputSurface()
        codec.start()
        Log.i(TAG, "AVC encoder started")
        packetizer.writeDiagnostic(
            phase = "encoder_setup",
            status = "succeeded",
            message = "Android AVC encoder started.",
            captureBackend = "mediaprojection",
            codec = "h264",
            width = captureSize.width,
            height = captureSize.height,
        )

        virtualDisplay = projection.createVirtualDisplay(
            "LuotsiMediaProjection",
            captureSize.width,
            captureSize.height,
            captureSize.densityDpi,
            DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
            inputSurface,
            null,
            null,
        )
        Log.i(TAG, "VirtualDisplay created")
        packetizer.writeDiagnostic(
            phase = "virtual_display",
            status = "succeeded",
            message = "MediaProjection virtual display is ready.",
            captureBackend = "mediaprojection",
            width = captureSize.width,
            height = captureSize.height,
        )
    }

    private fun drainEncoder(packetizer: MediaCodecPacketizer) {
        val codec = encoder ?: throw IllegalStateException("MediaCodec was not initialized.")
        val bufferInfo = MediaCodec.BufferInfo()

        while (!stopped.get()) {
            when (val outputIndex = codec.dequeueOutputBuffer(bufferInfo, 100_000L)) {
                MediaCodec.INFO_TRY_AGAIN_LATER -> Unit
                MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                    packetizer.writeDiagnostic(
                        phase = "stream_packet_health",
                        status = "succeeded",
                        message = "MediaCodec output format is available.",
                        captureBackend = "mediaprojection",
                        codec = "h264",
                    )
                    writeFormatConfig(codec.outputFormat, packetizer)
                }
                else -> {
                    if (outputIndex >= 0) {
                        packetizer.writeEncodedBuffer(codec, outputIndex, bufferInfo)
                        codec.releaseOutputBuffer(outputIndex, false)
                        if ((bufferInfo.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) {
                            return
                        }
                    }
                }
            }
        }
    }

    private fun writeFormatConfig(format: MediaFormat, packetizer: MediaCodecPacketizer) {
        val sps = format.getByteBuffer("csd-0")?.toByteArray() ?: ByteArray(0)
        val pps = format.getByteBuffer("csd-1")?.toByteArray() ?: ByteArray(0)
        if (sps.isNotEmpty()) {
            packetizer.writeCodecConfig(sps)
        }
        if (pps.isNotEmpty()) {
            packetizer.writeCodecConfig(pps)
        }
    }

    private fun ByteBuffer.toByteArray(): ByteArray {
        val duplicate = duplicate()
        val bytes = ByteArray(duplicate.remaining())
        duplicate.get(bytes)
        return bytes
    }

    internal data class Options(
        val resultCode: Int,
        val resultData: Intent,
        val socketName: String,
        val codec: String,
        val maxSize: Int,
        val maxFps: Int,
        val videoBitRate: String,
    )

    private data class DisplayCaptureSize(
        val width: Int,
        val height: Int,
        val densityDpi: Int,
    ) {
        companion object {
            fun resolve(context: Context, maxSize: Int): DisplayCaptureSize {
                val metrics = DisplayMetrics()
                val windowManager = context.getSystemService(Context.WINDOW_SERVICE) as WindowManager
                @Suppress("DEPRECATION")
                windowManager.defaultDisplay.getRealMetrics(metrics)

                val largestDimension = max(metrics.widthPixels, metrics.heightPixels)
                if (maxSize <= 0 || maxSize >= largestDimension) {
                    return DisplayCaptureSize(
                        normalizeDimension(metrics.widthPixels),
                        normalizeDimension(metrics.heightPixels),
                        metrics.densityDpi,
                    )
                }

                val scale = maxSize.toDouble() / largestDimension.toDouble()
                return DisplayCaptureSize(
                    normalizeDimension((metrics.widthPixels * scale).roundToInt()),
                    normalizeDimension((metrics.heightPixels * scale).roundToInt()),
                    metrics.densityDpi,
                )
            }

            private fun normalizeDimension(value: Int): Int {
                val evenValue = if (value % 2 == 0) value else value - 1
                return max(2, evenValue)
            }
        }
    }

    private companion object {
        private const val TAG = "LuotsiView"
        private fun parseBitRate(value: String): Int {
            val trimmed = value.trim()
            if (trimmed.isEmpty()) {
                return 8_000_000
            }

            val multiplier = when (trimmed.last().lowercaseChar()) {
                'k' -> 1_000
                'm' -> 1_000_000
                else -> 1
            }
            val number = if (multiplier == 1) trimmed else trimmed.dropLast(1)
            return number.toDoubleOrNull()
                ?.let { (it * multiplier).roundToInt() }
                ?.coerceAtLeast(100_000)
                ?: 8_000_000
        }
    }
}
