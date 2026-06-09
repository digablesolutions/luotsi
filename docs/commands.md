# Command Reference

All commands run on the host machine and return a single JSON envelope unless noted as an interactive session, an explicit raw replay output mode, or a one-shot command invoked with `--human`, `--quiet`, `--console-output human`, or `--console-output quiet`. Human mode keeps the same command surface but now renders failed `run` and replay front-door flows as compact triage capsules that surface the primary failure, evidence counts, and the best next command.

```
luotsi [--device <serial> | --device-query <query>] [--platform android] [--adb <path>] [--adb-timeout-sec <n>] <command> [flags]
luotsi quickstart [--device <serial>] [--package <app.id>] [--artifacts <directory>] [--write-json] [--write-markdown]
luotsi --version
luotsi version
luotsi update [--version <tag>] [--channel stable|prerelease] [--dry-run] [--detach]
```

**ADB path.** If `adb` is not on `PATH` (common in WSL), pass `--adb /path/to/adb` or set `LUOTSI_ADB`. Bounded ADB commands default to a 120-second timeout; override with `--adb-timeout-sec <n>` or `LUOTSI_ADB_TIMEOUT_SEC`. Use `0` to disable.

**Retry policy.** Safe reads (diagnostics, UI dumps, log snapshots, read-only shell probes) get one visible retry after known transient transport errors (protocol faults, missing/offline/connecting devices). Mutating commands (tap, type, install, push, key events) are not retried. Setup/download repair work and lab probes use separate named retry policies; lab status/doctor expose probe `attempt_count` and `retry_count`.

**Artifacts.**
- Use `--artifacts <directory>` to override the artifact root for the current command or session. Scenario `run` also accepts `--output-dir <directory>` as a clearer alias for the same root.
- Without an explicit override, `run` writes to Luotsi's default user-local artifact home (`%LOCALAPPDATA%\Luotsi\artifacts` on Windows, `~/.local/share/luotsi/artifacts` on Linux/macOS, with temp-folder fallback when unavailable).
- Discover and inspect artifacts:
  - `luotsi artifacts list [--artifacts <directory>] [--limit 20]` lists recent run ids.
  - `luotsi artifacts info <artifact-root-or-run-id-or-package.zip>` or `luotsi artifacts info --last [--artifacts <directory>]` summarizes one bundle without opening or changing it.
- Package and redaction:
  - `artifacts pack` writes `luotsi-artifact-package.json` into the zip (schema/version, run id, created timestamp, category counts, recommended unpack-time commands, and packed file paths).
  - Packages are exact copies by default. `--redact lab-safe` opt-in redacts obvious secrets from text-like entries, leaves source artifacts and binary media unchanged, and records redaction counts in the manifest.
- Validation and safety gates:
  - `luotsi artifacts verify <package.zip> [--require-lab-safe]` validates a received package without writing files.
  - `artifacts verify` and `artifacts info <package.zip>` require the manifest, check archive entries against it, report SHA-256 plus lab-safe redaction status, and return checksum-verified unpack/replay commands without extracting.
  - `--require-lab-safe` makes verify, unpack, or intake an enforcing CI/support handoff gate. Verify reports `status: blocked` for unredacted packages; unpack/intake reject them before extraction.
- Restore workflows:
  - `artifacts unpack` performs the same validation, supports `--sha256 <digest>` before extraction, and refreshes `index.html` for non-dry-run restores.
  - `luotsi artifacts intake <package.zip> [--require-lab-safe] [--write-json] [--write-readme] [--sha256 <digest>]` validates and restores a shared package locally.
  - `artifacts intake` also reports received-package status and exact info/open/replay commands, supports `--open` to launch the refreshed index, and can persist `artifact-intake-summary.json` and `artifact-intake.md` with `--write-json --write-readme`.
- `artifacts pack`, `artifacts unpack`, and `artifacts intake` all support `--dry-run` for safe handoff previews.

**Version and update.** `luotsi --version` prints the CLI version. `luotsi version` returns a JSON envelope with runtime version, installed tag/version, install root, command path, bundled helper APK presence, and installer-managed view extras status when available. `luotsi update` reruns the installer recorded in the installed manifest; use `--dry-run` to inspect the exact command. Stable updates target the latest non-prerelease release. Prerelease updates currently require `--version <tag>` and should use `--channel prerelease`. Luotsi does not auto-update silently. Custom install roots are discovered from `LUOTSI_INSTALL_ROOT`, the running `current` directory, or the platform default install root. On Windows, non-dry-run update requires `--detach` and returns `update_started` after launching a background updater so the running executable can exit before the install directory is replaced.

## Workflow quickstart

Use these entry points when you want the shortest path into a real Luotsi workflow instead of scanning the full command surface.

For a machine-readable five-minute plan, run `luotsi quickstart`. Add `--human` when you want the same plan as compact terminal text. Without `--device`, the plan starts with `luotsi doctor` so device selection and the exact selected-device next command come from live guidance. Pass `--device`, `--package`, and `--artifacts` when you already know the target and want the output to contain concrete commands for a specific app. Add `--write-json --write-markdown` to persist `quickstart-plan.json` and `quickstart-plan.md` in the artifact root for a copy-paste handoff. For the human help topic, run `luotsi help quickstart` or jump directly to a command family with `luotsi help <topic>`.

| Goal | Command |
|---|---|
| Get a five-minute first-run plan | `luotsi quickstart` or `luotsi quickstart --device <serial> --package <app.id> --artifacts artifacts/first-run` |
| Read the first-run plan in the terminal | `luotsi quickstart --human` |
| Persist a first-run handoff | `luotsi quickstart --artifacts artifacts/first-run --write-json --write-markdown` |
| Confirm Luotsi can see your device | `luotsi devices` |
| Choose a device and diagnose first-run issues | `luotsi doctor` then `luotsi doctor --device <serial>` |
| Prepare or repair live-view prerequisites | `luotsi view setup --device <serial>` |
| Open a live mirror | `luotsi view --device <serial>` |
| Snapshot current UI state | `luotsi screen-state --device <serial>` |
| Start an agent-driven inspection session | `luotsi inspect --device <serial>` |
| Map an app and generate scenario candidates | `luotsi discover --device <serial> --package <app.id> --budget 5m` |
| Generate a starter scenario | `luotsi scenario-init --file scenarios/smoke.json --name "smoke"` |
| Validate scenarios without using a device | `luotsi scenario-validate --path scenarios` |
| Run scenarios with CI output | `luotsi run --path scenarios --device <serial> --report-junit junit.xml` |
| Resume the latest local triage bundle | `luotsi replay open --last --artifacts artifacts --dry-run` |
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
| `lab status [--device-query <query>] [--device-pool <pool>] [--require-capabilities <csv>]` | Summarize attached-device availability, explain selection decisions, and include ADB probe attempt/retry counts |
| `lab doctor [--device-query <query>] [--fix] [--device-pool <pool>] [--require-capabilities <csv>]` | Detect stale/offline/ambiguous lab state, retry transient probes, and return concrete remediation commands |
| `lab plan [--device-query <query>] [--device-pool <pool>] [--require-capabilities <csv>]` | Dry-run lab allocation and explain the selected or rejected devices, including recommended claim/run commands |
| `lab claim [--device-query <query>] [--owner <name>] [--ttl-sec 3600] [--claim-wait-sec 60] [--device-pool <pool>] [--require-capabilities <csv>]` | Claim exactly one selected device with a host-side lease token, or join the scheduler queue and wait fairly |
| `lab leases` | List active host-side device leases |
| `lab queue` | List active queued claim waiters for the lab scheduler |
| `lab release (--lease <lease-id> | --serial <adb serial>)` | Release a host-side device lease |
| `lab extend (--lease <lease-id> | --serial <adb serial>) [--ttl-sec 3600]` | Renew an active host-side device lease |
| `lab quarantine [--device-query <query>] --reason <text> [--owner <name>]` | Mark exactly one selected device unavailable until explicitly unquarantined |
| `lab quarantines` | List quarantined lab devices |
| `lab unquarantine --serial <adb serial>` | Remove a device quarantine |
| `lab inventory list` | List the durable lab inventory registry merged with currently attached devices |
| `lab inventory set (--serial <adb serial> | --device-query <query>) [--pool <pool>] [--capabilities <csv>] [--owner <name>]` | Register durable pool/capability metadata for one lab device |
| `lab inventory clear --serial <adb serial>` | Remove a device's durable lab inventory registration |
| `device-status (--device <serial> | --device-query <query>)` | Read selected device inventory metadata plus current readiness details |
| `adb server-status` | Host ADB server status |
| `adb version` | ADB binary version |
| `adb features --device <serial>` | ADB feature set for a device |
| `adb mdns check` | mDNS availability check |
| `wait-for-device --device <serial> --timeout-sec <n>` | Wait for device readiness; verifies `adb shell echo ping` before returning |
| `adb reconnect offline` | Reconnect an offline ADB transport (separate from `reconnect` view command) |
| `adb reconnect device` | Reconnect a device transport without changing the active view/profile state |
| `preflight --device <serial> --package <app.id>` | Device preflight check; writes `device-fingerprint.json` |
| `doctor [--device <serial> | --device-query <query>] [--package <app.id>] [options]` | Device-selection guidance, or unified onboarding diagnostics for adb, optional package preflight, and live-view readiness |
| `screen-state --device <serial>` | Dump current screen state |

`wait-for-device` is also available as `device-wait` or `adb wait-for-device`.
Active `lab claim` leases are honored by `--device-query` selection so CI and agent workflows do not accidentally target an already claimed device. `--claim-wait-sec` lets an operator or agent join a durable queue for the selected serial instead of failing immediately, and `lab queue` exposes that wait state for diagnostics. Stale leases can be released by lease id or directly by serial with `lab release --serial <adb serial>`. By default Luotsi stores leases, queue entries, quarantines, inventory, and device-health state under the local workspace; set `LUOTSI_LAB_STATE_ROOT` to point that shared lab contract at a central path.
Long-running jobs can renew an active lease with `lab extend --serial <adb serial> --ttl-sec <seconds>`.
Active quarantines are also honored by `--device-query`; use them for unhealthy hardware that should stay out of local and CI allocation until repaired.
`lab inventory` persists per-device pool and capability metadata in the Luotsi workspace. `--device-pool` and `--require-capabilities` let `lab status`, `lab doctor`, `lab plan`, `lab claim`, and `run` require that durable inventory registration before allocating a device.
When `lab plan` is ready, `recommended_commands` includes both an explicit `lab claim` command and a direct `run --path <scenarios> --claim-device ...` command for agents or CI jobs that want allocation and execution in one step. Blocked plans now also return additive scheduler hints such as `blocked_reason`, `next_capacity_at`, `suggested_wait_sec`, and `queue_depth`.

`doctor` is the first-run entry point. Without `--device` or `--device-query`, it lists adb-visible devices and returns `status`, `blockers`, `next_command`, and `recommended_commands` for selecting the next doctor command. With one selected device, it reuses the existing adb/version checks, optional package-specific preflight, and the same live-view readiness report exposed by `view-doctor`. The selected-device result includes a `readiness_plan` with `status`, `blockers`, `next_command`, and `recommended_commands` so operators and agents can see whether the machine is ready, what still blocks it, and which exact command to run next. `doctor --fix` stages Luotsi-owned FFmpeg native libraries when the requested decoder is missing them, retrying transient setup/download failures before reporting a final setup result, then routes through the same helper/install readiness path as `view setup`. Published Luotsi bundles include those repair assets; source checkouts continue to resolve them from the repository layout.

---

## View & Profiles

See [view-session.md](view-session.md) for the full view reference (presets, backends, hotkeys, output modes, JSONL events, sharing).

| Command | Description |
|---|---|
| `view --device <serial> [options]` | Open live streaming mirror; human output by default, `-o jsonl`/`--json` for raw events |
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
| `artifacts list [--artifacts <directory>] [--limit 20]` | List artifact roots and run ids under the default Luotsi run-artifact home or `--artifacts <directory>` |
| `artifacts info (<artifact-root-or-run-id-or-package.zip> \| --last [--artifacts <directory>])` | Summarize one artifact root or run id, including index/replay flags, file categories, and next commands, without mutating the bundle; when given a package zip, validate the manifest and report redaction metadata, SHA-256, and unpack/replay commands without extracting files; `--last` resolves the latest root under the default Luotsi run-artifact home or `--artifacts <directory>` |
| `artifacts open (<artifact-root-or-run-id> \| --last [--artifacts <directory>]) [--dry-run]` | Refresh/open the artifact index for a root or run id; run-id lookup and `--last` resolution search the default Luotsi run-artifact home or `--artifacts <directory>` |
| `artifacts pack <artifact-root-or-run-id> [--output <file.zip>] [--force] [--dry-run] [--redact lab-safe\|off]` | Pack an artifact root into a zip with relative entries plus `luotsi-artifact-package.json` for sharing, upload, or local handoff; successful writes report SHA-256, `--redact lab-safe` redacts obvious secrets from text-like zip entries without mutating source artifacts, and `--dry-run` reports the output path, manifest, redaction metadata, and entry count without writing |
| `artifacts verify <artifact.zip> [--output <directory>] [--require-lab-safe]` | Validate a packed artifact bundle without writing files; reports manifest metadata, package SHA-256, lab-safe redaction status, entry count, the suggested unpack directory, and exact unpack/replay commands; `--require-lab-safe` turns this into a non-zero handoff gate for packages that were not packed with `--redact lab-safe` |
| `artifacts unpack <artifact.zip> [--output <directory>] [--force] [--dry-run] [--require-lab-safe] [--sha256 <digest>]` | Extract a packed artifact bundle into a local root with zip-slip protection, required manifest validation, and package SHA-256 reporting; `--require-lab-safe` rejects unredacted packages before extraction, `--sha256` rejects checksum mismatches before writing, and `--dry-run` validates entries and the manifest without writing |
| `artifacts intake <artifact.zip> [--output <directory>] [--force] [--dry-run] [--require-lab-safe] [--sha256 <digest>] [--write-json] [--write-readme] [--open]` | One-step received-package handoff for support, CI, and agents; applies the same package validation and lab-safe/SHA gates as unpack, restores the artifact root, returns exact info/open/replay commands, can persist `artifact-intake-summary.json` / `artifact-intake.md`, and can open the refreshed index after a successful restore |
| `replay summarize --artifacts <artifact-root> [--format json|jsonl]` | Read `session-replay.json` and `session-timeline.jsonl` under an existing artifact root and emit condensed replay summaries, including failure-capsule linkage for failed scenario runs |
| `replay capsule --artifacts <artifact-root> [--write-readme] [--write-json]` | Write the replay capsule with session counts, primary failure, artifact counts, recommended next steps, and suggested commands |
| `replay timeline --artifacts <artifact-root> [--failures] [--type <event-type>] [--contains <text>] [--source-path <timeline-path>] [--sequence <n>] [--since <timestamp>] [--until <timestamp>] [--context <n>] [--limit 200] [--format json|jsonl] [--write-json] [--write-jsonl] [--write-markdown]` | Read ordered replay timeline events with stable details and optional failure/type/text/source/time filtering |
| `replay scrub --artifacts <artifact-root> [--failures] [--source-path <timeline-path>] [--sequence <n>] [--context <n>] [--limit 200] [--write-json] [--write-markdown]` | Create a local previous/focused/next event scrub view with exact commands for moving through replay evidence |
| `replay graph --artifacts <artifact-root> [--failed] [--node-kind <kind>] [--edge-kind <kind>] [--action <text>] [--selector <text>] [--contains <text>] [--insight <kind>] [--severity info|warning|error] [--evidence <kind>] [--fact <text>] [--node <id> --depth 1] [--limit 200] [--format json|jsonl] [--write-json] [--write-jsonl] [--write-markdown]` | Build or query a stable node/edge model over sessions, timeline events, failures, scenarios, artifacts, actions, selectors, screen observations, telemetry signals, and scenario-draft provenance |
| `replay cluster --artifacts <artifact-root> [--min-count <n>] [--similarity same_failure_shape\|likely_same_cause\|same_bucket] [--contains <text>] [--write-json] [--write-markdown]` | Group failed replay sessions by normalized failure shape and emit triage intelligence, likely-cause hints, best replay commands, and replay/search commands |
| `replay open (--artifacts <artifact-root> \| --last [--artifacts <directory>]) [--dry-run] [--write-json] [--write-markdown]` | Refresh the artifact browser index, open `index.html` locally, and return the canonical replay front-door summary with next actions; `--last` reopens the latest local triage bundle without re-copying the path |
| `replay scenario-draft --artifacts <artifact-root> [--output <scenario.json>\|--file <scenario.json>] [--name <name>] [--validate] [--write-json] [--write-markdown]` | Convert inspect/replay action events into a conservative starter scenario with review items, warnings, inserted pre-tap waits, cleanup normalizations, and optional immediate static validation |
| `replay search --artifacts <artifact-root> --contains <text> [--limit 50]` | Search replay timelines and text-like artifacts for errors, labels, telemetry, or log lines |

Artifact command quick guide:

- `artifacts list` is the discovery step for local runs. It returns run ids, roots, file counts, index presence, replay metadata presence, and exact info/open/pack commands.
- Use `artifacts open --last` for the latest generic artifact browser.
- Use `replay open --last` for the latest replay-specific next actions.
- `artifacts info` is the non-mutating check for one artifact root or package zip:
  - For roots, it reports index/replay flags plus artifact category counts and next commands.
  - For package zips, it validates the manifest and entries, reports redaction metadata plus SHA-256, and returns checksum-verified unpack/replay-after-unpack commands without creating files.
- `artifacts verify` is the explicit received-package gate. It reports `share_safety` (`lab_safe` or `not_redacted`), checks every archive entry against the manifest, and with `--require-lab-safe` returns `status: blocked`, blockers, redaction-repair commands, and exit code 1 for unredacted packages.
- `artifacts pack` writes a zip with relative paths, reports the package SHA-256, recommends an unpack command with `--sha256`, carries `--require-lab-safe` on lab-safe package handoff commands, and refuses to overwrite unless `--force` is supplied.
- `artifacts unpack` extracts the zip into a local artifact root, reports the source package SHA-256, rejects unsafe entries, rejects `--require-lab-safe` failures and `--sha256` mismatches before writing files, and supports `--dry-run` for the same package validation without creating files.
- `artifacts intake` is the one-command restore path for received packages. It performs the same validation, reports `status` (`validated` or `restored`), can launch the refreshed index with `--open`, can write `artifact-intake-summary.json` and `artifact-intake.md`, and returns exact next commands for info, open, and replay.
- `replay open` remains the canonical replay front door, and replay commands continue to provide the persisted JSON/Markdown/JSONL review artifacts described in their command rows above.


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
| `journey-intake validate --file <path>` | Validate a non-executable Android CLI Journey-style intake handoff before Luotsi scenario drafting |
| `journey-intake draft-scenario --file <path> --output <scenario.json>` | Convert a valid Journey intake into a review-required Luotsi evidence skeleton without creating a device host |
| `scenario-init [--file <path>] [--name <name>] [--package <app.id>]` | Generate a starter scenario with metadata, setup, screenshot-oriented steps, teardown, docs link, and next commands |
| `scenario-list --path <scenario-file-or-directory-or-glob> [filters]` | Discover scenario files and report matched names, tags, and actions without executing them |
| `scenario-validate (--file <path> | --path <path>)` | Validate one or many scenarios without creating a device host |
| `scenario-explain --file <path>` | Summarize scenario metadata, lifecycle step counts, actions, docs, and suggested commands |
| `discover --device <serial> --package <app.id> [--activity <activity>] [--budget 5m] [--max-actions 25] [--max-depth 2] [--allow-text <patterns>] [--deny-text <patterns>]` | Explore visible real-device UI state, write discovery and replay artifacts, and emit a review-required JSON scenario candidate |
| `run --device <serial> --file <path>` | Execute one JSON scenario playbook; also supports `--validate-only`, `--progress`, `--events-jsonl`, `--report-json`, `--report-junit`, `--capture-on`, and `--attach-artifacts` |
| `run --device <serial> --path <scenario-file-or-directory-or-glob>` | Execute one or many scenario files discovered from a file, directory, or glob; supports filtering, `--dry-run`, `--validate-only`, progress, reporting, artifact-policy flags, and sharding |
| `inspect --device <serial>` | Open an agent-driven JSONL inspection session |

See [scenarios.md](scenarios.md) for the playbook format and full action reference.

`discover` is the conservative autonomous layer over inspect, run, and replay. It starts the package unless `--no-start` is supplied, reads screen-state snapshots, applies built-in and configured tap policy, taps safe visible clickable elements, follows changed screens up to `--max-depth`, and backtracks when a branch is exhausted, the depth limit is reached, or the run ends. Use comma- or semicolon-separated policy patterns with `--allow-text`, `--deny-text`, `--deny-resource-id`, and `--deny-class` to constrain what discovery may tap; skipped candidates are emitted as `action_skipped` events and the active policy is persisted in `discovery-map.json`. Discovery stops on budget expiry, action limit, no new actions, or a recorded command failure. It writes `discovery-map.json`, `discovery-events.jsonl`, `session-timeline.jsonl`, and `session-replay.json` so the run can be reopened through replay. Generated scenario candidates follow the observed traversal/backtrack order and remain starter artifacts with provenance in the discovery map; review them before using them as CI coverage.

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

`view` is an interactive session. It prints human progress by default, streams raw JSONL events with `-o jsonl`, `--output jsonl`, or `--json`, and always writes those events to `session-timeline.jsonl`. `-o json` is accepted as a JSONL alias because live view is a line-oriented stream. Core lifecycle events include `view_started`, `view_stats`, `view_reconnect_requested`, `view_reconnected`, `view_error`, and `view_ended`, with additional operational events for recording/share/input blocking.

See [view-session.md](view-session.md) for the complete event table and operator controls.

`scenario-list` and `run --path` share the same discovery filters: `--include-tag`, `--exclude-tag`, `--name`, and `--action`. `run --path` also supports `--shard-count`, `--shard-index`, and `--shard-strategy` for parallel execution.

Scenario runner flags:

- `--validate-only` validates the selected scenario file(s) and writes reports without creating a device host or executing device work.
- `--dry-run` is available only with `run --path`; it returns the selected scenario plan after filtering and sharding without validating or executing it.
- `--validate-only` and `--dry-run` are mutually exclusive.
- `--events-jsonl`, `--report-json`, and `--report-junit` write machine-readable run outputs for validation and execution flows. Run JSON payloads now include additive `governance`, `device_health`, and `ci_policy` objects. `governance` classifies whether the run looks product-, lab-, environment-, or harness-related; `device_health` tracks the rolling device state (`healthy`, `suspect`, `recovering`, `quarantined`) plus retry/pass-threshold counters; and `ci_policy` summarizes the recommended CI outcome and exit code. JUnit mirrors these signals under `luotsi.governance.*`, `luotsi.device_health.*`, and `luotsi.policy.*` testsuite and testcase properties.
- `--progress auto|line|plain|quiet|jsonl` controls live progress on stderr. `--quiet` also selects quiet progress for `run` unless `--progress quiet` is supplied explicitly. The final command envelope stays on stdout unless one-shot quiet output is enabled; `--events-jsonl` remains the durable event artifact.
- `--output-dir <directory>` is a scenario-run alias for `--artifacts <directory>`. Successful run results include `artifact_commands` with exact `artifacts open`, `artifacts pack`, and `replay open` commands for the run artifact root.
- `--capture-on failure|never` controls runtime failure capture during scenario execution.
- `--attach-artifacts never|on-failure|always` controls whether report outputs include artifact references.
- `--claim-device` creates a host-side lab lease for the selected `--device` or `--device-query` serial and releases it in a `finally` path after the scenario run. Use `--owner <name>` and `--ttl-sec <seconds>` to identify the run and set the safety expiry. Add `--claim-wait-sec <seconds>` to join the durable scheduler queue and wait fairly for the selected serial when it is already leased or already has queued claimers.
- `--device-pool <pool>` and `--require-capabilities <csv>` apply the same lab admission contract during `run` allocation. Devices can satisfy capabilities from durable `lab inventory` registration plus inferred live facts such as `adb`, transport, type, and `model:<model>`.
- `--ci-policy off|advisory|enforced` controls whether Luotsi only emits the CI policy (`advisory`) or also applies its recommended exit code directly (`enforced`). `--device-health-window-days`, `--retry-budget`, and `--pass-threshold` control the rolling registry window, auto-quarantine threshold, and recovery threshold for device trust.

---

## Output Envelopes

Normal command mode returns a single JSON envelope with `schema`, `ok`, `command`, `started_at`, `ended_at`, `data`, `artifacts`, `provenance`, and `error`. Use `--human` or `--console-output human` on one-shot commands when local terminal readability matters; use `--quiet` or `--console-output quiet` to suppress successful command output while still printing failure envelopes; rerun with `--json` or omit the human flag for the full machine-readable envelope. Luotsi intentionally does not use a global `--output` mode for command envelopes because existing commands such as `record` and `replay scenario-draft` already use `--output` as a file path.

Long-lived `inspect` and `view` sessions are the main exceptions: `inspect` streams JSONL, while `view` prints human output by default and streams JSONL only with `-o jsonl`, `--output jsonl`, or `--json`. `replay summarize --format json|jsonl` is the other intentional exception for CI-oriented replay export.

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
