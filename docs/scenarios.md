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
| `waitVisible` | `selector`, `timeout_sec` |
| `waitNotVisible` | `selector`, `timeout_sec` |
| `tapText` | `text`, `timeout_sec` |
| `tapPoint` | `x`, `y` |
| `doubleTapHeaderLogo` | — |
| `typeText` | `text` |
| `typePin` | `pin` |
| `keyevent` | `code` (KEYCODE_* string) |

### Waits & Assertions

| Action | Key arguments |
|---|---|
| `waitLog` | `contains`, `timeout_sec` |
| `waitStep` | `step`, `timeout_sec` |
| `waitActionReady` | `step` *(optional)*, `timeout_sec` |
| `resetLog` | — |
| `assertEvent` | `event`, `timeout_sec`; supports `observeFromPreviousStep: true` |
| `assertTextInputReady` | `timeout_sec` |
| `assertBelow` | `above`, `below` selectors |
| `assertAligned` | `left`, `right` selectors |
| `assertAppVersion` | `package`, `version` |

`assertEvent` with `observeFromPreviousStep: true` begins the log observation window at the previous step's start time rather than the assert step's own start time.

### App & Package

| Action | Key arguments |
|---|---|
| `startApp` | `package`, `activity` *(optional)*, `wait` *(bool)* |
| `startUri` | `uri`, `package`, `activity`, `action` *(all optional)*, `wait` |
| `forceStop` | `package` |
| `clear` / `clearApp` | `package` |
| `waitForActivity` | `activity` (string or pattern), `timeout_sec` |
| `waitForNotActivity` | `activity` (string or pattern), `timeout_sec` |
| `isAppInstalled` | `package` |
| `listInstalledPackages` | `thirdParty` *(bool)* |
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
