# View Session

`view` opens a long-lived JSONL session that mirrors a connected Android device to a local SDL window. It emits structured events on stdout so agents and CI can consume stream state without scraping the UI.

```bash
luotsi view --device <serial> --preset safe --decoder ffmpeg --record capture.mp4
luotsi view --profile desk
luotsi view --last
```

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
| `F6` | Toggle stream pause marker |
| `F7` | Open artifact folder |
| `F8` | Toggle fit / fill presentation mode |
| `F9` | Toggle live stream recording |
| `F11` / `Alt+Enter` | Toggle local fullscreen |
| `F12` | Capture device screenshot to artifact root |
| `Esc` | Exit fullscreen (back to windowed) |
| `Ctrl+V` | Paste host clipboard to device |

Plain text input and common navigation/editing keys are forwarded to the device. Mouse-wheel scrolling is also routed through the session.

**Drag and drop:** `.apk` files are installed on the device; other files are pushed to `/sdcard/Download`; `device:/sdcard/...` or `adb:/sdcard/...` path tokens pull from the device into the artifact root.

**Toolbar and shelf.** The SDL window paints an in-window toolbar (screenshot, record, reconnect, navigation, rotate, pause, open-folder, fit, fullscreen) so all controls are clickable without memorizing hotkeys. When multiple adb-visible devices are present, a multi-device shelf appears and lets you switch the mirrored device by clicking.

---

## JSONL Events

| Event | When |
|---|---|
| `view_started` | Session established |
| `view_stats` | Rolling decode/present FPS and latency (see `--stats-interval-ms`) |
| `view_error` | Unrecoverable stream error |
| `view_ended` | Session closed |
| `view_capture_backend_fallback` | `auto` backend fell back from MediaProjection to screenrecord |
| `view_recording_started` | Recording began (F9 or API) |
| `view_recording_stopped` | Recording ended |
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
| `view_input_blocked` | `--read-only` suppressed an interactive request |

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

---

## Read-only Mode

`--read-only` turns any view window into an observer surface. The stream renders and screenshots/reconnect/record controls work, but tap, typing, wheel-scroll, clipboard paste, and drag/drop are blocked and surfaced as `view_input_blocked` events.

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
