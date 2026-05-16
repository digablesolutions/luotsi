# VisitLab View Server Android

This project builds the device-side helper used by `VisitLab.Cli view`.

The helper project uses Kotlin source and Kotlin DSL Gradle build scripts.

It is intentionally small and currently provides only the Phase 2 transport
stub: it binds an Android localabstract socket, writes the private stream header,
emits a `stream_end` packet, and exits.

## Build

Run from this directory with the Gradle wrapper and Android SDK:

```powershell
.\gradlew.bat assembleDebug
```

The default output consumed by the CLI is:

```text
VisitLab.ViewServer.Android/app/build/outputs/apk/debug/app-debug.apk
```

If you build to a different path, set `DEVICE_E2E_VIEW_HELPER_JAR` to point to
the APK or jar containing `classes.dex` and the `fi.systam.visitlab.view.Main`
entry point.