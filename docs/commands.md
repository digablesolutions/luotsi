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

**Version and update.** `luotsi --version` prints the CLI version. `luotsi version` returns a JSON envelope with runtime version, installed tag/version, install root, command path, bundled helper APK presence, and installer-managed view extras status when available. `luotsi update` reruns the installer recorded in the installed manifest; use `--dry-run` to inspect the exact command. Stable updates target the latest non-prerelease release. Prerelease updates currently require `--version <tag>` and should use `--channel prerelease`. Luotsi does not auto-update silently. Custom install roots are discovered from `LUOTSI_INSTALL_ROOT`, the running `current` directory, or the platform default install root. On Windows, non-dry-run update requires `--detach` and returns `update_started` after launching a background updater so the running executable can exit before the install directory is replaced.

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
| `lab plan [--device-query <query>]` | Dry-run lab allocation and explain the selected or rejected devices, including recommended claim/run commands |
| `lab claim [--device-query <query>] [--owner <name>] [--ttl-sec 3600]` | Claim exactly one selected device with a host-side lease token |
| `lab leases` | List active host-side device leases |
| `lab release (--lease <lease-id> | --serial <adb serial>)` | Release a host-side device lease |
| `lab extend (--lease <lease-id> | --serial <adb serial>) [--ttl-sec 3600]` | Renew an active host-side device lease |
| `lab quarantine [--device-query <query>] --reason <text> [--owner <name>]` | Mark exactly one selected device unavailable until explicitly unquarantined |
| `lab quarantines` | List quarantined lab devices |
| `lab unquarantine --serial <adb serial>` | Remove a device quarantine |
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
Active `lab claim` leases are honored by `--device-query` selection so CI and agent workflows do not accidentally target an already claimed device. Stale leases can be released by lease id or directly by serial with `lab release --serial <adb serial>`.
Long-running jobs can renew an active lease with `lab extend --serial <adb serial> --ttl-sec <seconds>`.
Active quarantines are also honored by `--device-query`; use them for unhealthy hardware that should stay out of local and CI allocation until repaired.
When `lab plan` is ready, `recommended_commands` includes both an explicit `lab claim` command and a direct `run --path <scenarios> --claim-device ...` command for agents or CI jobs that want allocation and execution in one step.

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

`replay cluster` now adds cross-run failure intelligence to each cluster: similarity class/score, likely cause, best replay artifact root, and graph/scrub commands for the best representative bundle.

`replay graph` exposes `facts`, `causal_chains`, and `hypotheses` in addition to raw graph nodes and edges. Facts are compact subject-predicate-object statements for agents that need failure paths, transitions, selectors, actions, and evidence without traversing the full graph. Causal chains summarize the shortest transition path into each failure node. Hypotheses rank likely-cause hints with confidence, support IDs, and a follow-up command.

| Command | Description |
|---|---|
| `replay summarize --artifacts <artifact-root> [--format json|jsonl]` | Read `session-replay.json` and `session-timeline.jsonl` under an existing artifact root and emit condensed replay summaries, including failure-capsule linkage for failed scenario runs |
| `replay capsule --artifacts <artifact-root> [--write-readme] [--write-json]` | Write the replay capsule with session counts, primary failure, artifact counts, recommended next steps, and suggested commands |
| `replay timeline --artifacts <artifact-root> [--failures] [--type <event-type>] [--contains <text>] [--source-path <timeline-path>] [--sequence <n>] [--since <timestamp>] [--until <timestamp>] [--context <n>] [--limit 200] [--format json|jsonl] [--write-json] [--write-jsonl] [--write-markdown]` | Read ordered replay timeline events with stable details and optional failure/type/text/source/time filtering |
| `replay scrub --artifacts <artifact-root> [--failures] [--source-path <timeline-path>] [--sequence <n>] [--context <n>] [--limit 200] [--write-json] [--write-markdown]` | Create a local previous/focused/next event scrub view with exact commands for moving through replay evidence |
| `replay graph --artifacts <artifact-root> [--failed] [--node-kind <kind>] [--edge-kind <kind>] [--action <text>] [--selector <text>] [--contains <text>] [--insight <kind>] [--severity info|warning|error] [--evidence <kind>] [--fact <text>] [--node <id> --depth 1] [--limit 200] [--format json|jsonl] [--write-json] [--write-jsonl] [--write-markdown]` | Build or query a stable node/edge model over sessions, timeline events, failures, scenarios, artifacts, actions, selectors, screen observations, telemetry signals, and scenario-draft provenance |
| `replay cluster --artifacts <artifact-root> [--min-count <n>] [--similarity same_failure_shape\|likely_same_cause\|same_bucket] [--contains <text>] [--write-json] [--write-markdown]` | Group failed replay sessions by normalized failure shape and emit triage intelligence, likely-cause hints, best replay commands, and replay/search commands |
| `replay open --artifacts <artifact-root> [--dry-run]` | Refresh the artifact browser index, open `index.html` locally, and return the canonical replay front-door summary with next actions |
| `replay scenario-draft --artifacts <artifact-root> --output <scenario.json> [--name <name>] [--write-json] [--write-markdown]` | Convert inspect/replay action events into a conservative starter scenario with review items, warnings, and cleanup suggestions |
| `replay search --artifacts <artifact-root> --contains <text> [--limit 50]` | Search replay timelines and text-like artifacts for errors, labels, telemetry, or log lines |

`replay open` is the canonical replay front door. It attaches the existing artifact root, regenerates `index.md` and `index.html`, opens the HTML index with the platform default opener, and returns session/failure counts, primary failure, one recommended next action, and commands into capsule, timeline, scrub, graph, search, scenario draft, and clustering. `--dry-run` returns the same front-door summary plus the opener command without launching it. `replay summarize` returns the normal JSON command envelope by default and now includes summary-level `commands` that point into the replay capsule, artifact browser, failure scrub, failure graph, and repeated-failure clustering. `--format json` writes only the replay summary object. `--format jsonl` writes a `type: summary` header line followed by one `type: session` line per replay session. Failed scenario runs expose `failure_capsule_path` plus an embedded `failure_capsule` summary with linked reports, grouped failure artifacts, and failure-bundle metadata. `replay capsule` writes the higher-level bundle capsule: it identifies the primary failure with an exact reopen command, includes a compact `failure_timeline` with exact `replay timeline` source commands, reports existing `scenario_draft_artifacts` and `scenario_draft_summary`, counts screenshots/videos/logs/reports/timelines, and returns suggested follow-up commands. With `--write-readme`, it writes `replay-capsule.md` into the artifact root; with `--write-json`, it writes `replay-capsule-summary.json`. Both write options refresh the artifact index. `replay timeline` reads `session-timeline.jsonl` files directly and returns ordered events with path, sequence, timestamp, type, failure relevance, detail text, flattened scalar properties, and commands back into capsule/open/scrub/graph when the selected events include failures; `--contains` filters normalized event type/detail text, `--source-path` and `--sequence` reopen exact events referenced by scenario-draft provenance, `--since` and `--until` filter by event timestamp, `--context` includes neighboring events around filtered matches, `--format json|jsonl` writes raw timeline output instead of the command envelope, and `--write-json`, `--write-jsonl`, and `--write-markdown` persist `replay-timeline.json`, `replay-timeline.jsonl`, and `replay-timeline.md` into the artifact root. `replay scrub` uses the same timeline filters but returns a previous/focused/next event view with exact commands to reopen the focused event, move to adjacent events, search the focused detail, open the replay capsule, open semantic graph context for focused failures, or open the artifact browser; `--write-json` and `--write-markdown` persist `replay-scrub.json` and `replay-scrub.md`. `replay graph` builds a stable semantic-debug graph and can persist `replay-graph.json`, `replay-graph.jsonl`, and `replay-graph.md`; with `--format json|jsonl`, it writes raw machine output instead of the command envelope. The graph result includes `query`, `taxonomy`, `agent_summary`, `total_node_count`, `total_edge_count`, `matched_node_count`, `matched_edge_count`, `truncated`, `node_kinds`, `edge_kinds`, `insights`, `actions`, `evidence_kinds`, `evidence`, `failure_paths`, promoted semantic nodes, and `transitions_to` edges that classify timeline movement such as action-to-failure, screen changes, and telemetry observations. Graph actions start with the replay capsule command so agents can move from semantic context back to the bundle front door. Use `--failed`, `--node-kind`, `--edge-kind`, `--action`, `--selector`, `--contains`, `--insight`, `--severity`, `--evidence`, and `--limit` to return focused graph, insight, and evidence context for agents; use `--node <id> --depth <n>` to expand a deterministic neighborhood around a specific graph node. `replay cluster` groups failed replay sessions by normalized failure shape, including error category/message and failed action/step, returns triage hints with suggested capsule/graph/scrub/search commands for the best matching bundle, and can persist `replay-clusters.json` and `replay-clusters.md`. `replay scenario-draft` reads timeline action events from inspect/view/replay artifacts and writes a valid JSON scenario draft when enough action data is available; `--write-json` and `--write-markdown` persist draft review artifacts with confidence, warnings, suggestions, exact source file/sequence provenance, matching `replay timeline` source commands, capsule/open/scrub/search commands, and a graph command for generated-step provenance. `replay search` scans JSON, JSONL, XML, text, log, Markdown, HTML, and CSV artifacts and returns relative file paths with line numbers, previews, and commands back into capsule/open/scrub/graph when relevant. Failures continue to use the normal error envelope.

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

### Inspect mode protocol

`inspect` is a JSONL request/response session.

- Client writes one JSON command object per line.
- Luotsi writes JSONL events with snake_case keys.

Primary inspect events:

- `session_started`
- `screen_snapshot`
- `screen_delta`
- `command_result`
- `session_ended`
- `protocol_error`
- `session_error`

For command examples, see README [Inspect mode](../README.md#inspect-mode).

### View mode events

`view` is also a JSONL session. Core lifecycle events include `view_started`, `view_stats`, `view_reconnect_requested`, `view_reconnected`, `view_error`, and `view_ended`, with additional operational events for recording/share/input blocking.

See [view-session.md](view-session.md) for the complete event table and operator controls.

`scenario-list` and `run --path` share the same discovery filters: `--include-tag`, `--exclude-tag`, `--name`, and `--action`. `run --path` also supports `--shard-count`, `--shard-index`, and `--shard-strategy` for parallel execution.

Scenario runner flags:

- `--validate-only` validates the selected scenario file(s) and writes reports without creating a device host or executing device work.
- `--dry-run` is available only with `run --path`; it returns the selected scenario plan after filtering and sharding without validating or executing it.
- `--validate-only` and `--dry-run` are mutually exclusive.
- `--events-jsonl`, `--report-json`, and `--report-junit` write machine-readable run outputs for validation and execution flows.
- `--capture-on failure|never` controls runtime failure capture during scenario execution.
- `--attach-artifacts never|on-failure|always` controls whether report outputs include artifact references.
- `--claim-device` creates a host-side lab lease for the selected `--device` or `--device-query` serial and releases it in a `finally` path after the scenario run. Use `--owner <name>` and `--ttl-sec <seconds>` to identify the run and set the safety expiry.

---

## Output Envelopes

Normal command mode returns a single JSON envelope with `schema`, `ok`, `command`, `started_at`, `ended_at`, `data`, `artifacts`, `provenance`, and `error`.

Long-lived `inspect` and `view` sessions are the main exceptions: they stream JSONL events instead of a single final envelope. `replay summarize --format json|jsonl` is the other intentional exception for CI-oriented replay export.

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
