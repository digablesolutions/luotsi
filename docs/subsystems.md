# Subsystems

Use this map when a behavior crosses public commands, artifacts, and host-side
device work. `docs/architecture.md` is the top-level runtime story; this page is
the ownership map for the main code areas.

## Command Runtime

- `App`, `CliOptions`, `AppExecutionShell`, and the command routers own CLI
  parsing, dispatch, output envelopes, failure responses, and help.
- One-shot commands return one final envelope unless the caller chooses human or
  quiet console output.
- `inspect` streams JSONL for agents, while `view` is an operator session that
  can also stream JSONL.

Relevant code:

- `Luotsi.Cli/Cli/App.cs`
- `Luotsi.Cli/Cli/CliOptions.cs`
- `Luotsi.Cli/Cli/Composition/`
- `Luotsi.Cli/Cli/Routing/`
- `Luotsi.Cli/Cli/Envelope/`

## Lab And Device Selection

- Lab commands own inventory, leases, queued claims, quarantines, health
  summaries, and device-query selection.
- Scenario runs use the same allocation model through `run --claim-device`.
- Shared-lab flows should prefer claimed run commands when a concrete serial is
  available.

Relevant code:

- `Luotsi.Cli/Cli/Routing/Lab*`
- `Luotsi.Cli/Cli/Routing/DeviceSelectorResolver.cs`
- `Luotsi.Cli/Scenarios/ScenarioDeviceAllocator.cs`
- `Luotsi.Cli/Infrastructure/Devices/`

## Host Automation

- `IDeviceHost` is the host-side automation seam.
- The Android implementation stays adb-first and translates typed host actions
  into bounded device work.
- Safe read-style probes can use retry policies; mutating actions are not
  blindly replayed.

Relevant code:

- `Luotsi.Cli/Infrastructure/Contracts/`
- `Luotsi.Cli/Infrastructure/Devices/`
- `Luotsi.Cli/Hosts/Android/`
- `Luotsi.Cli/Telemetry/`

## Scenario Lifecycle

- Scenario files are explicit JSON playbooks with validation before execution.
- `ScenarioRunOrchestrator`, `ScenarioExecutor`, and
  `ScenarioActionDispatcher` run playbooks through `IDeviceHost`.
- Reports preserve governance, device-health, CI-policy, artifacts, and replay
  handoffs.

Relevant code:

- `Luotsi.Cli/Scenarios/ScenarioCatalog.cs`
- `Luotsi.Cli/Scenarios/ScenarioValidator.cs`
- `Luotsi.Cli/Scenarios/ScenarioValidationExecutor.cs`
- `Luotsi.Cli/Scenarios/ScenarioRunOrchestrator.cs`
- `Luotsi.Cli/Scenarios/ScenarioExecutor.cs`
- `Luotsi.Cli/Scenarios/ScenarioRunReport*`

## Authoring And Exploration

- `inspect` records structured command/action sessions for agents.
- `discover` explores UI state conservatively and emits review-required starter
  scenario evidence.
- `journey-intake` validates external Journey-style intent before drafting a
  Luotsi scenario skeleton.
- `replay scenario-draft` turns persisted action events into a conservative
  scenario draft, optionally validates it, and surfaces next actions.

Relevant code:

- `Luotsi.Cli/Cli/Inspect/`
- `Luotsi.Cli/Cli/Discovery/`
- `Luotsi.Cli/Cli/JourneyIntake/`
- `Luotsi.Cli/Cli/Replay/ReplayScenarioDraftService.cs`

## Artifacts, Packages, And Replay

- Artifact roots are the durable boundary between live device work and later
  analysis.
- Replay commands reopen packet summaries, capsules, timelines, scrub views,
  graphs, clusters, searches, and scenario drafts.
- Artifact packages can be packed, verified, unpacked, and ingested; lab-safe
  redaction affects zip text entries only and never mutates the source root.

Relevant code:

- `Luotsi.Cli/Artifacts/`
- `Luotsi.Cli/Cli/Replay/`
- `docs/schemas/`

## Live View

- `AndroidViewBootstrap` stages or locates the helper, configures the adb
  tunnel, and starts MediaProjection or `screenrecord`.
- `LocalhostViewStreamConnector` and `ViewPacketStreamReader` receive the
  private packet stream.
- `LibavViewBackend` decodes H.264 into BGRA frames.
- `NativeWindowViewRenderer` and `Sdl3ViewWindowSurface` own local
  presentation.
- Optional sharing is read-only for observers; screenshots and recordings write
  to artifacts.

Relevant code:

- `Luotsi.Cli/Cli/View/`
- `Luotsi.Cli/View/`
- `Luotsi.Cli/Hosts/Android/View/`
- `docs/view-session.md`

## Native Dependencies And Distribution

- SDL3 and FFmpeg/libav are probed from environment, repo-local, bundled, and
  app-base locations.
- `view setup`, `view-doctor`, and `doctor --fix` are the diagnostic and repair
  entry points for view prerequisites.
- `version` and `update` expose install metadata and explicit installer-managed
  updates.

Relevant code:

- `Luotsi.Cli/Cli/View/`
- `Luotsi.Cli/Cli/Update/`
- `ffmpeg/download-ffmpeg.ps1`
- `docs/distribution-playbook.md`
