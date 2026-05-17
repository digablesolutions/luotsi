# Luotsi View Server Android

Luotsi is a host-driven device automation and live-view tool. This project
builds the Android-side helper that the host CLI installs and launches for
interactive view sessions.

In product terms, this module is the thin on-device capture and transport
component. Session orchestration, reconnect policy, artifacts, and operator UX
stay on the host. The Android helper's job is to expose the device display as
Luotsi's private stream over an Android `localabstract` socket.

The helper project uses Kotlin source and Kotlin DSL Gradle build scripts. It
is intentionally small. Today it wraps Android `screenrecord`, packetizes the
H.264 byte stream into Luotsi's private packet format, and emits stream header,
frame/config, and stream-end packets for the host decoder.

## Build

Run from this directory with the Gradle wrapper and Android SDK:

```powershell
.\gradlew.bat assembleDebug
```

The default output consumed by the CLI is:

```text
Luotsi.ViewServer.Android/app/build/outputs/apk/debug/app-debug.apk
```

If you build to a different path, set `DEVICE_E2E_VIEW_HELPER_JAR` to point to
the APK or jar containing `classes.dex` and the `dev.luotsi.view.Main`
entry point.