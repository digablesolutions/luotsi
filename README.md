<p align="center">
  <img src="docs/luotsi-logo.svg" alt="Luotsi" width="360">
</p>

<p align="center">
  <a href="https://github.com/digablesolutions/luotsi/actions/workflows/ci.yml"><img alt="CI workflow" src="https://github.com/digablesolutions/luotsi/actions/workflows/ci.yml/badge.svg"></a>
  <a href="https://github.com/digablesolutions/luotsi/actions/workflows/release.yml"><img alt="Release workflow" src="https://github.com/digablesolutions/luotsi/actions/workflows/release.yml/badge.svg"></a>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white">
  <img alt="Platform Android" src="https://img.shields.io/badge/platform-Android-3DDC84?logo=android&logoColor=0F172A">
</p>

# Luotsi

Luotsi is a host-driven CLI for Android device automation, inspection, and live view. It runs on the engineer or CI machine, talks to real devices over ADB, and returns structured JSON results plus artifacts. Orchestration, policy, and diagnostics stay on the host; the on-device helper stays thin and purpose-built.

## How it works

1. **Run a command** — every command returns one JSON envelope with `ok`, `command`, `data`, `artifacts`, `provenance`, and `error`. No third-party device server required.
2. **Run a scenario** — drive multi-step device flows from a small JSON playbook. Steps are validated, templated, and timed; failures produce artifact bundles automatically.
3. **Inspect mode** — open a JSONL session for agent-driven exploration. Luotsi emits screen snapshots and diffs so an agent can reason about the UI and act without a scenario file.
4. **Live view** — stream a mirrored device display to a local SDL window with an operator control layer, hotkeys, and JSONL events for agents consuming stream state.
5. **Telemetry** — parse structured `LUOTSI_DEVICE_TELEMETRY` events from logcat for semantic waits and assertions.
6. **CI-friendly** — same binary, same output shape for engineers, CI pipelines, and agent-driven flows.

## Install

Quick install is per-user and does not require admin rights.

**Windows (PowerShell or Windows Terminal):**

```powershell
iex (irm https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.ps1)
```

The installer downloads the latest published release, installs Luotsi under `%LOCALAPPDATA%\Luotsi`, writes a `luotsi` command shim to `%LOCALAPPDATA%\Luotsi\bin`, and adds that directory to your user `PATH`. Open a new terminal after the install finishes.

Verify the installed executable:

```powershell
luotsi --version
luotsi devices
```

To pass installer options, use the scriptblock form:

```powershell
& ([scriptblock]::Create((irm https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.ps1))) -Version v1.2.3 -DryRun
& ([scriptblock]::Create((irm https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.ps1))) -InstallRoot 'D:\Tools\Luotsi' -SkipPathUpdate
```

**macOS / Linux:**

```bash
curl -fsSL https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.sh | sh
```

The shell installer downloads the latest published release, installs Luotsi under `~/.local/share/luotsi`, writes a `luotsi` command shim to `~/.local/share/luotsi/bin`, and updates your shell profile unless you pass `--skip-path-update`. Open a new terminal after the install finishes.

Verify the installed executable:

```bash
luotsi --version
luotsi devices
```

To pass installer options:

```bash
curl -fsSL https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.sh | sh -s -- --version v1.2.3 --dry-run
curl -fsSL https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.sh | sh -s -- --install-root "$HOME/tools/luotsi" --skip-path-update
```

**Manual fallback.** Download a self-contained archive from [GitHub Releases](https://github.com/digablesolutions/luotsi/releases). Each archive contains the self-contained `luotsi` executable (`luotsi.exe` on Windows) plus any companion files emitted by `dotnet publish`, with no separate .NET runtime required.

```bash
# macOS / Linux
./luotsi devices
./luotsi --version

# Windows (PowerShell)
./luotsi.exe devices
./luotsi.exe --version
```

**Source builds.** The repo is pinned to .NET SDK `10.0.300` (see `global.json`):

```bash
dotnet run --project Luotsi.Cli -- devices
```

**Build and test:**

```bash
dotnet build Luotsi.sln
dotnet test Luotsi.sln
```

**First run after install.** Point Luotsi at a connected device and ask it to diagnose or repair the local prerequisites it owns:

```bash
luotsi doctor --device <serial>
luotsi doctor --device <serial> --fix
```

`doctor` reuses the existing adb, device preflight, and live-view readiness checks. With `--fix`, Luotsi stages FFmpeg native libraries when the selected decoder is missing them, then runs the same helper provisioning flow used by `view setup`. Published Luotsi bundles include the repair assets needed for those fixes, and source checkouts continue to use the repository layout.

## Code layout

| Path | Contents |
|---|---|
| `Luotsi.Cli/Cli/` | Entrypoint, command dispatch, help text, inspect-mode session loop |
| `Luotsi.Cli/Hosts/Android/` | Android transport and device interaction runtime |
| `Luotsi.Cli/Artifacts/` | Artifact session management |
| `Luotsi.Cli/Models/` | Shared records, envelopes, screen and scenario data models |
| `Luotsi.Cli/Scenarios/` | Scenario execution flow and failure plumbing |
| `Luotsi.Cli/Telemetry/` | Telemetry parsing contracts and logcat parser |
| `Luotsi.Cli/Errors/` | Typed command and wait exceptions |
| `Luotsi.Cli/Infrastructure/` | Interfaces and default system-backed implementations |

## Commands

Quick reference. See [docs/commands.md](docs/commands.md) for flags, retry behavior, and wireless pairing details.

`luotsi --version` prints the CLI version embedded at build or release time.

### Device & ADB

| Command | Description |
|---|---|
| `devices` | List adb-visible devices |
| `lab status [--device-query <query>]` | Summarize attached-device availability and selection decisions |
| `lab doctor [--device-query <query>] [--fix]` | Detect and repair safe lab-level issues such as offline transports and stale Luotsi port plumbing |
| `device-status --device <serial>` | Read selected device readiness and inventory metadata |
| `adb server-status` | Host ADB server status |
| `adb version` | ADB binary version |
| `adb features --device <serial>` | ADB feature set for a device |
| `adb mdns check` | mDNS availability check |
| `wait-for-device --device <serial>` | Wait for device readiness |
| `adb reconnect offline` | Reconnect an offline ADB transport |
| `preflight --device <serial> --package <app.id>` | Device preflight check |
| `doctor --device <serial> [--package <app.id>] [--fix]` | Unified onboarding report for adb, package preflight, and live-view prerequisites |
| `screen-state --device <serial>` | Dump current screen state |

### View & Profiles

| Command | Description |
|---|---|
| `view --device <serial> [options]` | Open live streaming mirror (JSONL session) |
| `view --profile <name>` | Open view using a saved profile |
| `view --last` | Reopen the last successful view session |
| `reconnect` | Reconnect using the last successful profile |
| `view setup --device <serial> [options]` | Resolve helper and backend prerequisites without opening a stream |
| `view-doctor --device <serial>` | Diagnostic report without opening a stream |
| `profile-list` | List saved profiles |
| `profile-delete --name <name>` | Delete a saved profile |

### Wireless

| Command | Description |
|---|---|
| `wireless --device <usb-serial>` | Switch a USB device to TCP/IP mode (Android ≤10) |
| `wireless-scan` | Discover TLS pairing and connect services via mDNS |
| `wireless-pair --endpoint <host:port> --code <code>` | Pair a device for wireless debugging (Android 11+) |
| `wireless-connect --service <name>` | Connect to a paired device and return its selector |

### Port Forwarding

| Command | Description |
|---|---|
| `forward --local <endpoint> --remote <endpoint>` | Forward host port → device port |
| `forward-list` | List active forwards |
| `forward-remove --local <endpoint>` | Remove a forward |
| `reverse --remote <endpoint> --local <endpoint>` | Forward device port → host port |
| `reverse-list` | List active reverses |
| `reverse-remove --remote <endpoint>` | Remove a reverse |

### App Lifecycle

| Command | Description |
|---|---|
| `start-app --package <app.id> [--activity <activity>] [--wait]` | Launch an app |
| `start-uri --uri <uri> [options]` | Launch a URI intent |
| `force-stop --package <app.id>` | Force-stop an app |
| `clear --package <app.id>` | Clear app data |
| `wait-for-activity --activity <pattern>` | Wait for activity in the foreground |
| `wait-for-not-activity --activity <pattern>` | Wait for activity to leave the foreground |
| `is-app-installed --package <app.id>` | Check if a package is installed |
| `list-installed-packages [--third-party]` | List installed packages |
| `grant-permission --package <app.id> --permission <permission>` | Grant a runtime permission |
| `revoke-permission --package <app.id> --permission <permission>` | Revoke a runtime permission |

### Telemetry & Waits

| Command | Description |
|---|---|
| `telemetry-tail --device <serial> --tail <n>` | Snapshot recent telemetry from logcat |
| `telemetry-watch --device <serial> --timeout-sec <n>` | Collect telemetry over a bounded window |
| `wait-log --device <serial> --contains <text> --timeout-sec <n>` | Wait for a matching logcat line |
| `tap-text --device <serial> --text <text>` | Tap a UI element by visible text |
| `wait-step --device <serial> --step <name>` | Wait for a semantic step telemetry event |
| `wait-action-ready --device <serial> --action <name> [--step <name>]` | Wait for a semantic action-ready telemetry event |

The full command reference also includes direct UI and capture commands such as `wait-visible`, `tap`, `type-text`, `keyevent`, `logcat`, and `record`. See [docs/commands.md](docs/commands.md) for the complete surface.

### Scenarios & Inspect

| Command | Description |
|---|---|
| `scenario-list --path <file-or-dir-or-glob>` | Discover scenario files and filters without executing them |
| `scenario-init [--file <path>] [--name <name>]` | Generate a starter scenario with metadata, setup, screenshot steps, teardown, docs links, and next commands |
| `scenario-validate (--file <path> | --path <path>)` | Validate scenarios without creating a device host |
| `scenario-explain --file <path>` | Summarize metadata, actions, lifecycle steps, and suggested commands |
| `run --device <serial> --file <path>` | Execute a JSON scenario playbook |
| `run --device <serial> --path <file-or-dir-or-glob>` | Execute one or many scenario files resolved from a file, directory, or glob |
| `inspect --device <serial>` | Open an agent-driven JSONL inspection session |

## View session

`view` is a long-lived JSONL session that mirrors a connected device to a local SDL window. See [docs/view-session.md](docs/view-session.md) for the full reference.

Key flags: `--preset <name>` (low-latency / balanced / high-quality / safe), `--capture-backend <auto|screenrecord|mediaprojection>`, `--save-profile <name>`, `--record <file>`, `--share-bind <host:port>`, `--read-only`.

The SDL window has a clickable toolbar, multi-device shelf, and hotkeys (F1–F12, Ctrl+V, drag-and-drop). Full hotkey and JSONL event tables are in [docs/view-session.md](docs/view-session.md).

View screenshots and operator-triggered recordings go to the current artifact root. By default that is a timestamped directory under the host temp folder, for example `%TEMP%\luotsi\<timestamp>-view` on Windows or `/tmp/luotsi/<timestamp>-view` on Linux/macOS. Pass `--artifacts <directory>` to choose it. F12 writes files such as `view-window-001-screenshot.png`; F9 writes `view-window-record-001.h264` unless `--record <file.h264|file.mp4|file.mkv>` supplies a preferred recording path. Use F7 or the toolbar folder button to open the artifact root.

Published Luotsi bundles include the Android view helper APK. Source checkouts can build/install it with `luotsi view setup --device <serial> --fix`; custom helper builds can be selected with `LUOTSI_VIEW_HELPER_APK`.

## Inspect mode

`inspect` opens a JSONL session for agent-driven exploration without a scenario file. Startup emits `session_started` and an initial `screen_snapshot`; state-affecting commands emit `command_result` followed by a `screen_delta`.

```bash
luotsi inspect --device 192.168.0.134:5555
```

Send one JSON command per line:

```json
{"id":"1","command":"refresh"}
{"id":"2","command":"tap_text","text":"Sign in","timeout_sec":10}
{"id":"3","command":"telemetry_tail","tail":200}
{"id":"4","command":"exit"}
```

Available inspect commands: `refresh`, `screen_state`, `snapshot`, `tap`, `tap_text`, `wait_visible`, `type_text`, `keyevent`, `logcat`, `telemetry_tail`, `telemetry_watch`, `screenshot`, `take_screenshot`, `capture_artifacts`, `record`, `exit`.

## Scenarios

Scenarios are JSON playbooks. See [docs/scenarios.md](docs/scenarios.md) for the full format, template syntax, and action reference.

```json
{
  "name": "android-home-smoke",
  "steps": [
    { "name": "go home",            "action": "keyevent",       "code": "KEYCODE_HOME" },
    { "name": "let launcher settle","action": "sleep",          "milliseconds": 750 },
    { "name": "capture screenshot", "action": "takeScreenshot", "label": "android-home-smoke" }
  ]
}
```

Template syntax: `${env:NAME}`, `${env:NAME|fallback}`, `${var:name}`, `${now:HHmmss}`.

Generic examples: [`examples/scenarios/android-home-smoke.json`](examples/scenarios/android-home-smoke.json), [`examples/scenarios/android-navigation-smoke.json`](examples/scenarios/android-navigation-smoke.json).

For a full device walkthrough with screenshots, video, reports, and troubleshooting notes, see [Guides And Tutorials](docs/tutorials.md).

## Output format

Every command returns a single JSON envelope:

```json
{
  "schema": "luotsi-command.v1",
  "ok": true,
  "command": "screen-state",
  "data": {},
  "artifacts": {
    "artifact_root": "/tmp/luotsi/..."
  },
  "provenance": {
    "tool": "luotsi",
    "version": "1.2.3",
    "commit_sha": "...",
    "branch": "main",
    "repository": "digablesolutions/luotsi",
    "ci_provider": "github-actions",
    "ci_run_id": "123456789",
    "os": "Ubuntu 24.04.2 LTS",
    "architecture": "x64",
    "framework": ".NET 10.0.8"
  },
  "error": null
}
```

Failure envelopes include `error.type`, `error.message`, and `error.category`. The current category values are documented in [docs/commands.md](docs/commands.md).

Scenario `run` commands return the scenario result inside `data`, including per-step timing and top-level overhead:

```json
{
  "scenario": "android-home-smoke",
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

## Artifacts

Every command that reaches the device writes artifacts to a dedicated artifact root. Failures produce a bundle automatically:

- `device-fingerprint.json` — written by `preflight` and scenario runs
- `wait-log.txt` / `wait-log.json` — log streaming waits
- `telemetry-tail.txt` / `telemetry-tail.json` — telemetry snapshots
- `telemetry-watch.txt` / `telemetry-watch.json` — bounded telemetry collection
- Failure bundles — screenshot, logcat, screen-state, hierarchy, and metadata when a runtime command fails after reaching the device

## Documentation

| Doc | Contents |
|---|---|
| [docs/commands.md](docs/commands.md) | Full command reference with flags, retry behavior, wireless pairing |
| [docs/view-session.md](docs/view-session.md) | Presets, backends, profiles, hotkeys, JSONL events, sharing |
| [docs/scenarios.md](docs/scenarios.md) | Playbook format, template syntax, all actions |
| [docs/architecture.md](docs/architecture.md) | System architecture and component flow |
| [docs/subsystems.md](docs/subsystems.md) | CLI, host automation, scenario, view, and telemetry subsystems |
