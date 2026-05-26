# Subsystems

This document replaces the old phased view-plan docs with the subsystem story
that matches the code currently in the repo.

## CLI and session subsystem

- `App` dispatches one-shot commands and the two long-lived session modes.
- One-shot commands return exactly one final JSON envelope.
- `inspect` is a JSONL session. `view` is an interactive session that prints human progress by default, can stream JSONL with `-o jsonl` or `--json`, and always writes JSONL session timelines to artifacts.

Relevant code:

- `Luotsi.Cli/Cli/App.cs`
- `Luotsi.Cli/Cli/CliOptions.cs`
- `Luotsi.Cli/Cli/InspectSession.cs`
- `Luotsi.Cli/View/ViewSession.cs`

## Host automation subsystem

- `IDeviceHost` is the host-side automation seam.
- The Android runtime stays `adb` first.
- Core device actions include tap, type, key events, screen-state capture,
  hierarchy capture, and telemetry/log collection.

Relevant code:

- `Luotsi.Cli/Infrastructure/`
- `Luotsi.Cli/Hosts/Android/DeviceRunner.cs`
- `Luotsi.Cli/Hosts/Android/AdbClient.cs`

## Scenario subsystem

- The repo ships generic scenario examples under `examples/scenarios/`.
- `ScenarioExecutor` resolves templates, validates steps, and routes them
  through host-side actions.
- Failure handling is artifact-heavy by design.

Relevant code:

- `Luotsi.Cli/Scenarios/ScenarioExecutor.cs`
- `Luotsi.Cli/Scenarios/ScenarioStepFailureException.cs`

## Telemetry subsystem

- The CLI understands `LUOTSI_DEVICE_TELEMETRY` logcat lines.
- Telemetry is reused both by dedicated commands and by semantic waits in
  scenarios.

Relevant code:

- `Luotsi.Cli/Telemetry/LuotsiDeviceTelemetryParser.cs`
- `Luotsi.Cli/Telemetry/Telemetry.cs`

## Artifact subsystem

- Each run/session gets its own artifact root.
- Runtime failures should leave behind enough context to diagnose device,
  screen, log, and view state.

Relevant code:

- `Luotsi.Cli/Artifacts/ArtifactSession.cs`
- `Luotsi.Cli/Artifacts/UiPollArtifactPolicy.cs`

## View subsystem

The built-in mirror is now a real subsystem, not just a design sketch.

### Transport and bootstrap

- `AndroidViewBootstrap` stages the helper package, configures the ADB tunnel,
  and starts the helper process or MediaProjection consent flow.
- `LocalhostViewStreamConnector` opens the forwarded localhost socket.
- `ViewPacketStreamReader` parses the private packet stream.
- `auto` capture prefers MediaProjection and retries with `screenrecord` if
  startup or consent fails before the stream header is established.
- Optional TCP share relay (`--share-bind`) mirrors the packet stream to
  observers and replays bootstrap packets (config + latest keyframe) for
  late joins.

Relevant code:

- `Luotsi.Cli/Hosts/Android/View/AndroidViewBootstrap.cs`
- `Luotsi.Cli/Hosts/Android/View/AndroidViewServerInstaller.cs`
- `Luotsi.Cli/Hosts/Android/View/AndroidViewStreamClient.cs`
- `Luotsi.Cli/View/ViewSession.cs`

### Decode and presentation

- `LibavViewBackend` decodes compressed H.264 packets into BGRA frames.
- `NativeWindowViewRenderer` owns renderer/session glue and pointer routing.
- `Sdl3ViewWindowSurface` owns the actual local native window and texture
  presentation path.

Relevant code:

- `Luotsi.Cli/View/Backends/Ffmpeg/LibavViewBackend.cs`
- `Luotsi.Cli/View/NativeWindowViewRenderer.cs`
- `Luotsi.Cli/View/Sdl3ViewWindowSurface.cs`

### Input path

- Pointer events are intentionally mapped back into existing `IDeviceHost`
  semantics instead of inventing a separate low-level control protocol.
- That keeps mirror input aligned with scenario input behavior.

### Current constraints

- The built-in live path currently assumes H.264 over the private packet stream.
- MediaProjection currently requires H.264 and interactive Android consent;
  `screenrecord` remains the explicit fallback path.
- `screenrecord` capture has platform limits, so long runs may reconnect
  proactively before the backend limit window is reached.
- The primary validated host path is Windows.
- macOS and Linux are supported by the chosen SDL3/libav architecture, but they
  still need live validation passes on actual host machines.

### Operational diagnostics

- `view-doctor` uses the same option resolution path as `view`.
- Current checks cover decoder readiness, helper package discovery,
  capture-backend policy, adb device visibility, device preflight,
  MediaProjection readiness when requested, and optional recording output
  readiness.
- Startup and doctor flows emit explicit startup-phase/diagnostic events
  (`view_startup_phase`, `view_diagnostic`) so agents can track readiness
  progress from the artifact timeline or JSONL stdout mode.
- Stats cadence is split intentionally: `--stats-interval-ms` controls
  `view_stats` timeline/JSONL events, while `--renderer-stats-interval-ms`
  controls renderer/title update cadence.

## Suggested next docs to keep current

- keep `docs/architecture.md` as the top-level runtime map
- keep this file focused on subsystem boundaries and owning code
- add targeted operational notes only when they reflect validated behavior
