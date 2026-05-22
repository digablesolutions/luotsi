# Scenario Playbooks

Scenarios are JSON files that drive a sequence of device actions. The format is intentionally simple — no YAML ambiguity, no DSL parser, unambiguous across OSes and agents.

```bash
luotsi run --device <serial> --file examples/scenarios/android-home-smoke.json
```

---

## Format

```json
{
  "name": "android-home-smoke",
  "variables": {
    "appPackage": "com.example.app"
  },
  "metadata": {
    "package": "com.example.app",
    "activity": ".MainActivity",
    "device": {
      "model": "Pixel 9",
      "androidRelease": "16",
      "sdk": "36"
    },
    "layout": {
      "width": 1080,
      "height": 2400,
      "orientation": "portrait"
    },
    "notes": "Coordinate steps are calibrated for this layout."
  },
  "steps": [
    { "name": "go home",           "action": "keyevent",       "code": "KEYCODE_HOME" },
    { "name": "let launcher settle","action": "sleep",          "milliseconds": 750 },
    { "name": "capture screenshot", "action": "takeScreenshot", "label": "android-home-smoke" }
  ]
}
```

---

## Template Syntax

Scenario string values support lightweight substitution before execution:

| Syntax | Resolves to |
|---|---|
| `${env:NAME}` | Required environment variable (fails if missing) |
| `${env:NAME\|fallback}` | Optional environment variable with a fallback value |
| `${var:name}` | Variable from the root `variables` block |
| `${now:HHmmss}` | Timestamp fragment — useful for live test data |

---

## Metadata

The optional `metadata` block documents the device and app context a scenario was calibrated against. Luotsi includes it in `scenario-list`, run results, JSON reports, and dry-run plans.

When a real run has device readiness data, Luotsi emits non-fatal `metadata_warnings` if the connected device or foreground app differs from the scenario metadata. This is especially useful for coordinate-heavy scenarios where a different screen, app package, or activity can invalidate tap points.

Supported metadata fields:

| Field | Purpose |
|---|---|
| `package` | Expected app package. |
| `activity` | Expected activity or focus fragment. |
| `device.serial` | Expected device serial. |
| `device.model` | Expected Android model. |
| `device.androidRelease` | Expected Android release. |
| `device.sdk` | Expected SDK level. |
| `layout.width`, `layout.height` | Expected screenshot/screen dimensions. |
| `layout.orientation` | Expected orientation. |
| `notes` | Human notes for maintainers and CI reviewers. |

---

## Timing

Each step result includes:

- `total_ms` — wall time for the step
- `harness_delay_ms` — delay injected by the harness (e.g. settle waits in `tapPoint`)
- `configured_delay_ms` — delay configured in the step itself

The top-level scenario result also includes `prologue_ms`, `steps_ms`, and `non_step_ms` for overhead accounting.

---

## Actions

### Interaction

| Action | Key arguments |
|---|---|
| `waitVisible` | `text`, `timeoutSec` |
| `waitNotVisible` | `text`, `timeoutSec` |
| `tapText` | `text`, `timeoutSec` |
| `tapPoint` | `x`, `y` or `xRatio`, `yRatio`; `postTapDelayMs` *(optional)* |
| `doubleTapHeaderLogo` | — |
| `doubleTap` | `headerLogo: true` only; equivalent to `doubleTapHeaderLogo` |
| `typeText` | `text` |
| `typePin` | `text`, `intervalMs` *(optional)* |
| `keyevent` | `code` (KEYCODE_* string) |

`doubleTap` is currently a compatibility alias for the header-logo interaction only. If `headerLogo: true` is omitted, scenario validation rejects the step.

### Waits & Assertions

| Action | Key arguments |
|---|---|
| `waitLog` | `text`, `timeoutSec` |
| `waitStep` | `step`, `timeoutSec` |
| `waitActionReady` | `text` *(required)*, `step` *(optional)*, `timeoutSec` |
| `resetLog` | — |
| `assertEvent` | `event`, `timeoutSec`; supports `observeFromPreviousStep: true` |
| `assertScreenshot` | `label` *(optional; falls back to `text`/`name`)*, `expectedWidth`, `expectedHeight`, `expectedSha256`, `expectedSha256File`, `baselineFile`, `updateBaseline`, `regionX`, `regionY`, `regionWidth`, `regionHeight`, `expectedRegionSha256`, `expectedRegionSha256File` |
| `assertTextInputReady` | `timeoutSec`, `requireKeyboard` *(optional bool)* |
| `assertBelow` | `text`, `below`, `maxGapPx` *(optional)* |
| `assertAligned` | `text`, `with`, `maxDeltaPx` *(optional)* |
| `assertAppVersion` | `package` *(optional)*, `maxTopInsetPx` *(optional)*, `maxRightInsetPx` *(optional)* |

`assertEvent` with `observeFromPreviousStep: true` begins the log observation window at the previous step's start time rather than the assert step's own start time.

`assertScreenshot` captures a screenshot, stores it as an artifact, records its dimensions and SHA-256 hash, and fails when the provided expected dimensions or hash do not match. It can assert a literal SHA-256, a SHA-256 stored in a text file, or the SHA-256 of a baseline image. Region assertions require `regionX`, `regionY`, `regionWidth`, and `regionHeight`; pair them with `expectedRegionSha256` or `expectedRegionSha256File` to validate the cropped pixel region. Use `updateBaseline: true` with `baselineFile` when intentionally refreshing a checked-in baseline.

### App & Package

| Action | Key arguments |
|---|---|
| `startApp` | `package`, `activity` *(optional)*, `wait` *(bool)* |
| `startUri` | `uri`, `package`, `activity`, `intentAction` *(optional)*, `wait` |
| `forceStop` | `package` |
| `clear` / `clearApp` | `package` |
| `waitForActivity` | `activity` (string or pattern), `timeoutSec` |
| `waitForNotActivity` | `activity` (string or pattern), `timeoutSec` |
| `isAppInstalled` | `package` |
| `listInstalledPackages` | `thirdPartyOnly` *(bool)* |
| `grantPermission` | `package`, `permission` |
| `revokePermission` | `package`, `permission` |

### Artifacts & Utility

| Action | Key arguments |
|---|---|
| `takeScreenshot` | `label` *(optional)* |
| `captureArtifacts` | — |
| `screenState` | — |
| `sleep` | `milliseconds` |

Screenshot assertions are the preferred fallback when an older Android device has weak or broken hierarchy output.

---

## Examples

The repo ships two generic Android smoke scenarios under `examples/scenarios/` that avoid app-specific selectors:

- [`android-home-smoke.json`](../examples/scenarios/android-home-smoke.json)
- [`android-navigation-smoke.json`](../examples/scenarios/android-navigation-smoke.json)
