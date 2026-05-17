package dev.luotsi.view

import android.media.MediaCodec
import android.os.SystemClock
import java.io.Closeable
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

internal class MediaCodecPacketizer(private val output: OutputStream) : Closeable {
    private var sequence = 1L
    private var wroteConfig = false

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

    fun writeCodecConfig(payload: ByteArray) {
        if (payload.isEmpty()) {
            return
        }

        wroteConfig = true
        writePacket(TYPE_CONFIG, false, 0L, ensureAnnexB(payload))
    }

    fun writeEncodedBuffer(codec: MediaCodec, index: Int, info: MediaCodec.BufferInfo) {
        if (info.size <= 0) {
            return
        }

        val buffer = codec.getOutputBuffer(index) ?: return
        val payload = ByteArray(info.size)
        val duplicate = buffer.duplicate()
        duplicate.position(info.offset)
        duplicate.limit(info.offset + info.size)
        duplicate.get(payload)

        if ((info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) {
            writeCodecConfig(payload)
            return
        }

        val keyFrame = (info.flags and MediaCodec.BUFFER_FLAG_KEY_FRAME) != 0
        writePacket(TYPE_FRAME, keyFrame, info.presentationTimeUs, ensureAnnexB(payload))
    }

    fun writeServerError(message: String) = writePacket(TYPE_SERVER_ERROR, false, 0L, message.encodeToByteArray())

    fun writeStreamEnd() = writePacket(TYPE_STREAM_END, false, 0L, byteArrayOf())

    fun flush() = output.flush()

    override fun close() = output.close()

    private fun writePacket(packetType: Int, keyFrame: Boolean, presentationTimestampUs: Long, payload: ByteArray) {
        val ptsUs = if (presentationTimestampUs > 0) {
            presentationTimestampUs
        } else {
            SystemClock.elapsedRealtimeNanos() / 1_000L
        }
        val buffer = ByteBuffer.allocate(PACKET_HEADER_SIZE + payload.size)
            .order(ByteOrder.LITTLE_ENDIAN)
            .apply {
                put(packetType.toByte())
                put(if (keyFrame) 1.toByte() else 0.toByte())
                putShort(0.toShort())
                putLong(sequence++)
                putLong(ptsUs)
                putInt(payload.size)
                if (payload.isNotEmpty()) {
                    put(payload)
                }
            }

        output.write(buffer.array())
        output.flush()
    }

    private fun ensureAnnexB(payload: ByteArray): ByteArray {
        if (hasStartCode(payload)) {
            return payload
        }

        val converted = ByteArray(START_CODE.size + payload.size)
        START_CODE.copyInto(converted)
        payload.copyInto(converted, destinationOffset = START_CODE.size)
        return converted
    }

    private fun hasStartCode(payload: ByteArray): Boolean {
        return payload.size >= 4 &&
            payload[0] == 0.toByte() &&
            payload[1] == 0.toByte() &&
            (payload[2] == 1.toByte() || (payload[2] == 0.toByte() && payload[3] == 1.toByte()))
    }

    companion object {
        private const val MAGIC = 0x42414C56
        private const val PROTOCOL_VERSION = 1
        private const val CODEC_H264 = 1
        private const val TYPE_CONFIG = 1
        private const val TYPE_FRAME = 2
        private const val TYPE_STREAM_END = 4
        private const val TYPE_SERVER_ERROR = 5
        private const val STREAM_HEADER_SIZE = 16
        private const val PACKET_HEADER_SIZE = 24
        private val START_CODE = byteArrayOf(0, 0, 0, 1)
    }
}
