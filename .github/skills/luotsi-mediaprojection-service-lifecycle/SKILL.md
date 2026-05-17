---
name: luotsi-mediaprojection-service-lifecycle
description: 'MediaProjection and Android service lifecycle review guidance for Luotsi.ViewServer.Android. Use when editing CaptureService, ConsentActivity, MediaProjectionCaptureSession, notifications, foreground services, permissions, socket accept loops, cleanup ordering, MediaCodec, VirtualDisplay, Surface, or consent/result handoff.'
user-invocable: true
---

# Luotsi MediaProjection Service Lifecycle

## When to Use

- Editing `CaptureService.kt`, `ConsentActivity.kt`, or `MediaProjectionCaptureSession.kt`
- Reviewing foreground-service behavior, notification requirements, or permission flow
- Changing capture start/stop, resource cleanup, or consent/result handoff
- Debugging MediaProjection, `MediaCodec`, `VirtualDisplay`, `Surface`, or local socket lifecycle issues

## Standards

- MediaProjection capture must run in a foreground service with the correct service type and a persistent notification.
- Consent flow should stay minimal. `ConsentActivity` should collect permission and hand off work to the service, not accumulate feature logic.
- Capture startup and shutdown must be explicit. Every started resource needs a clearly owned release path.
- Cleanup ordering matters: stop or signal encoder input, release virtual display, release surface, stop/release codec, stop projection, close sockets/streams.
- Failure paths must leave the process in a recoverable state. Avoid partially initialized capture sessions.
- Avoid hidden background work. If a thread or coroutine is launched, its owner, cancellation, and terminal behavior should be obvious at the call site.
- Keep notification, permission, and component exposure aligned with current Android SDK requirements.

## Review Questions

1. Who owns capture start, capture stop, and abnormal termination?
2. Can any resource leak if consent is denied, a socket fails, or encoder startup throws?
3. Is the foreground-service contract satisfied for the target SDK?
4. Is any blocking or long-running work happening on the main thread?
5. Does the service stop itself cleanly after terminal conditions?
6. Are socket and stream shutdown paths deterministic even on exceptions?

## Anti-Patterns

- MediaProjection logic spread across activity and service without clear ownership
- Cleanup relying on GC or process death rather than explicit release
- Partial shutdown that leaves `MediaCodec`, `VirtualDisplay`, or sockets dangling
- Long-running capture work on the main thread
- Service logic growing into host-orchestrator logic

## Luotsi-Specific Notes

- The helper exists to capture and stream device output over Luotsi's private socket protocol. Keep it transport-focused.
- Any change to packetization, socket framing, or stream-end/error behavior needs coordination with the host-side decoder and tests.
- Prefer narrow, testable helpers around bitrate parsing, display sizing, packet writing, and lifecycle edges.
