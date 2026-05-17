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
import android.util.Log
import kotlin.concurrent.thread

class CaptureService : Service() {
    @Volatile
    private var activeSession: MediaProjectionCaptureSession? = null

    @Volatile
    private var serverSocket: LocalServerSocket? = null

    @Volatile
    private var captureThread: Thread? = null

    override fun onCreate() {
        super.onCreate()
        Log.i(TAG, "CaptureService onCreate sdk=${Build.VERSION.SDK_INT}")
        ensureNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        Log.i(TAG, "CaptureService onStartCommand action=${intent?.action} startId=$startId")
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
        stopActiveCapture()
        super.onDestroy()
    }

    private fun startCapture(intent: Intent) {
        stopActiveCapture()

        val resultData = intent.getParcelableExtra<Intent>(EXTRA_RESULT_DATA)
        val socketName = intent.getStringExtra(EXTRA_SOCKET_NAME)
        if (resultData == null || socketName.isNullOrBlank()) {
            Log.e(TAG, "Missing capture extras resultData=${resultData != null} socket=$socketName")
            stopSelf()
            return
        }

        val resultCode = intent.getIntExtra(EXTRA_RESULT_CODE, 0)
        val codec = intent.getStringExtra(EXTRA_CODEC) ?: "h264"
        val maxSize = intent.getIntExtra(EXTRA_MAX_SIZE, 1080)
        val maxFps = intent.getIntExtra(EXTRA_MAX_FPS, 60)
        val videoBitRate = intent.getStringExtra(EXTRA_VIDEO_BIT_RATE) ?: "8M"

        captureThread = thread(start = true, isDaemon = true, name = "luotsi-mediaprojection-capture") {
            var server: LocalServerSocket? = null
            try {
                Log.i(TAG, "Opening LocalServerSocket $socketName")
                server = LocalServerSocket(socketName)
                serverSocket = server
                Log.i(TAG, "Waiting for host stream client on $socketName")
                server.accept().use { client ->
                    Log.i(TAG, "Host stream client connected on $socketName")
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
            } catch (error: Throwable) {
                Log.e(TAG, "Capture thread failed", error)
            } finally {
                runCatching { server?.close() }
                Log.i(TAG, "Capture thread stopping")
                if (captureThread === Thread.currentThread()) {
                    activeSession = null
                    serverSocket = null
                    captureThread = null
                }
                stopSelf()
            }
        }
    }

    private fun stopActiveCapture() {
        activeSession?.stop()
        activeSession = null
        runCatching { serverSocket?.close() }
        serverSocket = null
        captureThread?.interrupt()
        captureThread = null
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
        private const val TAG = "LuotsiView"
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
