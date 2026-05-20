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
| `assertTextInputReady` | `timeoutSec`, `requireKeyboard` *(optional bool)* |
| `assertBelow` | `text`, `below`, `maxGapPx` *(optional)* |
| `assertAligned` | `text`, `with`, `maxDeltaPx` *(optional)* |
| `assertAppVersion` | `package` *(optional)*, `maxTopInsetPx` *(optional)*, `maxRightInsetPx` *(optional)* |

`assertEvent` with `observeFromPreviousStep: true` begins the log observation window at the previous step's start time rather than the assert step's own start time.

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

---

## Examples

The repo ships two generic Android smoke scenarios under `examples/scenarios/` that avoid app-specific selectors:

- [`android-home-smoke.json`](../examples/scenarios/android-home-smoke.json)
- [`android-navigation-smoke.json`](../examples/scenarios/android-navigation-smoke.json)
