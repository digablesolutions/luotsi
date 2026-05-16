# Subsystems

This document replaces the old phased view-plan docs with the subsystem story
that matches the code currently in the repo.

## CLI and session subsystem

- `App` dispatches one-shot commands and the two long-lived session modes.
- One-shot commands return exactly one final JSON envelope.
- `inspect` and `view` are JSONL sessions with startup and shutdown events.

Relevant code:

- `VisitLab.Cli/Cli/App.cs`
- `VisitLab.Cli/Cli/CliOptions.cs`
- `VisitLab.Cli/Cli/InspectSession.cs`
- `VisitLab.Cli/View/ViewSession.cs`

## Host automation subsystem

- `IDeviceHost` is the host-side automation seam.
- The Android runtime stays `adb` first.
- Core device actions include tap, type, key events, screen-state capture,
  hierarchy capture, and telemetry/log collection.

Relevant code:

- `VisitLab.Cli/Infrastructure/`
- `VisitLab.Cli/Hosts/Android/DeviceRunner.cs`
- `VisitLab.Cli/Hosts/Android/AdbClient.cs`

## Scenario subsystem

- Scenario files live under `scenarios/`.
- `ScenarioExecutor` resolves templates, validates steps, and routes them
  through host-side actions.
- Failure handling is artifact-heavy by design.

Relevant code:

- `VisitLab.Cli/Scenarios/ScenarioExecutor.cs`
- `VisitLab.Cli/Scenarios/ScenarioStepFailureException.cs`

## Telemetry subsystem

- The CLI understands `DEVICE_TEST_TELEMETRY` logcat lines.
- Telemetry is reused both by dedicated commands and by semantic waits in
  scenarios.

Relevant code:

- `VisitLab.Cli/Telemetry/DeviceTestTelemetryParser.cs`
- `VisitLab.Cli/Telemetry/Telemetry.cs`

## Artifact subsystem

- Each run/session gets its own artifact root.
- Runtime failures should leave behind enough context to diagnose device,
  screen, log, and view state.

Relevant code:

- `VisitLab.Cli/Artifacts/ArtifactSession.cs`
- `VisitLab.Cli/Artifacts/UiPollArtifactPolicy.cs`

## View subsystem

The built-in mirror is now a real subsystem, not just a design sketch.

### Transport and bootstrap

- `AndroidViewBootstrap` stages the helper package, configures the ADB tunnel,
  and starts the helper process.
- `LocalhostViewStreamConnector` opens the forwarded localhost socket.
- `ViewPacketStreamReader` parses the private packet stream.

Relevant code:

- `VisitLab.Cli/Hosts/Android/View/AndroidViewBootstrap.cs`
- `VisitLab.Cli/Hosts/Android/View/AndroidViewServerInstaller.cs`
- `VisitLab.Cli/Hosts/Android/View/AndroidViewStreamClient.cs`
- `VisitLab.Cli/View/ViewSession.cs`

### Decode and presentation

- `LibavViewBackend` decodes compressed H.264 packets into BGRA frames.
- `NativeWindowViewRenderer` owns renderer/session glue and pointer routing.
- `Sdl3ViewWindowSurface` owns the actual local native window and texture
  presentation path.

Relevant code:

- `VisitLab.Cli/View/Backends/Ffmpeg/LibavViewBackend.cs`
- `VisitLab.Cli/View/NativeWindowViewRenderer.cs`
- `VisitLab.Cli/View/Sdl3ViewWindowSurface.cs`

### Input path

- Pointer events are intentionally mapped back into existing `IDeviceHost`
  semantics instead of inventing a separate low-level control protocol.
- That keeps mirror input aligned with scenario input behavior.

### Current constraints

- The built-in live path currently assumes H.264 over the private packet stream.
- The primary validated host path is Windows.
- macOS and Linux are supported by the chosen SDL3/libav architecture, but they
  still need live validation passes on actual host machines.

## Suggested next docs to keep current

- keep `docs/architecture.md` as the top-level runtime map
- keep this file focused on subsystem boundaries and owning code
- add targeted operational notes only when they reflect validated behavior