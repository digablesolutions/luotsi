# Command Reference

All commands run on the host machine and return a single JSON envelope unless noted as a JSONL session or as an explicit raw replay output mode.

```
luotsi [--device <serial> | --device-query <query>] [--platform android] [--adb <path>] [--adb-timeout-sec <n>] <command> [flags]
luotsi --version
luotsi version
luotsi update [--version <tag>] [--channel stable|prerelease] [--dry-run]
```

**ADB path.** If `adb` is not on `PATH` (common in WSL), pass `--adb /path/to/adb` or set `LUOTSI_ADB`. Bounded ADB commands default to a 120-second timeout; override with `--adb-timeout-sec <n>` or `LUOTSI_ADB_TIMEOUT_SEC`. Use `0` to disable.

**Retry policy.** Safe reads (diagnostics, UI dumps, log snapshots, read-only shell probes) get one visible retry after known transient transport errors (protocol faults, missing/offline/connecting devices). Mutating commands (tap, type, install, push, key events) are not retried.

**Artifacts.** Use `--artifacts <directory>` to override the artifact root for the current command or session. Use `--poll-artifacts <final|per-attempt|none>` to control whether polling-style commands write artifacts only at the end, on each attempt, or not at all.

**Version and update.** `luotsi --version` prints the CLI version. `luotsi version` returns a JSON envelope with runtime version, installed tag/version, install root, command path, and bundled helper APK presence. `luotsi update` reruns the installer recorded in the installed manifest; use `--dry-run` to inspect the exact command. Stable updates target the latest non-prerelease release. Prerelease updates currently require `--version <tag>` and should use `--channel prerelease`. Luotsi does not auto-update silently. Custom install roots are discovered from `LUOTSI_INSTALL_ROOT`, the running `current` directory, or the platform default install root. On Windows, non-dry-run update requires `--detach` and returns `update_started` after launching a background updater so the running executable can exit before the install directory is replaced.

## Workflow quickstart

Use these entry points when you want the shortest path into a real Luotsi workflow instead of scanning the full command surface.

For the same summary inside the CLI, run `luotsi help quickstart` or jump directly to a command family with `luotsi help <topic>`.

| Goal | Command |
|---|---|
| Confirm Luotsi can see your device | `luotsi devices` |
| Diagnose and repair first-run issues | `luotsi doctor --device <serial>` |
| Prepare or repair live-view prerequisites | `luotsi view setup --device <serial>` |
| Open a live mirror | `luotsi view --device <serial>` |
| Snapshot current UI state | `luotsi screen-state --device <serial>` |
| Start an agent-driven inspection session | `luotsi inspect --device <serial>` |
| Generate a starter scenario | `luotsi scenario-init --file scenarios/smoke.json --name "smoke"` |
| Validate scenarios without using a device | `luotsi scenario-validate --path scenarios` |
| Run scenarios with CI output | `luotsi run --path scenarios --device <serial> --report-junit junit.xml` |
| Check install metadata | `luotsi version` |
| Update explicit install | `luotsi update --dry-run` then `luotsi update` |

The CLI includes the same flow-oriented summary in `luotsi help quickstart`.

---

## Device & ADB

| Command | Description |
|---|---|
| `version` | Return Luotsi runtime/install metadata as a JSON envelope |
| `update [--version <tag>] [--channel stable|prerelease] [--dry-run] [--detach]` | Re-run the release installer for the recorded install root |
| `devices` | List adb-visible devices |
| `lab status [--device-query <query>]` | Summarize attached-device availability and explain which devices match or are rejected by a selection query |
| `lab doctor [--device-query <query>]` | Detect stale/offline/ambiguous lab state and return concrete remediation commands |
| `device-status (--device <serial> | --device-query <query>)` | Read selected device inventory metadata plus current readiness details |
| `adb server-status` | Host ADB server status |
| `adb version` | ADB binary version |
| `adb features --device <serial>` | ADB feature set for a device |
| `adb mdns check` | mDNS availability check |
| `wait-for-device --device <serial> --timeout-sec <n>` | Wait for device readiness; verifies `adb shell echo ping` before returning |
| `adb reconnect offline` | Reconnect an offline ADB transport (separate from `reconnect` view command) |
| `adb reconnect device` | Reconnect a device transport without changing the active view/profile state |
| `preflight --device <serial> --package <app.id>` | Device preflight check; writes `device-fingerprint.json` |
| `doctor --device <serial> [--package <app.id>] [options]` | Unified onboarding diagnostics for adb, optional package preflight, and live-view readiness |
| `screen-state --device <serial>` | Dump current screen state |

`wait-for-device` is also available as `device-wait` or `adb wait-for-device`.

`doctor` is the first-run entry point. It reuses the existing adb/version checks, optional package-specific preflight, and the same live-view readiness report exposed by `view-doctor`. `doctor --fix` stages Luotsi-owned FFmpeg native libraries when the requested decoder is missing them, then routes through the same helper/install readiness path as `view setup`. Published Luotsi bundles include those repair assets; source checkouts continue to resolve them from the repository layout.

---

## View & Profiles

See [view-session.md](view-session.md) for the full view reference (presets, backends, hotkeys, JSONL events, sharing).

| Command | Description |
|---|---|
| `view --device <serial> [options]` | Open live streaming mirror (JSONL session) |
| `view --profile <name>` | Open view using a saved profile |
| `view --last` | Reopen the last successful view session |
| `reconnect` | Reconnect using the last successful profile |
| `reconnect --profile <name>` | Reconnect using a specific profile |
| `view setup --device <serial> [options]` | Resolve helper, decoder, backend, and recording prerequisites without opening a stream (alias: `view-setup`) |
| `view-doctor --device <serial> [options]` | Diagnostic report: decoder, helper, backend, preflight, MediaProjection, recording |
| `profile-list` | List saved view profiles |
| `profile-delete --name <name>` | Delete a saved view profile |

View screenshots and operator-triggered recordings are written to the artifact root. By default that root is a timestamped directory under the host temp folder, such as `%TEMP%\luotsi\<timestamp>-view` on Windows or `/tmp/luotsi/<timestamp>-view` on Linux/macOS. Pass `--artifacts <directory>` to choose it. F12/toolbar screenshot writes `view-window-001-screenshot.png`; F9/toolbar record writes `view-window-record-001.h264` unless `--record <file.h264|file.mp4|file.mkv>` supplies a preferred output path. F7/toolbar open-folder opens the artifact root.

---

## Replay & Artifact Triage

| Command | Description |
|---|---|
| `replay summarize --artifacts <artifact-root> [--format json|jsonl]` | Read `session-replay.json` and `session-timeline.jsonl` under an existing artifact root and emit condensed replay summaries, including failure-capsule linkage for failed scenario runs |
| `replay capsule --artifacts <artifact-root> [--write-readme] [--write-json]` | Return a compact capsule manifest with session counts, primary failure, artifact counts, and suggested next commands |
| `replay timeline --artifacts <artifact-root> [--failures] [--type <event-type>] [--contains <text>] [--limit 200] [--format json|jsonl] [--write-json] [--write-jsonl] [--write-markdown]` | Read ordered replay timeline events with stable details and optional failure/type/text filtering |
| `replay open --artifacts <artifact-root> [--dry-run]` | Refresh the artifact browser index and open `index.html` locally |
| `replay scenario-draft --artifacts <artifact-root> --output <scenario.json> [--name <name>]` | Convert inspect/replay action events into a conservative starter scenario with warnings and cleanup suggestions |
| `replay search --artifacts <artifact-root> --contains <text> [--limit 50]` | Search replay timelines and text-like artifacts for errors, labels, telemetry, or log lines |

`replay summarize` returns the normal JSON command envelope by default. `--format json` writes only the replay summary object. `--format jsonl` writes a `type: summary` header line followed by one `type: session` line per replay session. Failed scenario runs expose `failure_capsule_path` plus an embedded `failure_capsule` summary with linked reports, grouped failure artifacts, and failure-bundle metadata. `replay capsule` is the higher-level entry point for a bundle: it identifies the primary failure, counts screenshots/videos/logs/reports/timelines, and returns suggested follow-up commands. With `--write-readme`, it writes `replay-capsule.md` into the artifact root; with `--write-json`, it writes `replay-capsule-summary.json`. Both write options refresh the artifact index. `replay timeline` reads `session-timeline.jsonl` files directly and returns ordered events with path, sequence, timestamp, type, failure relevance, and detail text; `--contains` filters normalized event type/detail text, `--format json|jsonl` writes raw timeline output instead of the command envelope, and `--write-json`, `--write-jsonl`, and `--write-markdown` persist `replay-timeline.json`, `replay-timeline.jsonl`, and `replay-timeline.md` into the artifact root. `replay open` attaches the existing artifact root, regenerates `index.md` and `index.html`, and opens the HTML index with the platform default opener; `--dry-run` returns the opener command without launching it. `replay scenario-draft` reads timeline action events from inspect/view/replay artifacts and writes a valid JSON scenario draft when enough action data is available. `replay search` scans JSON, JSONL, XML, text, log, Markdown, HTML, and CSV artifacts and returns relative file paths with line numbers and previews. Failures continue to use the normal error envelope.

---

## Wireless

### Legacy (Android ≤10)

`wireless` infers the device Wi-Fi address from `adb shell ip route get 8.8.8.8` when `--host` is omitted, then switches the device to TCP/IP mode.

```bash
luotsi wireless --device <usb-serial> --host 192.168.0.44
```

### TLS/mDNS (Android 11+)

Three commands cover the modern wireless debugging pairing flow:

| Command | Description |
|---|---|
| `wireless-scan` | Scan for `_adb-tls-pairing._tcp`, `_adb-tls-connect._tcp`, and legacy `_adb._tcp` services |
| `wireless-pair --endpoint <host:port> --code <code>` | Pair with a device; pass `--service <name>` from `wireless-scan` instead of `--endpoint` |
| `wireless-connect --endpoint <host:port>` | Connect to a paired device |
| `wireless-connect --service <service-name>` | Resolve a `_adb-tls-connect._tcp` service and connect |
| `wireless-connect ... --save-profile <name>` | Connect and save a view profile in one step |

`wireless-scan` is useful for inspecting available services. `wireless-pair` and `wireless-connect --service` perform their own mDNS discovery when no explicit endpoint is supplied — `wireless-scan` is not a prerequisite. If only one service of the required type is discovered, `--endpoint` and `--service` can be omitted.

`wireless-pair` without `--code` returns a structured error — `adb pair` requires interactive input that Luotsi cannot safely drive. Run `adb pair <host:port>` manually or always pass `--code`.

The returned `device_selector` from `wireless-connect` can be passed directly to `view --device`.

```bash
luotsi wireless-connect --service adb-14141FDF600081-TnSdi9 --save-profile desk-wifi
luotsi view --profile desk-wifi
```

---

## Port Forwarding

Endpoints use adb syntax: `tcp:8080`, `tcp:0`, `localabstract:service`.

| Command | Description |
|---|---|
| `forward --local <endpoint> --remote <endpoint>` | Forward a host port to a device port |
| `forward-list` | List active host→device forwards |
| `forward-remove --local <endpoint>` | Remove a host→device forward |
| `reverse --remote <endpoint> --local <endpoint>` | Forward a device port to a host port |
| `reverse-list` | List active device→host reverses |
| `reverse-remove --remote <endpoint>` | Remove a device→host reverse |

---

## App Lifecycle

| Command | Description |
|---|---|
| `start-app --package <app.id> [--activity <activity>] [--wait]` | Launch an app |
| `start-uri --uri <uri> [--package <app.id>] [--activity <activity>] [--action <intent>] [--wait]` | Launch a URI intent |
| `force-stop --package <app.id>` | Force-stop an app |
| `clear --package <app.id>` | Clear app data (alias: `clear-app`) |
| `is-app-installed --package <app.id>` | Check if a package is installed |
| `list-installed-packages [--third-party]` | List installed packages |
| `wait-for-activity --activity <activity-or-pattern>` | Wait until an activity is in the foreground |
| `wait-for-not-activity --activity <activity-or-pattern>` | Wait until an activity leaves the foreground |
| `grant-permission --package <app.id> --permission <permission>` | Grant a runtime permission |
| `revoke-permission --package <app.id> --permission <permission>` | Revoke a runtime permission |

---

## Interaction, Logs, and Capture

These commands are the direct device-control surface outside scenarios and `inspect`.

| Command | Description |
|---|---|
| `wait-visible --text <label> [--timeout-sec 15]` | Wait until a visible text selector appears on screen |
| `tap-text --device <serial> --text <text>` | Tap a UI element by visible text |
| `tap --x <px> --y <px>` | Tap an absolute screen coordinate |
| `type-text --text <value>` | Send text input to the focused field |
| `keyevent --code <code>` | Send an Android key event such as `KEYCODE_HOME` |
| `logcat [--tail 200]` | Snapshot recent raw logcat lines |
| `wait-log --device <serial> --contains <text> --timeout-sec <n>` | Wait for a logcat line matching a substring |
| `record --output <file.mp4> [--time-limit-sec 30]` | Record the device screen to a host video file |

`record` is the direct ADB screenrecord command and writes exactly to `--output`. It is separate from live-view F9 recording, which records the decoded live stream and defaults to the current artifact root.

---

## Telemetry & Semantic Waits

Luotsi reads the `LUOTSI_DEVICE_TELEMETRY` logcat marker to parse structured semantic events from the app under test.

| Command | Description |
|---|---|
| `telemetry-tail --device <serial> --tail <n>` | Snapshot recent telemetry from logcat |
| `telemetry-watch --device <serial> --timeout-sec <n>` | Collect telemetry over a bounded window |
| `wait-step --device <serial> --step <name>` | Wait for a `LUOTSI_DEVICE_TELEMETRY` step event |
| `wait-action-ready --device <serial> --action <name> [--step <name>]` | Wait for a `LUOTSI_DEVICE_TELEMETRY` action-ready event |

`telemetry-tail` and `telemetry-watch` write both `.txt` and `.json` artifacts alongside parsed events and any malformed telemetry lines.

---

## Scenarios & Inspect

| Command | Description |
|---|---|
| `scenario-init [--file <path>] [--name <name>] [--package <app.id>]` | Generate a starter scenario with metadata, setup, screenshot-oriented steps, teardown, docs link, and next commands |
| `scenario-list --path <scenario-file-or-directory-or-glob> [filters]` | Discover scenario files and report matched names, tags, and actions without executing them |
| `scenario-validate (--file <path> | --path <path>)` | Validate one or many scenarios without creating a device host |
| `scenario-explain --file <path>` | Summarize scenario metadata, lifecycle step counts, actions, docs, and suggested commands |
| `run --device <serial> --file <path>` | Execute one JSON scenario playbook; also supports `--validate-only`, `--events-jsonl`, `--report-json`, `--report-junit`, `--capture-on`, and `--attach-artifacts` |
| `run --device <serial> --path <scenario-file-or-directory-or-glob>` | Execute one or many scenario files discovered from a file, directory, or glob; supports filtering, `--dry-run`, `--validate-only`, reporting, artifact-policy flags, and sharding |
| `inspect --device <serial>` | Open an agent-driven JSONL inspection session |

See [scenarios.md](scenarios.md) for the playbook format and full action reference.
`inspect` is described in the README [Inspect mode](../README.md#inspect-mode) section.

`scenario-list` and `run --path` share the same discovery filters: `--include-tag`, `--exclude-tag`, `--name`, and `--action`. `run --path` also supports `--shard-count`, `--shard-index`, and `--shard-strategy` for parallel execution.

Scenario runner flags:

- `--validate-only` validates the selected scenario file(s) and writes reports without creating a device host or executing device work.
- `--dry-run` is available only with `run --path`; it returns the selected scenario plan after filtering and sharding without validating or executing it.
- `--validate-only` and `--dry-run` are mutually exclusive.
- `--events-jsonl`, `--report-json`, and `--report-junit` write machine-readable run outputs for validation and execution flows.
- `--capture-on failure|never` controls runtime failure capture during scenario execution.
- `--attach-artifacts never|on-failure|always` controls whether report outputs include artifact references.

---

## Output Envelopes

Normal command mode returns a single JSON envelope with `ok`, `command`, `data`, `artifacts`, `provenance`, and `error`. Long-lived `inspect` and `view` sessions are the main exceptions: they stream JSONL events instead of a single final envelope. `replay summarize --format json|jsonl` is the other intentional exception for CI-oriented replay export.

Common `error.category` values currently include:

| Category | Typical meaning |
|---|---|
| `usage_error` | The command line or scenario input is invalid |
| `log_wait_timeout` | A `wait-log` operation timed out |
| `oracle_timeout` | A semantic telemetry wait such as `wait-step` or `wait-action-ready` timed out |
| `configuration_error` | Host, adb, device-availability, helper, or environment setup is not ready |
| `selector_or_screen_state` | A UI/screen-state wait timed out |
| `scenario_error` | A scenario failed for another runtime reason |

The exact classification is derived from the current command failure path, so treat these as the current public values rather than a forever-closed enum.
