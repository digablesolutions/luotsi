# Android Agent Bundle

This repo now uses a two-part Android/Kotlin setup for agents:

- External global skills installed via `npx skills add ... -g -y`
- A repo-local skill in `.github/skills/luotsi-android-kotlin/` for Luotsi-specific Android helper conventions
- File instructions in `.github/instructions/` so Kotlin and Android config conventions auto-apply on helper source, manifest, and Gradle files
- Two repo-local companion skills for Compose UI review and MediaProjection/service lifecycle review

## Installed Skills

Run these commands to mirror the current setup:

```powershell
npx skills add wshobson/agents@mobile-android-design -g -y
npx skills add affaan-m/everything-claude-code@android-clean-architecture -g -y
npx skills add affaan-m/everything-claude-code@kotlin-coroutines-flows -g -y
npx skills add chrisbanes/skills@kotlin-coroutines-structured-concurrency -g -y
npx skills add thebushidocollective/han@android-jetpack-compose -g -y
```

## Bundle Rationale

### `mobile-android-design`

Use for Material 3, adaptive layouts, Navigation Compose, accessibility, and Android-specific UI patterns. This is the best broad Android UI/design skill in the current bundle.

### `android-clean-architecture`

Use for module boundaries, dependency inversion, repository/use-case separation, and Android/KMP-style data flow design. This is useful when the Android helper grows beyond a thin transport component.

### `kotlin-coroutines-flows`

Use for coroutine cancellation, `Flow`/`StateFlow`/`SharedFlow`, async orchestration, and coroutine testing. This is the general Kotlin async baseline for Android-side work.

### `kotlin-coroutines-structured-concurrency`

Use for review and implementation when scope ownership, lifecycle-tied work, and coroutine API shape matter. This is the highest-signal narrow skill in the bundle for avoiding hidden scope and cancellation bugs.

### `android-jetpack-compose`

Use when the Android side adds or expands real UI surfaces. It covers state primitives, state hoisting, recomposition, Material 3, Navigation Compose, and lazy layouts.

## Why This Bundle

- It matches current Android guidance around modern app architecture, state holders, unidirectional data flow, lifecycle-aware state collection, and minimal surface area.
- It matches current Kotlin guidance around null-safety, structured concurrency, and `Flow`-based state modeling.
- It separates generic Android/UI knowledge from Luotsi-specific transport, service, and MediaProjection conventions.

## Repo-Local Skill

The repo-local skill lives at `.github/skills/luotsi-android-kotlin/SKILL.md`.

Use it for anything under `Luotsi.ViewServer.Android/`, especially:

- MediaProjection and foreground-service changes
- `CaptureService`, `ConsentActivity`, `MediaProjectionCaptureSession`, and `Main.kt`
- Packetization and socket transport changes
- Manifest, permissions, notifications, and Gradle changes
- Kotlin concurrency and any future Compose additions

## Auto-Applied File Instructions

The Kotlin helper instruction lives at `.github/instructions/luotsi-viewserver-android-kotlin.instructions.md`.

It auto-applies to `Luotsi.ViewServer.Android/**/*.kt` and reinforces:

- thin-helper boundaries
- protocol/packet compatibility
- service and capture lifecycle ownership
- main-thread avoidance for capture and transport work
- minimal permission and exported-component surface

The Android config instruction lives at `.github/instructions/luotsi-viewserver-android-config.instructions.md`.

It auto-applies to `Luotsi.ViewServer.Android/**/AndroidManifest.xml` and `Luotsi.ViewServer.Android/**/*.gradle.kts` and reinforces:

- minimal permission and exported-component surface
- foreground-service and MediaProjection declaration correctness
- small, explicit Gradle Kotlin DSL configuration
- cautious dependency, plugin, SDK, and package/component name changes

## Companion Repo Skills

### `.github/skills/luotsi-compose-ui-review/`

Use when the Android side adds Compose UI or when reviewing state hoisting, lifecycle-aware collection, Material 3 usage, side effects, recomposition, and accessibility.

### `.github/skills/luotsi-mediaprojection-service-lifecycle/`

Use when reviewing `CaptureService`, `ConsentActivity`, `MediaProjectionCaptureSession`, foreground-service rules, consent/result handoff, socket ownership, and explicit cleanup ordering.

## Notes

- The bundle was selected against current official Android Developers and Kotlin guidance available in May 2026.
- The external skill installer reported low risk for four skills and medium risk for `android-clean-architecture`; review external skills before use.
- The Luotsi Android helper is intentionally small. Prefer thin, explicit implementations over heavy abstractions unless the module's scope materially changes.