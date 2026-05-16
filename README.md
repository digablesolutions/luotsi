# VisitLab

Local-only experiment for a cross-platform on-device end-to-end harness.

The current kiosk harness proved the useful shape:

- host-side commands that drive a real Android device through `adb`
- stdout as exactly one JSON envelope for agents
- artifacts by default
- scenarios as small readable playbooks
- app-side semantic telemetry as the high-value oracle when available

This repo explores whether the next version should be a typed .NET CLI rather
than PowerShell. It is intentionally separate from the kiosk repo while the
shape is still experimental.

## Why look at this approach?

This approach has a useful architecture lesson even if we do not copy its protocol:


V1 of this lab does **not** vendor any specific tool or implement its server protocol. It
keeps boring ADB primitives first, with room to add an optional binary
adapter later for low-latency mirroring, recording, or HID/OTG control.

## Code layout

- `VisitLab.Cli/Cli/` contains the entrypoint, command dispatch, help text, and inspect-mode session loop.
- `VisitLab.Cli/Hosts/Android/` contains the Android transport and device interaction runtime.
- `VisitLab.Cli/Artifacts/` contains artifact session management.
- `VisitLab.Cli/Models/` contains shared records, envelopes, and screen/scenario data models.
- `VisitLab.Cli/Scenarios/` contains scenario execution flow and scenario-specific failure plumbing.
- `VisitLab.Cli/Telemetry/` contains telemetry parsing contracts and the kiosk telemetry parser.
- `VisitLab.Cli/Errors/` contains typed command and wait exceptions.
- `VisitLab.Cli/Infrastructure/` contains interfaces plus the default system-backed implementations used by the CLI.

## Current commands

Run from WSL or PowerShell:

```bash
cd <repo-root>
dotnet run --project VisitLab.Cli -- devices
dotnet run --project VisitLab.Cli -- preflight --device <serial> --package fi.systam.visit
dotnet run --project VisitLab.Cli -- screen-state --device <serial>
dotnet run --project VisitLab.Cli -- view --device <serial> --decoder ffmpeg --record capture.mp4
dotnet run --project VisitLab.Cli -- telemetry-tail --device <serial> --tail 200
dotnet run --project VisitLab.Cli -- telemetry-watch --device <serial> --timeout-sec 10
dotnet run --project VisitLab.Cli -- tap-text --device <serial> --text "Sign in"
dotnet run --project VisitLab.Cli -- wait-log --device <serial> --contains "DEVICE_READY" --timeout-sec 20
dotnet run --project VisitLab.Cli -- run --device <serial> --file scenarios/idle-language-switch-finnish.json
```

Inspect mode is intentionally different: it is a long-lived JSONL session over
stdin/stdout rather than a single JSON envelope. Example:

```powershell
dotnet run --project VisitLab.Cli -- inspect --device 192.168.0.134:5555
```

Then send one JSON command per line:

```json
{"id":"1","command":"refresh"}
{"id":"2","command":"tap_text","text":"Sign in","timeout_sec":10}
{"id":"3","command":"telemetry_tail","tail":200}
{"id":"4","command":"exit"}
```

If WSL cannot see `adb`, pass a path with `--adb` or expose Android platform
tools on WSL's `PATH`.

The `view` command is also a long-lived JSONL session. Alongside `view_started`,
`view_error`, and `view_ended`, it can emit throttled `view_stats` events so
agents can consume rolling decode/present FPS and latency without scraping the
SDL window title or flooding stdout on long-lived sessions.

The implementation currently supports `--platform android`. The host seam is in
place so an iOS adapter can be added later without rewriting the command layer.

Every command prints a single JSON envelope:

```json
{
  "schema": "visit-lab-command.v1",
  "ok": true,
  "command": "screen-state",
  "data": {},
  "artifacts": {
    "artifact_root": "/tmp/visit-lab/..."
  },
  "error": null
}
```

Scenario `run` commands return the scenario result inside `data`. That payload now
includes top-level timing for non-step overhead:

```json
{
  "scenario": "idle-visitor-sign-in-happy-path",
  "status": "passed",
  "timing": {
    "total_ms": 86361.4686,
    "prologue_ms": 655.9714,
    "steps_ms": 85701.7421,
    "non_step_ms": 659.7265
  },
  "steps": []
}
```

Runtime commands now also write richer artifacts when they interact with a device:

- `device-fingerprint.json` for `preflight` and scenario runs
- `wait-log.txt` / `wait-log.json` for log streaming waits
- `telemetry-tail.txt` / `telemetry-tail.json` for semantic telemetry snapshots
- `telemetry-watch.txt` / `telemetry-watch.json` for bounded telemetry collection
- automatic failure bundles with screenshot, logcat, screen-state, hierarchy, and metadata when a runtime command fails after reaching the device

Inspect sessions stream JSONL events instead of writing a final command envelope.
They begin with `session_started` and `screen_snapshot`, then emit
`command_result`, `screen_delta`, and `session_ended` events as the agent drives
the device.

## Scenario playbook

The first playbook format is JSON to keep parsing unambiguous across OSes and
agents. The ported kiosk scenarios now live under `scenarios/`:

```json
{
  "name": "idle-language-switch-finnish",
  "steps": [
    { "name": "open language menu", "action": "tapText", "text": "English", "timeoutSec": 10 },
    { "name": "choose Finnish", "action": "tapText", "text": "Suomi", "timeoutSec": 10 },
    { "name": "assert Finnish sign-in", "action": "waitVisible", "text": "Kirjaudu sisään", "timeoutSec": 15 }
  ]
}
```

Scenario strings also support lightweight templating:

- `${env:NAME}` for required environment variables
- `${env:NAME|fallback}` for optional environment variables with a fallback
- `${var:name}` for scenario variables from the root `variables` block
- `${now:HHmmss}` for timestamp fragments used in live test data

Scenario step results also include per-step timing. For actions that include
harness-authored waits such as `tapPoint`, the step timing reports
`harness_delay_ms` and `configured_delay_ms` alongside total duration.

Supported actions:

- `waitVisible`
- `waitNotVisible`
- `tapText`
- `tapPoint`
- `doubleTapHeaderLogo`
- `typeText`
- `typePin`
- `keyevent`
- `waitLog`
- `waitStep`
- `waitActionReady`
- `resetLog`
- `assertEvent`
- `assertTextInputReady`
- `screenState`
- `assertBelow`
- `assertAligned`
- `assertAppVersion`
- `takeScreenshot`
- `captureArtifacts`
- `sleep`

`assertEvent` also supports `observeFromPreviousStep: true` when the log
observation window should begin at the previous step's start time instead of the
assert step's own start time.

## Telemetry support

The CLI now understands the kiosk `DEVICE_TEST_TELEMETRY` logcat prefix.

- `telemetry-tail` reads recent logcat lines, parses matching telemetry JSON,
  and returns both parsed events and malformed telemetry lines.
- `telemetry-watch` waits for a bounded window, then dumps and parses telemetry
  emitted during that interval.
- `wait-step` waits for a semantic kiosk step event.
- `wait-action-ready` waits for a semantic kiosk action-ready event, optionally
  scoped to a step.

## Inspect mode

`inspect` opens a JSONL session intended for agent-driven exploration without a
scenario file.

- startup emits `session_started` and an initial `screen_snapshot`
- incoming JSON commands can `refresh`, `tap`, `tap_text`, `wait_visible`,
  `type_text`, `keyevent`, `telemetry_tail`, `telemetry_watch`, and `exit`
- state-affecting commands emit a `command_result` followed by a `screen_delta`
  containing the new snapshot and a diff summary

This is enough to let an agent reason about the current UI, choose the next
action, and keep iterating without authoring a scenario up front.

## Packaging

The CLI can now be published as a self-contained single-file executable for the
first supported host targets:

```powershell
dotnet publish VisitLab.Cli -c Release -r win-x64
dotnet publish VisitLab.Cli -c Release -r linux-x64
dotnet publish VisitLab.Cli -c Release -r osx-arm64
dotnet publish VisitLab.Cli -c Release -r osx-x64
```

Publish outputs land under:

```text
VisitLab.Cli/bin/Release/net10.0/<rid>/publish/
```

The published app is self-contained and single-file by default. If you want a
framework-dependent or non-single-file build, override the MSBuild properties
at publish time.

## Next experiment lanes

- See `docs/architecture.md` for the current high-level CLI and view runtime
  architecture, and `docs/subsystems.md` for the active subsystem map.
- For the native `view --decoder ffmpeg` runtime, populate `ffmpeg/bin` with
  host-native shared libraries via `ffmpeg/download-ffmpeg.ps1` or set
  `DEVICE_E2E_FFMPEG_ROOT`.
- `view --record <file.h264|file.mp4|file.mkv>` supports raw H.264 capture and
  container remuxing. `.mp4` and `.mkv` recording require an `ffmpeg`
  executable resolvable from `ffmpeg/bin`, `DEVICE_E2E_FFMPEG_ROOT`, or `PATH`.
- Current macOS publishes already include the SDL3 native runtime, but FFmpeg
  shared libraries still need to be staged separately for live `view` runs.
- Build typed semantic waits such as `wait-step` and `wait-action-ready` on top
  of the raw `DEVICE_TEST_TELEMETRY` parser.
- Expand inspect mode with optional event subscriptions and continuous polling.
- Add an iOS host adapter if the new host interface holds up outside Android.
