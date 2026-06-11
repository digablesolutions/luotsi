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

Luotsi is a host-driven Android device automation and replay CLI for AI agents and CI. It runs on the engineer, agent, or runner machine, talks to real Android devices over ADB, and returns structured JSON, JSONL session streams, and replayable artifacts. Use it when a workflow needs real-device state, lab-coordinated execution, and post-run evidence instead of a browser mock or a heavyweight device-farm control plane.

Orchestration, policy, and diagnostics stay on the host. The Android helper stays thin and purpose-built.

Docs site: [https://digablesolutions.github.io/luotsi/](https://digablesolutions.github.io/luotsi/)

## Start Here

- First five minutes: [docs/getting-started/first-five-minutes](https://digablesolutions.github.io/luotsi/docs/getting-started/first-five-minutes/)
- Installation: [docs/getting-started/installation](https://digablesolutions.github.io/luotsi/docs/getting-started/installation/)
- Quickstart: [docs/getting-started/quickstart](https://digablesolutions.github.io/luotsi/docs/getting-started/quickstart/)
- AI agent workflows: [docs/core-workflows/ai-agent-workflows](https://digablesolutions.github.io/luotsi/docs/core-workflows/ai-agent-workflows/)
- Agent loop example: [docs/core-workflows/agent-loop-example](https://digablesolutions.github.io/luotsi/docs/core-workflows/agent-loop-example/)
- Engineering lead evaluation: [docs/use-cases/android-automation-for-engineering-leads](https://digablesolutions.github.io/luotsi/docs/use-cases/android-automation-for-engineering-leads/)
- Live view: [docs/core-workflows/live-view](https://digablesolutions.github.io/luotsi/docs/core-workflows/live-view/)
- Inspect and scenarios: [docs/core-workflows/inspect-and-scenarios](https://digablesolutions.github.io/luotsi/docs/core-workflows/inspect-and-scenarios/)
- Replay and artifacts: [docs/core-workflows/replay-and-artifacts](https://digablesolutions.github.io/luotsi/docs/core-workflows/replay-and-artifacts/)

## The three questions Luotsi should answer

### Can my agent inspect and act on a real Android device?

Yes. `inspect` opens a long-lived JSONL session, emits screen snapshots and deltas, accepts JSON commands such as `wait_visible`, `tap_text`, `tap_element`, `type_text`, `screenshot`, and `exit`, and writes the same event stream into replay artifacts.

### Can CI run this and leave useful evidence?

Yes. `run` executes JSON scenario playbooks from the host, can claim a lab device, writes JSON/JUnit reports, and keeps screenshots, hierarchy captures, logcat, telemetry, timelines, and governance signals under an artifact root.

### Can I debug the failure after the device is gone?

Yes. `replay open`, `replay summarize`, `replay capsule`, `replay timeline`, `replay graph`, `replay cluster`, `replay search`, and `replay scenario-draft` work from saved artifacts instead of requiring another live device session.

## Why Luotsi

- Real device, structured contract. Commands return JSON envelopes; `inspect` and `view --json` expose JSONL sessions for agents and host-side tooling.
- Replay-first failure triage. Scenario runs leave screenshots, hierarchy captures, logcat, telemetry, timelines, reports, and replay bundles for later investigation.
- CI and lab discipline. Device readiness, claims, queues, quarantine, device health, JUnit, and governance signals share the same CLI surface.
- Host-driven by design. The CLI, policy, and diagnostics stay on the operator, agent, or CI machine while the Android helper remains thin.
- Bridge, not replacement. Luotsi is not a scrcpy clone, an Appium replacement, or a hosted device farm. It is the host-side evidence and control layer around real Android device workflows.

## Product Paths

- [When Luotsi fits](https://digablesolutions.github.io/luotsi/docs/use-cases/when-luotsi-fits/) - decide whether Luotsi matches your workflow shape and team setup.
- [AI agent Android automation](https://digablesolutions.github.io/luotsi/docs/use-cases/ai-agent-android-automation/) - inspect and act on a physical Android device with JSONL state.
- [Android CI device lab workflows](https://digablesolutions.github.io/luotsi/docs/use-cases/android-ci-device-lab-workflows/) - run scenarios with lab claims, JUnit, governance, and device health.
- [Replay-driven triage](https://digablesolutions.github.io/luotsi/docs/use-cases/replay-driven-triage/) - explain failures from saved artifacts instead of rerunning.
- [Live remote device inspection](https://digablesolutions.github.io/luotsi/docs/use-cases/live-remote-device-inspection/) - mirror and observe a connected device from the host.
- [Scenario-based Android automation](https://digablesolutions.github.io/luotsi/docs/use-cases/scenario-based-android-automation/) - move from exploration into versioned scenario playbooks.

## How it works

1. **Run a command** - commands return one JSON envelope with `schema`, `ok`, `command`, `started_at`, `ended_at`, `data`, `artifacts`, `provenance`, and `error` by default. Scenario progress stays on stderr while stdout remains parseable.
2. **Run a scenario** - drive multi-step device flows from a small JSON playbook. Steps are validated, templated, and timed; failures produce artifact bundles automatically.
3. **Inspect mode** - open a JSONL session for agent-driven exploration. Luotsi emits structured events (`session_started`, `screen_snapshot`, `screen_delta`, `command_result`, `session_ended`, `protocol_error`, `session_error`) so an agent can reason about the UI and act without a scenario file.
4. **Preserve artifacts** - `run` writes into the user-local artifact home by default; `inspect` and one-shot commands write into a temp artifact root unless you override them with `--artifacts` or `--output-dir`. `artifacts list`, `info`, `open`, `pack`, `verify --require-lab-safe`, and `unpack` make bundles discoverable and shareable.
5. **Replay failures** - start with `luotsi replay packet --artifacts <artifact-root>` to write `run-summary.json` and `run-summary.md` with the At a Glance summary, failure snapshot, packet gate, and first-minute commands. Validate shared packets with `luotsi replay packet --artifacts <artifact-root> --check`, then use `replay open --dry-run`, `summarize`, `capsule`, `timeline`, `scrub`, `graph`, `search`, `scenario-draft`, and clustering from the same saved artifacts.
6. **Live view** - stream a mirrored device display to a local SDL window with an operator control layer, hotkeys, human startup progress, and JSONL events for agents consuming stream state.
7. **Telemetry** - parse structured `LUOTSI_DEVICE_TELEMETRY` events from logcat for semantic waits and assertions.
8. **CI-friendly** - same binary for engineers, CI pipelines, and agent-driven flows, with default envelopes plus optional raw replay summary output for CI consumers.

## Install

Quick install is per-user and does not require admin rights.

**Windows (PowerShell or Windows Terminal):**

```powershell
iex (irm https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.ps1)
```

The installer downloads the latest published release, installs Luotsi under `%LOCALAPPDATA%\Luotsi`, writes a `luotsi` command shim to `%LOCALAPPDATA%\Luotsi\bin`, stages FFmpeg view extras under the install root, and adds the command directory to your user `PATH`. Open a new terminal after the install finishes.

Verify the installed executable:

```powershell
luotsi --version
luotsi version
luotsi devices
```

To pass installer options, use the scriptblock form:

```powershell
& ([scriptblock]::Create((irm https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.ps1))) -Version v1.2.3 -DryRun
& ([scriptblock]::Create((irm https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.ps1))) -InstallRoot 'D:\Tools\Luotsi' -SkipPathUpdate
& ([scriptblock]::Create((irm https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.ps1))) -SkipFfmpeg
```

**macOS / Linux:**

```bash
curl -fsSL https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.sh | sh
```

The shell installer downloads the latest published release, installs Luotsi under `~/.local/share/luotsi`, writes a `luotsi` command shim to `~/.local/share/luotsi/bin`, stages FFmpeg view extras on Linux, and updates your shell profile unless you pass `--skip-path-update`. Open a new terminal after the install finishes.

Verify the installed executable:

```bash
luotsi --version
luotsi version
luotsi devices
```

To pass installer options:

```bash
curl -fsSL https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.sh | sh -s -- --version v1.2.3 --dry-run
curl -fsSL https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.sh | sh -s -- --install-root "$HOME/tools/luotsi" --skip-path-update
curl -fsSL https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.sh | sh -s -- --skip-ffmpeg
```

**Manual fallback.** Download a self-contained archive from [GitHub Releases](https://github.com/digablesolutions/luotsi/releases). Each archive contains the self-contained `luotsi` executable (`luotsi.exe` on Windows) plus any companion files emitted by `dotnet publish`, with no separate .NET runtime required. Release archives are intentionally core-only; run `luotsi doctor --fix` or use the installer to stage FFmpeg view extras for live view.

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

**Update an installed Luotsi.** `luotsi update` reuses the same release installer and install manifest that the quick installers create. Stable updates target the latest non-prerelease GitHub release. Release candidates and other prereleases need an explicit tag for now.

```bash
luotsi version
luotsi update --dry-run
luotsi update --detach
luotsi update --version v0.1.0-rc.4 --channel prerelease --dry-run
luotsi update --version v0.1.0-rc.4 --channel prerelease --detach
```

Luotsi does not auto-update silently. Updates are explicit so CI and lab machines stay reproducible. If Luotsi was installed into a custom root and `luotsi version` cannot find its manifest, set `LUOTSI_INSTALL_ROOT` to that install root. On Windows, non-dry-run update requires `--detach` and returns `update_started` after launching a background updater that waits for the current `luotsi.exe` process to exit before replacing the installed `current` directory.

After install, use the [First Run](#first-run) commands near the top of this README. `quickstart` gives the shared human/agent plan; `doctor` selects or explains the device and reports readiness blockers plus next commands.

## Workflow quickstart

If you already know the target serial, start from `luotsi quickstart --device <serial> --package <app.id>`. If you do not, use bare `luotsi quickstart`; it starts with `luotsi doctor` so the next command comes from live device selection guidance. Add `--human` when you want compact terminal text, or `--write-json --write-markdown` when you want `quickstart-plan.json`, `quickstart-plan.md`, `evaluation-proof-pack.json`, and `evaluation-proof-pack.md` persisted for a human or AI operator handoff. Treat `proof_checks` as the install/device/artifact/device-truth/replay checklist for deciding whether the first five minutes produced usable evidence; each check says whether it is `ready_to_run`, `needs_input`, or `ready_after_artifact`. Run `luotsi quickstart-verify` with the same inputs when you want that checklist grouped into ready, blocked, and later proof commands before starting; it also writes the local plan/proof-pack handoff artifacts and reports the local proofs that passed. Use the proof pack as the durable evidence-gate handoff before calling the first five minutes production-ready. Then choose the workflow that matches what you are trying to do:

1. First-time setup and repair:

  ```bash
  luotsi devices
  luotsi doctor
  luotsi doctor --device <serial>
  luotsi doctor --device <serial> --fix
  luotsi view setup --device <serial>
  ```

2. Manual live debugging:

  ```bash
  luotsi view --device <serial>
  luotsi screen-state --device <serial>
  luotsi inspect --device <serial>
  ```

3. Scenario authoring:

  ```bash
  luotsi scenario-init --file scenarios/smoke.json --name "smoke"
  luotsi scenario-validate --path scenarios
  ```

4. CI execution and reports:

  ```bash
  luotsi run --path scenarios --device <serial> --claim-device --claim-wait-sec 60 --report-junit junit.xml
  ```

  Run JSON reports, JSONL lifecycle events, and failed run payloads include
  additive `governance`, `device_health`, and `ci_policy` objects so CI can
  tell whether a red run looks like observable scenario/app behavior,
  lab/device trouble, environment/setup debt, or a Luotsi/harness-side failure.
  The device-health registry tracks rolling trust for each serial and can
  automatically quarantine unhealthy devices, while `--ci-policy enforced`
  applies the recommended policy exit code directly. JUnit mirrors the same
  signals under `luotsi.governance.*`, `luotsi.device_health.*`, and
  `luotsi.policy.*` properties.
  For shared labs, `--claim-device --claim-wait-sec <seconds>` joins Luotsi's
  durable lease queue instead of failing immediately when the selected serial is
  already leased; inspect pending waiters with `luotsi lab queue`. Set
  `LUOTSI_LAB_STATE_ROOT` when multiple runners should share the same leases,
  queue, quarantine, inventory, and device-health state.

The CLI also exposes this directly via `luotsi help quickstart`; use `luotsi help output` for the JSON envelope, JSONL session, artifact, and replay mental model.

## Code layout

AI agents working in this repository should start with [`AGENTS.md`](AGENTS.md). It summarizes the Luotsi output model, ownership map, and validation commands.

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

Use the public [CLI command groups](https://digablesolutions.github.io/luotsi/docs/reference/cli-command-groups/) as the maintained command surface. The usual first commands are:

| Need | Command |
|---|---|
| Confirm the installed binary | `luotsi version` |
| Find connected devices | `luotsi devices` |
| Get the first-run plan | `luotsi quickstart --human` |
| Check the first-run proof path | `luotsi quickstart-verify --device <serial> --package <app.id>` |
| Diagnose selected-device readiness | `luotsi doctor --device <serial> --fix` |
| Mirror a device for a human operator | `luotsi view --device <serial>` |
| Open an agent JSONL inspection loop | `luotsi inspect --device <serial>` |
| Create a Journey intake handoff | `luotsi journey-intake init --output journey-intake.json --package <app.id> --device <serial> --write-markdown` |
| Create a scenario skeleton | `luotsi scenario-init --file scenarios/smoke.json --name smoke` |
| Validate scenarios without a device | `luotsi scenario-validate --path scenarios` |
| Run scenarios in CI/lab mode | `luotsi run --path scenarios --device <serial> --claim-device --claim-wait-sec 60 --report-junit junit.xml` |
| Reopen saved evidence | `luotsi replay open --artifacts <artifact-root> --dry-run` |
| Verify a shared artifact zip | `luotsi artifacts verify <artifact.zip> --require-lab-safe --sha256 <digest>` |

`luotsi --version` prints the CLI version embedded at build or release time. `luotsi update --dry-run` shows the exact installer command Luotsi would use before changing an installed copy.

## Output And Next Actions

One-shot commands return one JSON envelope by default. Human output leads with the artifact root, a `guide:` reminder that the root is durable evidence, and a `next:` command when Luotsi can name the follow-up before the rest of the summary.

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

The public [CLI command groups](https://digablesolutions.github.io/luotsi/docs/reference/cli-command-groups/) also cover the direct UI and capture commands such as `wait-visible`, `tap`, `type-text`, `keyevent`, `logcat`, and `record`.

### Scenarios & Inspect

| Command | Description |
|---|---|
| `journey-intake validate --file <path>` | Validate a non-executable Journey intake handoff before scenario drafting |
| `journey-intake draft-scenario --file <path> --output <scenario.json>` | Draft a review-required Luotsi evidence skeleton from Journey intake |
| `scenario-list --path <file-or-dir-or-glob>` | Discover scenario files and filters without executing them |
| `scenario-init [--file <path>] [--name <name>]` | Generate a starter scenario with metadata, setup, screenshot steps, teardown, docs links, and next commands |
| `scenario-validate (--file <path> | --path <path>)` | Validate scenarios without creating a device host |
| `scenario-explain --file <path>` | Summarize metadata, actions, lifecycle steps, and suggested commands |
| `run --device <serial> --file <path>` | Execute a JSON scenario playbook |
| `run --device <serial> --path <file-or-dir-or-glob>` | Execute one or many scenario files resolved from a file, directory, or glob |
| `inspect --device <serial>` | Open an agent-driven JSONL inspection session |

## View session

`view` is a long-lived interactive session that mirrors a connected device to a local SDL window. It prints human progress by default, supports `-o jsonl` / `--json` for raw events, and always records the JSONL timeline in artifacts. See the public [Live View guide](https://digablesolutions.github.io/luotsi/docs/core-workflows/live-view/) for the main operator-facing reference.

Key flags: `--preset <name>` (low-latency / balanced / high-quality / safe), `--capture-backend <auto|screenrecord|mediaprojection>`, `--save-profile <name>`, `--record <file>`, `--share-bind <host:port>`, `--read-only`.

`--join-share <host:port>` attaches as a read-only observer. Observer sessions can reconnect and render stream state, but interactive input plus screenshot/record controls are intentionally blocked and emitted as `view_input_blocked` events.

Share relay is lab-oriented: `--share-bind`/`--join-share` currently uses an unauthenticated, unencrypted TCP stream (no TLS, no auth token). Do not expose it on untrusted networks.

The SDL window has a clickable toolbar, multi-device shelf, and hotkeys (F1–F12, Ctrl+V, drag-and-drop). The public [Live View guide](https://digablesolutions.github.io/luotsi/docs/core-workflows/live-view/) covers the main controls and workflow shape.

View screenshots and operator-triggered recordings go to the current artifact root. By default that is a timestamped directory under the host temp folder, for example `%TEMP%\luotsi\<timestamp>-view` on Windows or `/tmp/luotsi/<timestamp>-view` on Linux/macOS. Pass `--artifacts <directory>` to choose it. F12 writes files such as `view-window-001-screenshot.png`; F9 writes `view-window-record-001.h264` unless `--record <file.h264|file.mp4|file.mkv>` supplies a preferred recording path. Use F7 or the toolbar folder button to open the artifact root.

Published Luotsi bundles include the Android view helper APK. Source checkouts can build/install it with `luotsi view setup --device <serial> --fix`; custom helper builds can be selected with `LUOTSI_VIEW_HELPER_APK`.
Release packaging signs the helper with `LUOTSI_ANDROID_KEYSTORE_*` secrets and verifies the certificate against `LUOTSI_ANDROID_CERT_SHA256`. Pull-request CI packages build the helper with the local/debug fallback because they are validation artifacts, not release artifacts. Local/source builds also use debug signing unless signing environment variables are set.

## Inspect mode

`inspect` opens a JSONL session for agent-driven exploration without a scenario file. Startup emits `session_started` and an initial `screen_snapshot`; state-affecting commands emit `command_result` followed by a `screen_delta`. Parse failures emit `protocol_error`, command/runtime failures emit `session_error`, and shutdown emits `session_ended`.

```bash
luotsi inspect --device 192.168.0.134:5555
```

Send one JSON command per line:

```json
{"id":"1","command":"refresh"}
{"id":"2","command":"tap_text","text":"Sign in","text_match":"exact","timeout_sec":10}
{"id":"2b","command":"tap_element","text":"Files","text_match":"exact","resource_id":"com.elotouch.home:id/tvAppName","class_name":"android.widget.TextView","timeout_sec":10}
{"id":"3","command":"telemetry_tail","tail":200}
{"id":"4","command":"exit"}
```

`wait_visible`, `tap_text`, and `tap_element` accept selector fields: `text`, `text_match` (`exact` or `contains`), `content_description`, `content_description_match`, `resource_id`, `resource_id_match`, `class_name`, `class_name_match`, nested `region` (`left`, `top`, `right`, `bottom`), top-level `left`/`top`/`right`/`bottom`, and `allow_ambiguous`.

Available inspect commands: `refresh`, `screen_state`, `snapshot`, `tap`, `tap_text`, `tap_element`, `tap_selector`, `wait_visible`, `wait_element`, `wait_selector`, `type_text`, `keyevent`, `logcat`, `telemetry_tail`, `telemetry_watch`, `screenshot`, `take_screenshot`, `capture_artifacts`, `record`, `exit`.

## Scenarios

Scenarios are JSON playbooks. See the public [Scenario Playbooks guide](https://digablesolutions.github.io/luotsi/docs/reference/scenario-playbooks/) for the format, template syntax, and supported action families.

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

Selector-backed steps are available when text alone is too broad:

```json
{ "name": "tap Files", "action": "tapElement", "selector": { "text": "Files", "textMatch": "exact", "resourceId": "com.elotouch.home:id/tvAppName" } }
```

Use `waitElement` for the same structured selector wait. Scenario selectors use camelCase fields (`textMatch`, `resourceId`, `allowAmbiguous`) while inspect JSONL uses snake_case.

Template syntax: `${env:NAME}`, `${env:NAME|fallback}`, `${var:name}`, `${now:HHmmss}`.

Generic examples: [`examples/scenarios/android-home-smoke.json`](examples/scenarios/android-home-smoke.json), [`examples/scenarios/android-navigation-smoke.json`](examples/scenarios/android-navigation-smoke.json).

For a full device walkthrough with screenshots, reports, and troubleshooting notes, see the public [Buggy Controller Live Demo](https://digablesolutions.github.io/luotsi/docs/tutorials/buggy-controller-live-demo/).

## Output format

One-shot commands return a single JSON envelope by default. Use `--human` or `--console-output human` when you want a concise terminal summary, `--quiet` or `--console-output quiet` when success output should be suppressed, and use `--json` or omit the human flag when a script needs the full envelope. Human output leads with the artifact root, a `guide:` reminder that the root is durable evidence, and a `next:` command when Luotsi can name the follow-up before the rest of the summary. Quiet mode still prints failure envelopes so diagnostics are not lost. Luotsi does not currently use a global `--output` switch for this because some commands already use `--output` for file paths.

Default JSON envelope:

```json
{
  "schema": "luotsi-command.v1",
  "artifacts": {
    "artifact_root": "/tmp/luotsi/...",
    "poll_artifacts": "final"
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

Source checkouts include executable parser examples at [`examples/agents/extract-next-command.py`](examples/agents/extract-next-command.py) and [`examples/agents/extract-next-command.mjs`](examples/agents/extract-next-command.mjs); they accept one JSON envelope or a saved JSONL-style log and print the best next command.

When an agent or CI job needs the next command, check `data.recommended_next_action_command` / `data.recommendedNextActionCommand` first when a check result exposes a direct continuation command, then `data.recommended_next_action.command`, then focused packet evidence such as `data.primary_failure.source_command` or `data.primaryFailure.sourceCommand`, then packet checklist commands such as `data.triage_checklist[].command` or `data.triageChecklist[].command`, then ordered handoff arrays such as `data.recommended_next_steps`, `data.next_actions`, and `data.suggested_commands`. If no richer field is present, use `artifacts.artifact_root` with `luotsi replay packet --artifacts <artifact-root>` first so the loop has `run-summary.json` and `run-summary.md`. Only fall back to command arrays such as `data.commands`, `data.artifact_commands`, and `data.recommended_commands` when there is no artifact root to packetize. Use `luotsi replay open --artifacts <artifact-root> --dry-run` when a human needs the replay front door response, and use `luotsi artifacts open <artifact-root>` only when you specifically need the generic artifact browser. Source checkouts include executable parser examples at [`examples/agents/extract-next-command.py`](examples/agents/extract-next-command.py) and [`examples/agents/extract-next-command.mjs`](examples/agents/extract-next-command.mjs); they accept one JSON envelope or a saved JSONL-style log and print the best next command.

Scenario `run` commands return the scenario result inside `data`, including per-step timing and top-level overhead:

- **Live view** mirrors a connected device to a local SDL window, records a JSONL timeline, supports operator controls, and can expose read-only observer sessions. See [Live View](https://digablesolutions.github.io/luotsi/docs/core-workflows/live-view/).
- **Inspect mode** opens an agent-driven JSONL session with screen snapshots, deltas, command results, and replayable artifacts. See [AI agent workflows](https://digablesolutions.github.io/luotsi/docs/core-workflows/ai-agent-workflows/).
- **Scenarios** are JSON playbooks for repeatable device flows, validation, CI reports, and artifact-backed failure evidence. See [Scenario Playbooks](https://digablesolutions.github.io/luotsi/docs/reference/scenario-playbooks/).
- **Output envelopes** give scripts and agents one predictable JSON shape for command status, data, artifacts, provenance, and errors. See [Output Envelopes](https://digablesolutions.github.io/luotsi/docs/reference/output-envelopes/).
- **Artifacts and replay** preserve device fingerprints, screenshots, hierarchies, logcat, telemetry, timelines, reports, and packageable handoff bundles. See [Replay and artifacts](https://digablesolutions.github.io/luotsi/docs/core-workflows/replay-and-artifacts/).

## Documentation

| Doc | Contents |
|---|---|
| [Luotsi docs](https://digablesolutions.github.io/luotsi/docs/) | Public docs hub for installation, workflows, reference, and tutorials |
| [CLI command groups](https://digablesolutions.github.io/luotsi/docs/reference/cli-command-groups/) | Command families and first-stop command surface |
| [Live View](https://digablesolutions.github.io/luotsi/docs/core-workflows/live-view/) | Presets, profiles, artifacts, hotkeys, and sharing |
| [Scenario Playbooks](https://digablesolutions.github.io/luotsi/docs/reference/scenario-playbooks/) | Playbook format, template syntax, and supported action families |
| [Architecture](https://digablesolutions.github.io/luotsi/docs/concepts/architecture/) | System architecture and component flow |
| [Subsystems](https://digablesolutions.github.io/luotsi/docs/concepts/subsystems/) | CLI, host automation, scenario, view, and telemetry subsystems |
| [Troubleshooting](https://digablesolutions.github.io/luotsi/docs/getting-started/troubleshooting/) | First-run failure shapes, pairing friction, and hierarchy limits |
