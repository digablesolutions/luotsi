# View Session

`view` opens a long-lived JSONL session that mirrors a connected Android device to a local SDL window. It emits structured events on stdout so agents and CI can consume stream state without scraping the UI.

```bash
luotsi view --device <serial> --preset safe --decoder ffmpeg --record capture.mp4
luotsi view --profile desk
luotsi view --last
```

View writes screenshots, live-view recordings, JSONL events, and diagnostics under the current artifact root. By default that root is a timestamped directory under the host temp folder, for example `%TEMP%\luotsi\<timestamp>-view` on Windows or `/tmp/luotsi/<timestamp>-view` on Linux/macOS. Use `--artifacts <directory>` to choose a stable location.

Published Luotsi bundles include the Android view helper APK used for MediaProjection and screenrecord startup. Source checkouts can build it with `luotsi view setup --device <serial> --fix`; if you keep a custom helper build elsewhere, set `LUOTSI_VIEW_HELPER_APK` to that `.apk`.

---

## Presets

`--preset <name>` seeds launch defaults. Any flag overrides the preset value.

| Preset | Profile |
|---|---|
| `low-latency` | Smallest buffer, lowest decode delay |
| `balanced` | Default tradeoff between latency and stability |
| `high-quality` | Higher bitrate, larger decode buffer |
| `safe` | Conservative settings; most compatible |

`--defaults` is shorthand for `--preset safe`.

---

## Capture Backends

`--capture-backend <backend>` controls how screen data is captured on the device.

| Backend | Behavior |
|---|---|
| `auto` *(default)* | Prefers MediaProjection; falls back to `screenrecord` if helper startup or consent fails, emitting `view_capture_backend_fallback` |
| `screenrecord` | Legacy Android screen recording; 180-second session limit |
| `mediaprojection` | Requires screen-capture consent on the device; supports `--codec h264` |

---

## Profiles

Profiles persist the resolved connection settings so you can reuse a view configuration by name.

- `--save-profile <name>` — save the current session's settings (device, decoder, size/FPS/bitrate, record target, stats cadences, share settings, fit/fill mode, always-on-top, artifact policy)
- `--profile <name>` — load a saved profile
- `LUOTSI_PROFILE_ROOT` — override the profile directory (useful for repo-local or CI-specific profiles; default is the user app-data directory)

When `--defaults` is combined with `--profile`, connection identity and artifact settings come from the profile but preset-driven launch tuning resets to `safe`.

Every successful `view` launch refreshes the special `last` profile. `reconnect` and `view --last` reuse it.

---

## Stats

| Flag | Default | Description |
|---|---|---|
| `--stats-interval-ms <ms>` | `1000` | Cadence for JSONL `view_stats` events (0 = disabled) |
| `--renderer-stats-interval-ms <ms>` | `0` | Cadence for renderer/title stats updates (0 = every update) |

---

## Hotkeys

| Key | Action |
|---|---|
| `F1` | Android Back |
| `F2` | Android Home |
| `F3` | Android Recents |
| `F4` | Rotate device |
| `F5` | Reconnect stream |
| `F6` | Toggle local stream pause marker |
| `F7` | Open artifact folder |
| `F8` | Toggle fit / fill presentation mode |
| `F9` | Toggle live stream recording |
| `F10` | Toggle the in-window help legend |
| `F11` / `Alt+Enter` | Toggle local fullscreen |
| `F12` | Capture device screenshot to artifact root |
| `Esc` | Exit fullscreen (back to windowed) |
| `Ctrl+V` | Paste host clipboard to device |

Plain text input and common navigation/editing keys are forwarded to the device. Mouse-wheel scrolling is also routed through the session.

**Drag and drop:** `.apk` files are installed on the device; other files are pushed to `/sdcard/Download`; `device:/sdcard/...` or `adb:/sdcard/...` path tokens pull from the device into the artifact root.

`F6` only toggles the local renderer pause marker (`view_stream_paused` / `view_stream_resumed`). It does not stop the upstream device stream.

**Toolbar and shelf.** The SDL window paints an in-window toolbar (help, screenshot, record, reconnect, navigation, rotate, pause, open-folder, fit, fullscreen) so all controls are clickable without memorizing hotkeys. The help button and `F10` toggle a visible legend overlay with the main operator shortcuts. Hover tooltips mirror the keyboard shortcuts for those actions, and the share badge tooltip shows the active share endpoint plus observer count. When multiple adb-visible devices are present, a multi-device shelf appears and lets you switch the mirrored device by clicking.

**Artifact paths.** F12/toolbar screenshot writes files such as `view-window-001-screenshot.png` to the artifact root. F9/toolbar record writes `view-window-record-001.h264` there by default. If `--record <file.h264|file.mp4|file.mkv>` is supplied, startup recording writes that exact path and subsequent operator recordings reuse its directory, base name, and extension with a numeric suffix. Container outputs (`.mp4`, `.mkv`) require an `ffmpeg` executable; raw `.h264` does not. F7/toolbar open-folder opens the artifact root. Each session also mirrors its JSONL operator/runtime events to `session-timeline.jsonl` and writes replay metadata to `session-replay.json` so failures can be triaged from artifacts without reattaching to a live stream. The generated `index.md` and `index.html` now surface those replay artifacts in a dedicated Replay Sessions section with direct metadata/timeline links, and when a failed scenario run leaves a `failure-capsule.json`, the report list includes a compact summary of the failed scenarios, failed steps, and linked failure artifacts.

For CI and agent workflows, `luotsi replay open --artifacts <artifact-root>` is the front door for that replay metadata: it refreshes the local artifact browser and returns session counts, primary failure, recommended next action, and commands into capsule, timeline, scrub, graph, search, scenario draft, and clustering. `luotsi replay summarize --artifacts <artifact-root>` reads the same replay metadata directly for machine consumers. By default it returns the condensed timeline as a normal JSON command envelope. `--format json` writes the bare summary object, and `--format jsonl` writes a summary header line followed by one session line per replay session. The summary includes replay workflow commands that route into `replay open`, `replay capsule`, `replay scrub`, `replay graph`, and repeated-failure clustering when failures are present. It also includes reconnect/share churn and the latest `view_stats` snapshot when those events are present in the timeline. When the artifact root also contains a failed scenario run, the session summary exposes `failure_capsule_path` plus an embedded `failure_capsule` object that links reports, screenshots, logcat, hierarchy, screen-state captures, and failure bundles.

The `view_started` JSONL event includes `artifacts.artifact_root`; `view_screenshot_captured`, `view_recording_started`, and `view_recording_stopped` include the file or record path that was written.

---

## JSONL Events

| Event | When |
|---|---|
| `view_started` | Session established |
| `view_startup_phase` | Structured startup progress emitted during bring-up and diagnostics |
| `view_diagnostic` | Startup/doctor diagnostic detail emitted with status and message |
| `view_stats` | Rolling decode/present FPS and latency (see `--stats-interval-ms`) |
| `view_error` | Unrecoverable stream error |
| `view_ended` | Session closed |
| `view_capture_backend_fallback` | `auto` backend fell back from MediaProjection to screenrecord |
| `view_recording_started` | Recording began (F9 or API) |
| `view_recording_stopped` | Recording ended (includes reconnect-triggered stop reason when applicable) |
| `view_stream_paused` | Stream pause marker toggled on (F6) |
| `view_stream_resumed` | Stream pause marker toggled off (F6) |
| `view_reconnect_requested` | Reconnect triggered (F5 or API) |
| `view_reconnected` | Reconnect succeeded |
| `view_device_switch_requested` | Device shelf switch initiated |
| `view_screenshot_captured` | Screenshot written to artifact root (F12) |
| `view_clipboard_pasted` | Clipboard paste forwarded to device |
| `view_interaction_failed` | A tap/swipe/key-forward interaction could not be completed |
| `view_key_command_sent` | A hotkey action was dispatched |
| `view_artifacts_opened` | Artifact folder opened (F7) |
| `view_file_pushed` | File drag-dropped and pushed to device |
| `view_file_pulled` | File drag-dropped and pulled from device |
| `view_package_installed` | APK drag-dropped and installed |
| `view_device_shelf` | Multiple adb-visible devices detected; shelf rendered |
| `view_share_started` | Share endpoint bound and ready |
| `view_share_client_connected` | A share client joined |
| `view_share_client_disconnected` | A share client left |
| `view_input_blocked` | Interaction suppressed by policy (for example `read_only` or observer-session-only restrictions) |

---

## Stream Sharing

A source session can expose the live stream to a second client over TCP.

```bash
# Source
luotsi view --device <serial> --share-bind 0.0.0.0:9000

# Observer
luotsi view --join-share 192.168.0.10:9000
```

The host session relays the private binary packet protocol and reports the bound endpoint in JSONL via `view_share_started`.

Joined share sessions are forced into read-only observer mode. They reconnect to the shared TCP source rather than talking to adb directly.

**Security note:** share relay is intended for trusted lab/dev networks. The current transport is raw TCP without TLS or authentication, so stream traffic is not encrypted and observers are not identity-verified.

---

## Read-only Mode

`--read-only` turns any view window into an observer surface. The stream renders, reconnect works, and screenshot/record controls remain available, but tap, typing, wheel-scroll, clipboard paste, and drag/drop are blocked and surfaced as `view_input_blocked` events.

`--join-share` sessions are always observer sessions. They use the read-only interaction blocks above and also block screenshot/record controls with `view_input_blocked` (`reason: observer_session`).

---

## view-setup

`view setup` / `view-setup` uses the same option resolution path as `view`, but focuses on preparing the local/device prerequisites instead of opening a stream.

```bash
luotsi view setup --device <serial>
luotsi view-setup --device <serial> --dry-run
```

Without `--dry-run`, setup first tries to resolve the helper APK, builds it with Gradle if needed, installs/verifies it on the selected device, and then runs the same readiness checks exposed by `view-doctor`.

With `--dry-run`, Luotsi stays report-only: it resolves the requested configuration and returns skipped/failing setup steps plus the doctor report, but it does not attempt helper build or install fixes.

---

## view-doctor

`view-doctor` runs the same option resolution as `view` and returns a diagnostic report without opening a stream.

```bash
luotsi view-doctor --device <serial> --preset low-latency
```

Checks: FFmpeg decoder readiness (`LUOTSI_FFMPEG_ROOT` + bundled `ffmpeg/` paths), Android helper package discovery (`LUOTSI_VIEW_HELPER_APK` or repo layout), capture-backend policy, adb device visibility, device preflight, MediaProjection API/encoder/consent readiness, recording target readiness.

Use `view setup` when you want Luotsi to prepare the helper and verify install state before diagnosing readiness. `view-doctor --fix` routes through the same setup path.

Use `doctor` when you want the broader first-run onboarding report. It wraps adb checks, optional package preflight, and the same `view-doctor` / `view setup` readiness flow behind a single command. Published Luotsi bundles include the repair assets required by `doctor --fix` and `view-doctor --fix`; source checkouts continue to resolve them from the repository layout.
