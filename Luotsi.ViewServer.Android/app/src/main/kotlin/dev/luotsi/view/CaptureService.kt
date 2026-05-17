package dev.luotsi.view

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.LocalServerSocket
import android.os.Build
import android.os.IBinder
import kotlin.concurrent.thread

class CaptureService : Service() {
    @Volatile
    private var activeSession: MediaProjectionCaptureSession? = null

    @Volatile
    private var serverSocket: LocalServerSocket? = null

    override fun onCreate() {
        super.onCreate()
        ensureNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_START -> {
                startAsForegroundService()
                startCapture(intent)
            }

            ACTION_STOP -> stopSelf()
        }

        return START_NOT_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onDestroy() {
        activeSession?.stop()
        activeSession = null
        runCatching { serverSocket?.close() }
        serverSocket = null
        super.onDestroy()
    }

    private fun startCapture(intent: Intent) {
        val resultData = intent.getParcelableExtra<Intent>(EXTRA_RESULT_DATA)
        val socketName = intent.getStringExtra(EXTRA_SOCKET_NAME)
        if (resultData == null || socketName.isNullOrBlank()) {
            stopSelf()
            return
        }

        val resultCode = intent.getIntExtra(EXTRA_RESULT_CODE, 0)
        val codec = intent.getStringExtra(EXTRA_CODEC) ?: "h264"
        val maxSize = intent.getIntExtra(EXTRA_MAX_SIZE, 1080)
        val maxFps = intent.getIntExtra(EXTRA_MAX_FPS, 60)
        val videoBitRate = intent.getStringExtra(EXTRA_VIDEO_BIT_RATE) ?: "8M"

        thread(start = true, isDaemon = true, name = "luotsi-mediaprojection-capture") {
            try {
                LocalServerSocket(socketName).use { server ->
                    serverSocket = server
                    server.accept().use { client ->
                        val session = MediaProjectionCaptureSession(
                            this,
                            MediaProjectionCaptureSession.Options(
                                resultCode = resultCode,
                                resultData = resultData,
                                socketName = socketName,
                                codec = codec,
                                maxSize = maxSize,
                                maxFps = maxFps,
                                videoBitRate = videoBitRate,
                            ),
                            client.outputStream,
                        )
                        activeSession = session
                        session.run()
                    }
                }
            } finally {
                activeSession = null
                serverSocket = null
                stopSelf()
            }
        }
    }

    private fun ensureNotificationChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            return
        }

        val manager = getSystemService(NotificationManager::class.java)
        val channel = NotificationChannel(
            NOTIFICATION_CHANNEL_ID,
            "Luotsi view capture",
            NotificationManager.IMPORTANCE_LOW,
        )
        manager.createNotificationChannel(channel)
    }

    private fun startAsForegroundService() {
        val notification = buildNotification()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    private fun buildNotification(): Notification {
        val builder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Notification.Builder(this, NOTIFICATION_CHANNEL_ID)
        } else {
            @Suppress("DEPRECATION")
            Notification.Builder(this)
        }

        return builder
            .setContentTitle("Luotsi view capture")
            .setContentText("Screen capture is running.")
            .setSmallIcon(android.R.drawable.presence_video_online)
            .setOngoing(true)
            .build()
    }

    companion object {
        const val ACTION_START = "dev.luotsi.view.action.START_CAPTURE"
        const val ACTION_STOP = "dev.luotsi.view.action.STOP_CAPTURE"
        const val EXTRA_RESULT_CODE = "dev.luotsi.view.extra.RESULT_CODE"
        const val EXTRA_RESULT_DATA = "dev.luotsi.view.extra.RESULT_DATA"
        const val EXTRA_SOCKET_NAME = "socket"
        const val EXTRA_CODEC = "codec"
        const val EXTRA_MAX_SIZE = "max_size"
        const val EXTRA_MAX_FPS = "max_fps"
        const val EXTRA_VIDEO_BIT_RATE = "video_bit_rate"
        const val NOTIFICATION_CHANNEL_ID = "luotsi_view_capture"
        private const val NOTIFICATION_ID = 1001
    }
}
