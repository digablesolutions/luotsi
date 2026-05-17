---
name: luotsi-android-kotlin
description: 'Android/Kotlin guidance for Luotsi.ViewServer.Android. Use when editing CaptureService, ConsentActivity, MediaProjectionCaptureSession, Main.kt, MediaCodecPacketizer, AndroidManifest.xml, app/build.gradle.kts, foreground services, MediaProjection, socket transport, packetization, coroutines, Flow, permissions, or any future Compose UI in the Android helper. Encodes current Android Developers and Kotlin best practices plus Luotsi-specific conventions.'
user-invocable: true
---

# Luotsi Android/Kotlin

## When to Use

- Editing files under `Luotsi.ViewServer.Android/`
- Changing MediaProjection capture, foreground service behavior, or consent flow
- Modifying socket transport, packetization, H.264 handling, or protocol-sensitive Android code
- Reviewing Kotlin concurrency, cancellation, or lifecycle/resource ownership
- Adding Android UI, permissions, notifications, or tests on the helper side

## Project Shape

- Treat the Android module as a thin on-device helper, not a full product app.
- The host CLI owns orchestration, reconnect behavior, artifact policy, operator UX, and higher-level session management.
- The Android helper owns only capture, encode/packetize, consent handoff, and local socket streaming.
- Keep dependencies minimal. Prefer small plain Kotlin classes and platform APIs over large architectural frameworks unless complexity clearly justifies them.

## Non-Negotiables

- Preserve Luotsi's private transport contract: stream header, packet types, H.264/Annex B expectations, and socket behavior.
- Keep Android component exposure minimal. Only export components when Android requires it.
- MediaProjection capture must run in a foreground service with a persistent notification.
- Release `MediaCodec`, `VirtualDisplay`, `Surface`, `MediaProjection`, sockets, and streams deterministically on stop and failure paths.
- Do not move host concerns into the helper. If logic is about CLI UX, reconnect policy, artifact capture policy, or cross-device orchestration, it probably belongs on the host.

## Current Android/Kotlin Standards

- Prefer Kotlin-first, AndroidX-first implementations.
- Keep main-thread work trivial. Do not run socket, encoder, or blocking I/O loops on the main thread.
- If asynchronous logic grows, prefer coroutines plus structured concurrency over raw threads.
- Scope ownership belongs to the caller or lifecycle owner. Avoid hidden long-lived scopes.
- Model state explicitly. If UI is added, prefer state holders, unidirectional data flow, hoisted state, and lifecycle-aware collection.
- Use WorkManager only for deferrable background work. Do not replace active capture or streaming with WorkManager.
- Prefer constructor-injected collaborators and small seams over premature DI frameworks.
- Add focused tests for protocol-sensitive logic, size normalization, bitrate parsing, packetization, and service/consent edge cases.

## Repo-Specific Guidance

- `Main.kt`: app-process and `screenrecord` entry point. Keep startup fast and protocol-compatible.
- `CaptureService.kt`: own service lifecycle, notification setup, socket accept loop, and clean shutdown.
- `ConsentActivity.kt`: keep it as a consent trampoline, not a feature surface.
- `MediaProjectionCaptureSession.kt`: preserve explicit resource ownership and release ordering.
- `MediaCodecPacketizer.kt`: treat as protocol-critical code. Any payload or header change needs host-side coordination and tests.
- `AndroidManifest.xml`: justify every new permission, service attribute, and exported component.
- `app/build.gradle.kts`: keep Gradle Kotlin DSL, current SDK levels, and dependency/plugin count small.

## Review Checklist

1. Does this logic truly belong on Android, or should it stay on the host?
2. Does the change preserve transport and packet compatibility?
3. Are lifecycle ownership and shutdown paths explicit?
4. Is main-thread work kept minimal?
5. If concurrency changed, who owns cancellation and error propagation?
6. If UI was added, is state hoisted and lifecycle-aware?
7. If a new dependency was added, does the helper actually need it?
8. Were targeted tests or protocol checks added for behavior-sensitive changes?

## If UI Grows Beyond Consent

- Prefer Jetpack Compose and Material 3 for real UI surfaces.
- Use `ViewModel` only when screen/state complexity warrants it.
- Collect streams with lifecycle-aware APIs such as `collectAsStateWithLifecycle`.
- Hoist state and keep composables stateless where practical.
- Be deliberate with side effects such as `LaunchedEffect`, `DisposableEffect`, and `rememberUpdatedState`.
- Optimize recomposition only when measurement shows a real problem.

## Anti-Patterns

- Forcing a full clean-architecture stack into this thin helper without clear need
- Hiding concurrency behind global scopes or unmanaged background work
- Blocking the main thread with socket, codec, or file operations
- Expanding permission or exported-component surface without a direct requirement
- Changing packet or header semantics without host-side coordination
- Adding Compose, DI, Room, or WorkManager because they are fashionable rather than necessary
