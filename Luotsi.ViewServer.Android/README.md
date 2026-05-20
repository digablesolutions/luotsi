# Luotsi View Server Android

Luotsi is a host-driven device automation and live-view tool. This project
builds the Android-side helper that the host CLI installs and launches for
interactive `view` sessions.

The helper stays intentionally thin. Session orchestration, reconnect policy,
artifacts, and operator UX remain on the host. On-device code is responsible
for capturing the display, encoding H.264, and serving Luotsi's private packet
stream over an Android `localabstract` socket.

## Runtime Shape

The helper currently provides two capture paths:

- `screenrecord` fallback via `dev.luotsi.view.Main`
- MediaProjection capture via `ConsentActivity` + `CaptureService`

The host chooses the backend. With `--capture-backend auto`, Luotsi prefers
MediaProjection and falls back to `screenrecord` if helper startup or consent
fails before the stream is established.

### Screenrecord path

`Main.kt` is the app-process entry point used for the legacy `screenrecord`
path. It opens the requested `localabstract` socket, launches Android
`screenrecord --output-format=h264`, and packetizes the emitted Annex B NAL
units into Luotsi stream packets.

This path is intentionally simple and remains the explicit fallback backend. It
inherits Android `screenrecord` limits, including the platform 180-second
session cap.

### MediaProjection path

The MediaProjection path is split across three Kotlin classes:

- `ConsentActivity.kt` requests Android screen-capture consent and forwards the
  granted result to the service.
- `CaptureService.kt` runs as a foreground service, opens the socket, waits for
  the host stream client, and owns the active capture thread.
- `MediaProjectionCaptureSession.kt` acquires `MediaProjection`, creates a
  `VirtualDisplay`, configures an AVC `MediaCodec` encoder, and drains encoded
  buffers into Luotsi packets.

`MediaCodecPacketizer.kt` is shared packet-writing infrastructure for the
MediaProjection path. It writes the stream header plus codec-config, frame,
server-error, and stream-end packets expected by the host decoder.

Current constraint: the helper only supports H.264 (`codec=h264`) on both
capture paths.

## Android Components

`AndroidManifest.xml` declares the components and permissions required by the
current helper:

- `android.permission.FOREGROUND_SERVICE`
- `android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION`
- `android.permission.POST_NOTIFICATIONS`
- exported activity: `dev.luotsi.view/.ConsentActivity`
- non-exported foreground service: `dev.luotsi.view/.CaptureService`

The service is declared with `foregroundServiceType="mediaProjection"` because
the MediaProjection backend must run as an active foreground service while
capture is in progress.

## Build

Run from this directory with the Gradle wrapper and Android SDK installed:

```powershell
.\gradlew.bat assembleDebug
```

The default output consumed by the host CLI is:

```text
Luotsi.ViewServer.Android/app/build/outputs/apk/debug/app-debug.apk
```

The host resolves that default path automatically from the repository root. If
you build to a different location, set `LUOTSI_VIEW_HELPER_APK` to the built
APK path.

## Host Integration Contract

The host-side bootstrap resolves and installs the APK, then uses one of these
entry paths depending on the requested backend:

- `dev.luotsi.view.Main` for the `screenrecord` app-process path
- `dev.luotsi.view/.ConsentActivity` to start MediaProjection consent
- `dev.luotsi.view/.CaptureService` to own the foreground MediaProjection run

Both paths stream over a host-selected `localabstract` socket name. The helper
also consumes host-provided capture settings such as codec, max size, max FPS,
and target video bitrate.

## Project Defaults

- application ID / package: `dev.luotsi.view`
- `minSdk = 21`
- `targetSdk = 35`
- `compileSdk = 35`
- Java compatibility: 17

This project is intentionally small and purpose-built. If you change helper
behavior, keep the host-owned orchestration model intact and update the host
docs when the bootstrap contract changes.