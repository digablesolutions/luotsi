package dev.luotsi.view

import android.app.Activity
import android.content.Intent
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Bundle

class ConsentActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val projectionManager = getSystemService(MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        startActivityForResult(projectionManager.createScreenCaptureIntent(), REQUEST_MEDIA_PROJECTION)
    }

    @Deprecated("onActivityResult is enough for the minSdk used by this helper.")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)

        if (requestCode == REQUEST_MEDIA_PROJECTION && resultCode == RESULT_OK && data != null) {
            val serviceIntent = Intent(this, CaptureService::class.java)
                .setAction(CaptureService.ACTION_START)
                .putExtra(CaptureService.EXTRA_RESULT_CODE, resultCode)
                .putExtra(CaptureService.EXTRA_RESULT_DATA, data)
                .putExtra(CaptureService.EXTRA_SOCKET_NAME, intent.getStringExtra(CaptureService.EXTRA_SOCKET_NAME))
                .putExtra(CaptureService.EXTRA_CODEC, intent.getStringExtra(CaptureService.EXTRA_CODEC))
                .putExtra(CaptureService.EXTRA_MAX_SIZE, intent.getIntExtra(CaptureService.EXTRA_MAX_SIZE, 1080))
                .putExtra(CaptureService.EXTRA_MAX_FPS, intent.getIntExtra(CaptureService.EXTRA_MAX_FPS, 60))
                .putExtra(CaptureService.EXTRA_VIDEO_BIT_RATE, intent.getStringExtra(CaptureService.EXTRA_VIDEO_BIT_RATE))
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                startForegroundService(serviceIntent)
            } else {
                startService(serviceIntent)
            }
        }

        finish()
    }

    private companion object {
        private const val REQUEST_MEDIA_PROJECTION = 1001
    }
}
