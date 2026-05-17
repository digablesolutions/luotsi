---
name: "Luotsi Android Kotlin"
description: "Use when editing Luotsi.ViewServer.Android Kotlin sources. Covers thin-helper boundaries, transport safety, service lifecycle, MediaProjection, Kotlin concurrency, and Android-specific code review rules."
applyTo: "Luotsi.ViewServer.Android/**/*.kt"
---

# Luotsi Android Kotlin

- Treat `Luotsi.ViewServer.Android` as a thin on-device helper. Keep orchestration, UX, reconnect policy, and artifact policy on the host.
- Preserve Luotsi's transport contract. Packet/header semantics, socket behavior, and H.264/Annex B expectations are compatibility-sensitive.
- Keep main-thread work trivial. Do not run socket loops, encoder draining, or blocking I/O on the main thread.
- Prefer explicit lifecycle ownership and deterministic cleanup for `MediaProjection`, `MediaCodec`, `VirtualDisplay`, `Surface`, sockets, and streams.
- Prefer small Kotlin classes and platform APIs over heavy architecture layers unless the helper's scope materially grows.
- If concurrency grows beyond a simple helper thread, prefer coroutines and structured concurrency over hidden long-lived scopes.
- Keep Android component exposure and permission surface minimal. Export only what Android requires.
- Add targeted tests or protocol checks when changing packetization, capture sizing, bitrate parsing, consent flow, or foreground-service behavior.
