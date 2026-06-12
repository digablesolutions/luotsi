package dev.luotsi.view

import android.net.LocalServerSocket
import android.os.SystemClock
import java.io.ByteArrayOutputStream
import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.concurrent.thread
import kotlin.math.max
import kotlin.math.roundToInt

object Main {
    private const val MAGIC = 0x42414C56
    private const val PROTOCOL_VERSION = 1
    private const val CODEC_H264 = 1
    private const val TYPE_CONFIG = 1
    private const val TYPE_FRAME = 2
    private const val TYPE_STREAM_END = 4
    private const val TYPE_SERVER_ERROR = 5
    private const val TYPE_DIAGNOSTIC = 6
    private const val STREAM_HEADER_SIZE = 16
    private const val PACKET_HEADER_SIZE = 24
    private const val SCREENRECORD_TIME_LIMIT_SECONDS = 180

    @JvmStatic
    @Throws(Exception::class)
    fun main(args: Array<String>) {
        val options = Options.parse(args)
        val serverSocket = LocalServerSocket(options.socketName)
        try {
            serverSocket.accept().use { client ->
                client.outputStream.use { output ->
                    ScreenrecordCaptureSession(options, PacketWriter(output)).run()
                }
            }
        } finally {
            serverSocket.close()
        }
    }

    private class ScreenrecordCaptureSession(
        private val options: Options,
        private val packetWriter: PacketWriter,
    ) {
        fun run() {
            val captureSize = DisplayCaptureSize.resolve(options.maxSize)
            packetWriter.writeHeader("h264", captureSize.width, captureSize.height)
            packetWriter.writeDiagnostic(
                phase = "socket_client_connected",
                status = "succeeded",
                message = "Host stream client connected to the screenrecord helper.",
                captureBackend = "screenrecord",
                socketName = options.socketName,
                codec = options.codec,
                width = captureSize.width,
                height = captureSize.height,
                maxFps = options.maxFps,
                videoBitRate = options.videoBitRate,
            )

            if (!options.codec.equals("h264", ignoreCase = true)) {
                packetWriter.writeDiagnostic(
                    phase = "codec_preflight",
                    status = "failed",
                    message = "The screenrecord helper rejected the requested codec.",
                    captureBackend = "screenrecord",
                    codec = options.codec,
                    error = "unsupported_codec",
                )
                packetWriter.writeServerError("The Android helper currently supports only h264 capture.")
                packetWriter.writeStreamEnd()
                packetWriter.flush()
                return
            }

            var captureProcess: Process? = null
            try {
                packetWriter.writeDiagnostic(
                    phase = "screenrecord_process",
                    status = "starting",
                    message = "Starting Android screenrecord capture.",
                    captureBackend = "screenrecord",
                    width = captureSize.width,
                    height = captureSize.height,
                    maxFps = options.maxFps,
                    videoBitRate = options.videoBitRate,
                )
                captureProcess = startScreenrecordProcess(captureSize)
                packetWriter.writeDiagnostic(
                    phase = "screenrecord_process",
                    status = "succeeded",
                    message = "Android screenrecord process started.",
                    captureBackend = "screenrecord",
                )
                val stderrCollector = ProcessStreamCollector(captureProcess.errorStream)
                stderrCollector.start()

                captureProcess.inputStream.use { input ->
                    AnnexBNalUnitPacketizer(::writeNalUnit).consume(input)
                }

                val exitCode = captureProcess.waitFor()
                val stderr = stderrCollector.awaitText()
                if (exitCode != 0) {
                    packetWriter.writeDiagnostic(
                        phase = "screenrecord_process",
                        status = "failed",
                        message = "Android screenrecord process exited before a clean stream end.",
                        detail = stderr.ifBlank { null },
                        captureBackend = "screenrecord",
                        error = "exit_code_$exitCode",
                    )
                    packetWriter.writeServerError(
                        buildString {
                            append("screenrecord exited with code ")
                            append(exitCode)
                            if (stderr.isNotBlank()) {
                                append(": ")
                                append(stderr)
                            }
                        },
                    )
                }
            } catch (error: Exception) {
                runCatching {
                    packetWriter.writeDiagnostic(
                        phase = "screenrecord_process",
                        status = "failed",
                        message = "Android screenrecord capture failed.",
                        captureBackend = "screenrecord",
                        error = error.message ?: error::class.java.simpleName,
                    )
                    packetWriter.writeServerError(error.message ?: error::class.java.simpleName)
                }
            } finally {
                captureProcess?.destroy()
                runCatching {
                    packetWriter.writeStreamEnd()
                    packetWriter.flush()
                }
            }
        }

        private fun writeNalUnit(nalUnit: ByteArray) {
            if (nalUnit.isEmpty()) {
                return
            }

            val packetType = if (isCodecConfigNalUnit(nalUnit)) TYPE_CONFIG else TYPE_FRAME
            packetWriter.writePacket(packetType, isKeyFrameNalUnit(nalUnit), nalUnit)
        }

        private fun startScreenrecordProcess(captureSize: DisplayCaptureSize): Process {
            return ProcessBuilder(
                listOf(
                    "screenrecord",
                    "--output-format=h264",
                    "--size", captureSize.asArgument(),
                    "--bit-rate", options.videoBitRate,
                    "--time-limit", SCREENRECORD_TIME_LIMIT_SECONDS.toString(),
                    "-",
                ),
            )
                .redirectErrorStream(false)
                .start()
        }
    }

    private class PacketWriter(private val output: OutputStream) {
        private var sequence = 1L

        fun writeHeader(codec: String, width: Int, height: Int) {
            val buffer = ByteBuffer.allocate(STREAM_HEADER_SIZE)
                .order(ByteOrder.LITTLE_ENDIAN)
                .apply {
                    putInt(MAGIC)
                    put(PROTOCOL_VERSION.toByte())
                    put((if (codec.equals("h264", ignoreCase = true)) CODEC_H264 else CODEC_H264).toByte())
                    putShort(0.toShort())
                    putInt(width)
                    putInt(height)
                }

            output.write(buffer.array())
            output.flush()
        }

        fun writePacket(packetType: Int, keyFrame: Boolean, payload: ByteArray) {
            val buffer = ByteBuffer.allocate(PACKET_HEADER_SIZE)
                .order(ByteOrder.LITTLE_ENDIAN)
                .apply {
                    put(packetType.toByte())
                    put(if (keyFrame) 1.toByte() else 0.toByte())
                    putShort(0.toShort())
                    putLong(sequence++)
                    putLong(SystemClock.elapsedRealtimeNanos() / 1_000L)
                    putInt(payload.size)
                }

            output.write(buffer.array())
            if (payload.isNotEmpty()) {
                output.write(payload)
            }
        }

        fun writeStreamEnd() = writePacket(TYPE_STREAM_END, false, byteArrayOf())

        fun writeServerError(message: String) = writePacket(TYPE_SERVER_ERROR, false, message.encodeToByteArray())

        fun writeDiagnostic(
            phase: String,
            status: String,
            message: String,
            captureBackend: String,
            detail: String? = null,
            socketName: String? = null,
            codec: String? = null,
            width: Int? = null,
            height: Int? = null,
            maxFps: Int? = null,
            videoBitRate: String? = null,
            error: String? = null,
        ) = writePacket(
            TYPE_DIAGNOSTIC,
            false,
            HelperDiagnosticJson.build(
                phase = phase,
                status = status,
                message = message,
                detail = detail,
                captureBackend = captureBackend,
                socketName = socketName,
                codec = codec,
                width = width,
                height = height,
                maxFps = maxFps,
                videoBitRate = videoBitRate,
                error = error,
            ),
        )

        fun flush() = output.flush()
    }

    private class AnnexBNalUnitPacketizer(
        private val onNalUnit: (ByteArray) -> Unit,
    ) {
        private var buffer = ByteArray(128 * 1024)
        private var length = 0

        fun consume(input: InputStream) {
            val readBuffer = ByteArray(64 * 1024)
            while (true) {
                val count = input.read(readBuffer)
                if (count <= 0) {
                    break
                }

                append(readBuffer, count)
            }

            flush()
        }

        private fun append(source: ByteArray, count: Int) {
            ensureCapacity(length + count)
            source.copyInto(buffer, destinationOffset = length, startIndex = 0, endIndex = count)
            length += count
            emitCompleteNalUnits()
        }

        private fun flush() {
            val firstStart = findStartCode(0)
            if (firstStart < 0) {
                length = 0
                return
            }

            if (firstStart > 0) {
                discardPrefix(firstStart)
            }

            if (length > (startCodeLengthAt(0) ?: 0)) {
                emitNalUnit(0, length)
            }

            length = 0
        }

        private fun emitCompleteNalUnits() {
            val firstStart = findStartCode(0)
            if (firstStart < 0) {
                trimTrailingProbeBytes()
                return
            }

            if (firstStart > 0) {
                discardPrefix(firstStart)
            }

            while (true) {
                val startCodeLength = startCodeLengthAt(0) ?: return
                val nextStart = findStartCode(startCodeLength)
                if (nextStart < 0) {
                    return
                }

                emitNalUnit(0, nextStart)
                discardPrefix(nextStart)
            }
        }

        private fun emitNalUnit(startIndex: Int, endIndex: Int) {
            val nalUnit = buffer.copyOfRange(startIndex, endIndex)
            val startCodeLength = detectStartCodeLength(nalUnit, 0)
            if (startCodeLength == null || nalUnit.size <= startCodeLength) {
                return
            }

            onNalUnit(nalUnit)
        }

        private fun ensureCapacity(requiredCapacity: Int) {
            if (requiredCapacity <= buffer.size) {
                return
            }

            var newCapacity = buffer.size
            while (newCapacity < requiredCapacity) {
                newCapacity *= 2
            }

            buffer = buffer.copyOf(newCapacity)
        }

        private fun discardPrefix(prefixLength: Int) {
            buffer.copyInto(buffer, destinationOffset = 0, startIndex = prefixLength, endIndex = length)
            length -= prefixLength
        }

        private fun trimTrailingProbeBytes() {
            if (length <= 3) {
                return
            }

            buffer.copyInto(buffer, destinationOffset = 0, startIndex = length - 3, endIndex = length)
            length = 3
        }

        private fun findStartCode(fromIndex: Int): Int {
            if (length - fromIndex < 3) {
                return -1
            }

            for (index in fromIndex until (length - 2)) {
                val startCodeLength = startCodeLengthAt(index)
                if (startCodeLength != null) {
                    return index
                }
            }

            return -1
        }

        private fun startCodeLengthAt(index: Int): Int? = detectStartCodeLength(buffer, index)

        private fun detectStartCodeLength(bytes: ByteArray, index: Int): Int? {
            if (index + 3 >= bytes.size) {
                return null
            }

            return when {
                bytes[index] == 0.toByte() &&
                    bytes[index + 1] == 0.toByte() &&
                    bytes[index + 2] == 1.toByte() -> 3

                bytes[index] == 0.toByte() &&
                    bytes[index + 1] == 0.toByte() &&
                    bytes[index + 2] == 0.toByte() &&
                    bytes[index + 3] == 1.toByte() -> 4

                else -> null
            }
        }
    }

    private data class DisplayCaptureSize(
        val width: Int,
        val height: Int,
    ) {
        fun asArgument(): String = "${width}x${height}"

        companion object {
            private val PhysicalSizePattern = Regex("Physical size:\\s*(\\d+)x(\\d+)")

            fun resolve(maxSize: Int): DisplayCaptureSize {
                val physical = readPhysicalDisplaySize() ?: return DisplayCaptureSize(maxSize, maxSize).normalize()
                val largestDimension = max(physical.width, physical.height)
                if (maxSize <= 0 || maxSize >= largestDimension) {
                    return physical.normalize()
                }

                val scale = maxSize.toDouble() / largestDimension.toDouble()
                return DisplayCaptureSize(
                    width = (physical.width * scale).roundToInt(),
                    height = (physical.height * scale).roundToInt(),
                ).normalize()
            }

            private fun readPhysicalDisplaySize(): DisplayCaptureSize? {
                val process = ProcessBuilder(listOf("wm", "size"))
                    .redirectErrorStream(true)
                    .start()
                val output = process.inputStream.bufferedReader().use { it.readText() }
                val exitCode = process.waitFor()
                if (exitCode != 0) {
                    return null
                }

                val match = PhysicalSizePattern.find(output) ?: return null
                return DisplayCaptureSize(
                    width = match.groupValues[1].toInt(),
                    height = match.groupValues[2].toInt(),
                )
            }
        }

        private fun normalize(): DisplayCaptureSize {
            return copy(width = normalizeDimension(width), height = normalizeDimension(height))
        }

        private fun normalizeDimension(value: Int): Int {
            val evenValue = if (value % 2 == 0) value else value - 1
            return max(2, evenValue)
        }
    }

    private class ProcessStreamCollector(private val inputStream: InputStream) {
        private val buffer = ByteArrayOutputStream()
        private val collectorThread = thread(start = false, isDaemon = true, name = "screenrecord-stderr") {
            inputStream.copyTo(buffer)
            inputStream.close()
        }

        fun start() {
            collectorThread.start()
        }

        fun awaitText(): String {
            collectorThread.join()
            return buffer.toString(Charsets.UTF_8.name()).trim()
        }
    }

    private fun isCodecConfigNalUnit(nalUnit: ByteArray): Boolean {
        return when (readH264NalUnitType(nalUnit)) {
            7, 8 -> true
            else -> false
        }
    }

    private fun isKeyFrameNalUnit(nalUnit: ByteArray): Boolean = readH264NalUnitType(nalUnit) == 5

    private fun readH264NalUnitType(nalUnit: ByteArray): Int {
        val headerIndex = when {
            nalUnit.size >= 4 && nalUnit[0] == 0.toByte() && nalUnit[1] == 0.toByte() && nalUnit[2] == 1.toByte() -> 3
            nalUnit.size >= 5 && nalUnit[0] == 0.toByte() && nalUnit[1] == 0.toByte() && nalUnit[2] == 0.toByte() && nalUnit[3] == 1.toByte() -> 4
            else -> return -1
        }

        return nalUnit[headerIndex].toInt() and 0x1F
    }

    private data class Options(
        val socketName: String,
        val codec: String,
        val maxSize: Int,
        val maxFps: Int,
        val videoBitRate: String,
    ) {
        companion object {
            fun parse(args: Array<String>): Options {
                var socketName: String? = null
                var codec = "h264"
                var maxSize = 1080
                var maxFps = 60
                var videoBitRate = "8M"

                var index = 0
                while (index < args.size) {
                    when (args[index]) {
                        "--socket" -> socketName = requireValue(args, ++index, "--socket")
                        "--codec" -> codec = requireValue(args, ++index, "--codec")
                        "--max-size" -> {
                            val value = requireValue(args, ++index, "--max-size")
                            maxSize = value.toIntOrNull()
                                ?: throw IllegalArgumentException("Invalid value for --max-size: $value")
                        }

                        "--max-fps" -> {
                            val value = requireValue(args, ++index, "--max-fps")
                            maxFps = value.toIntOrNull()
                                ?: throw IllegalArgumentException("Invalid value for --max-fps: $value")
                        }

                        "--video-bit-rate" -> videoBitRate = requireValue(args, ++index, "--video-bit-rate")
                    }

                    index++
                }

                require(!socketName.isNullOrBlank()) { "Missing required --socket argument." }
                return Options(
                    socketName = socketName,
                    codec = codec,
                    maxSize = maxSize,
                    maxFps = maxFps,
                    videoBitRate = videoBitRate,
                )
            }

            private fun requireValue(args: Array<String>, index: Int, flag: String): String {
                return args.getOrNull(index)
                    ?: throw IllegalArgumentException("Missing value for $flag")
            }
        }
    }
}
