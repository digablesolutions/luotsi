package dev.luotsi.view

internal object HelperDiagnosticJson {
    private const val SCHEMA = "luotsi-view-helper-diagnostic.v1"

    fun build(
        category: String = "helper",
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
    ): ByteArray {
        val fields = linkedMapOf<String, Any?>(
            "schema" to SCHEMA,
            "category" to category,
            "phase" to phase,
            "status" to status,
            "message" to message,
            "detail" to detail,
            "capture_backend" to captureBackend,
            "socket_name" to socketName,
            "codec" to codec,
            "width" to width,
            "height" to height,
            "max_fps" to maxFps,
            "video_bit_rate" to videoBitRate,
            "error" to error,
        )

        return buildString {
            append('{')
            var first = true
            fields.forEach { (name, value) ->
                if (value == null) {
                    return@forEach
                }

                if (!first) {
                    append(',')
                }
                first = false
                append('"')
                append(escape(name))
                append("\":")
                appendValue(value)
            }
            append('}')
        }.encodeToByteArray()
    }

    private fun StringBuilder.appendValue(value: Any) {
        when (value) {
            is Number, is Boolean -> append(value)
            else -> {
                append('"')
                append(escape(value.toString()))
                append('"')
            }
        }
    }

    private fun escape(value: String): String {
        val builder = StringBuilder(value.length + 8)
        value.forEach { char ->
            when (char) {
                '\\' -> builder.append("\\\\")
                '"' -> builder.append("\\\"")
                '\b' -> builder.append("\\b")
                '\u000C' -> builder.append("\\f")
                '\n' -> builder.append("\\n")
                '\r' -> builder.append("\\r")
                '\t' -> builder.append("\\t")
                else -> {
                    if (char.code < 0x20) {
                        builder.append("\\u")
                        builder.append(char.code.toString(16).padStart(4, '0'))
                    } else {
                        builder.append(char)
                    }
                }
            }
        }

        return builder.toString()
    }
}
