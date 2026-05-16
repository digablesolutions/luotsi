# Luotsi

Luotsi is a host-side .NET CLI for cross-platform on-device end-to-end
automation.

The current kiosk harness proved the useful shape:

- host-side commands that drive a real Android device through `adb`
- stdout as exactly one JSON envelope for agents
- artifacts by default
- scenarios as small readable playbooks
- app-side semantic telemetry as the high-value oracle when available

This repo packages the next version as a typed .NET CLI rather than
PowerShell. It stays separate from the kiosk repo while the host-side harness
is generalized beyond the original kiosk-specific runner.

## Why look at this approach?

This approach has a useful architecture lesson even if we do not copy its protocol:


Luotsi v1 does **not** vendor any specific tool or implement its server protocol. It
keeps boring ADB primitives first, with room to add an optional binary
adapter later for low-latency mirroring, recording, or HID/OTG control.

## Code layout

- `Luotsi.Cli/Cli/` contains the entrypoint, command dispatch, help text, and inspect-mode session loop.
- `Luotsi.Cli/Hosts/Android/` contains the Android transport and device interaction runtime.
- `Luotsi.Cli/Artifacts/` contains artifact session management.
- `Luotsi.Cli/Models/` contains shared records, envelopes, and screen/scenario data models.
- `Luotsi.Cli/Scenarios/` contains scenario execution flow and scenario-specific failure plumbing.
- `Luotsi.Cli/Telemetry/` contains telemetry parsing contracts and the kiosk telemetry parser.
- `Luotsi.Cli/Errors/` contains typed command and wait exceptions.
- `Luotsi.Cli/Infrastructure/` contains interfaces plus the default system-backed implementations used by the CLI.

## Current commands

Run from WSL or PowerShell:

```bash
cd <repo-root>
dotnet run --project Luotsi.Cli -- devices
dotnet run --project Luotsi.Cli -- preflight --device <serial> --package dev.luotsi.app
dotnet run --project Luotsi.Cli -- screen-state --device <serial>
dotnet run --project Luotsi.Cli -- view --device <serial> --preset safe --decoder ffmpeg --record capture.mp4 --stats-interval-ms 1000
dotnet run --project Luotsi.Cli -- view --profile desk
dotnet run --project Luotsi.Cli -- view-doctor --device <serial> --preset low-latency
dotnet run --project Luotsi.Cli -- telemetry-tail --device <serial> --tail 200
dotnet run --project Luotsi.Cli -- telemetry-watch --device <serial> --timeout-sec 10
dotnet run --project Luotsi.Cli -- tap-text --device <serial> --text "Sign in"
dotnet run --project Luotsi.Cli -- wait-log --device <serial> --contains "DEVICE_READY" --timeout-sec 20
dotnet run --project Luotsi.Cli -- run --device <serial> --file scenarios/idle-language-switch-finnish.json
```

Inspect mode is intentionally different: it is a long-lived JSONL session over
stdin/stdout rather than a single JSON envelope. Example:

```powershell
dotnet run --project Luotsi.Cli -- inspect --device 192.168.0.134:5555
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
SDL window title or flooding stdout on long-lived sessions. Use
`--stats-interval-ms <ms>` to tune that cadence; the default is `1000`, and
`0` disables JSONL `view_stats` emission entirely. Use
`--renderer-stats-interval-ms <ms>` to throttle local renderer/title stats
updates independently; the default is `0`, which forwards every renderer stats
update. `--preset <name>` seeds the launch defaults without blocking explicit
overrides. The built-in presets are `low-latency`, `balanced`, `high-quality`,
and `safe`; `--defaults` is a shorthand for the conservative `safe` preset.
Use `--save-profile <name>` to persist the resolved connection settings and
`--profile <name>` to reuse them later. Profiles include the device selector,
decoder, size/FPS/bitrate, record target, stats cadences, share settings, and
artifact policy. By default they are stored under the user app-data directory;
set `LUOTSI_PROFILE_ROOT` to use a repo-local or CI-specific profile directory.
The built-in SDL window now exposes an operator control layer: `F12` captures a
device screenshot into the artifact root, `F9` toggles live stream recording,
`F5` reconnects the mirrored stream, `F11` toggles fullscreen, and `F8`
switches between `fit` and `fill` presentation modes. Plain text input, common
navigation/editing keys, mouse-wheel scrolling, host clipboard paste via
`Ctrl+V`, and drag/drop helpers are also routed through the same session-owned
interaction surface. Dropped `.apk` files install on the device; other dropped
files are pushed to `/sdcard/Download`. The SDL window now also paints a small
in-window toolbar and multi-device shelf on top of the mirror surface, so
operators can click screenshot/record/reconnect/fit/fullscreen controls instead
of relying only on hotkeys. When multiple adb-visible devices are present, the
shelf becomes clickable and switches the active mirrored device by reusing the
same reconnect loop that powers `F5`.

Source sessions can expose the live stream to a second client with
`--share-bind <host:port>`. The host session relays the existing private binary
packet protocol over TCP and reports the bound share endpoint in JSONL. A
second client can join that stream with `view --join-share <host:port>`.
Joined share sessions are forced into read-only observer mode and reconnect to
the shared TCP source rather than talking to adb directly.

`--read-only` turns the view window into an observer surface. The stream still
renders, screenshots and reconnect/record controls still work, but tap, typing,
wheel-scroll, clipboard paste, and drag/drop requests are blocked and surfaced
as `view_input_blocked` JSONL events. Joined share sessions behave the same way
by default, but additionally disable device-only actions such as screenshots and
device switching.

`view-doctor` runs the same option resolution as `view` and returns a diagnostic
report instead of opening a stream. The current checks cover FFmpeg decoder
readiness, Android helper package availability, adb device visibility, device
preflight, and optional recording target readiness.

Interactive `view` sessions can now emit additional JSONL events beyond
`view_started`, `view_stats`, `view_error`, and `view_ended`, including:

- `view_recording_started` / `view_recording_stopped`
- `view_reconnect_requested` / `view_reconnected`
- `view_device_switch_requested`
- `view_screenshot_captured`
- `view_clipboard_pasted`
- `view_file_pushed`
- `view_package_installed`
- `view_device_shelf` when multiple adb-visible devices are present
- `view_share_started`
- `view_share_client_connected` / `view_share_client_disconnected`
- `view_input_blocked` when `--read-only` suppresses an interactive request

The implementation currently supports `--platform android`. The host seam is in
place so an iOS adapter can be added later without rewriting the command layer.

Every command prints a single JSON envelope:

```json
{
  "schema": "luotsi-command.v1",
  "ok": true,
  "command": "screen-state",
  "data": {},
  "artifacts": {
    "artifact_root": "/tmp/luotsi/..."
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

Current physical visitor-home smoke runs can stay on pure UI state until the app
starts emitting `LUOTSI_DEVICE_TELEMETRY`:

- `scenarios/idle-visitor-home-smoke-basics.json`
- `scenarios/idle-visitor-home-smoke-language-roundtrip.json`
- `scenarios/idle-header-logo-sync-settings.json`

## Telemetry support

Luotsi currently understands the kiosk `LUOTSI_DEVICE_TELEMETRY` logcat marker.

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
dotnet publish Luotsi.Cli -c Release -r win-x64
dotnet publish Luotsi.Cli -c Release -r linux-x64
dotnet publish Luotsi.Cli -c Release -r osx-arm64
dotnet publish Luotsi.Cli -c Release -r osx-x64
```

Publish outputs land under:

```text
Luotsi.Cli/bin/Release/net10.0/<rid>/publish/
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
  of the raw `LUOTSI_DEVICE_TELEMETRY` parser.
- Expand inspect mode with optional event subscriptions and continuous polling.
- Add an iOS host adapter if the new host interface holds up outside Android.
