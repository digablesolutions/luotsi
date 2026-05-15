# Device E2E Lab

Local-only experiment for a cross-platform on-device end-to-end harness.

The current kiosk harness proved the useful shape:

- host-side commands that drive a real Android device through `adb`
- stdout as exactly one JSON envelope for agents
- artifacts by default
- scenarios as small readable playbooks
- app-side semantic telemetry as the high-value oracle when available

This repo explores whether the next version should be a typed .NET CLI rather
than PowerShell. It is intentionally separate from the kiosk repo while the
shape is still experimental.

## Why look at scrcpy?

scrcpy has a useful architecture lesson even if we do not copy its protocol:

- keep host orchestration separate from device execution
- make device communication explicit and stream-friendly
- treat video/control/logs as independent channels
- avoid leaving a permanent app installed on the device
- optimize for low startup cost and boring command-line operation

V1 of this lab does **not** vendor scrcpy or implement its server protocol. It
keeps boring ADB primitives first, with room to add an optional `scrcpy` binary
adapter later for low-latency mirroring, recording, or HID/OTG control.

## Current commands

Run from WSL:

```bash
cd /home/perttu/sources/repos/device-e2e-lab
dotnet run --project DeviceE2ELab.Cli -- devices
dotnet run --project DeviceE2ELab.Cli -- preflight --device <serial> --package fi.systam.visit
dotnet run --project DeviceE2ELab.Cli -- screen-state --device <serial>
dotnet run --project DeviceE2ELab.Cli -- tap-text --device <serial> --text "Sign in"
dotnet run --project DeviceE2ELab.Cli -- run --device <serial> --file examples/idle-language-fi.json
```

If WSL cannot see `adb`, pass a path with `--adb` or expose Android platform
tools on WSL's `PATH`.

Every command prints a single JSON envelope:

```json
{
  "schema": "device-e2e-lab-command.v1",
  "ok": true,
  "command": "screen-state",
  "data": {},
  "artifacts": {
    "artifact_root": "/tmp/device-e2e-lab/..."
  },
  "error": null
}
```

## Scenario playbook

The first playbook format is JSON to keep parsing unambiguous across OSes and
agents:

```json
{
  "name": "idle-language-fi",
  "steps": [
    { "name": "open language menu", "action": "tapText", "text": "English", "timeoutSec": 10 },
    { "name": "choose Finnish", "action": "tapText", "text": "Suomi", "timeoutSec": 10 },
    { "name": "assert Finnish sign-in", "action": "waitVisible", "text": "Kirjaudu sisään", "timeoutSec": 15 }
  ]
}
```

Supported actions in this first slice:

- `waitVisible`
- `tapText`
- `typeText`
- `keyevent`
- `sleep`

## Next experiment lanes

- Add semantic telemetry commands compatible with the kiosk
  `DEVICE_TEST_TELEMETRY` logcat contract.
- Add a `scrcpy` adapter that can launch `scrcpy --no-playback --record ...`
  when installed, while preserving the same JSON envelope.
- Add an interactive inspect mode that streams screen-state deltas and lets an
  agent choose the next action without writing a scenario.
- Add host adapters for Android now, then iOS later if the interface holds.
