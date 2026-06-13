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

Luotsi is a host-driven Android device automation and replay CLI for AI agents and CI. It talks to real Android devices over ADB, returns structured JSON/JSONL contracts, and preserves replayable evidence so a run can be inspected after the device is gone.

Use Luotsi when you need real-device state, lab-coordinated execution, and artifact-backed triage instead of a browser mock, ad hoc adb script, or heavyweight device-farm control plane.

Public docs: [digablesolutions.github.io/luotsi](https://digablesolutions.github.io/luotsi/)

## Start Here

- [First five minutes](https://digablesolutions.github.io/luotsi/docs/getting-started/first-five-minutes/)
- [Installation](https://digablesolutions.github.io/luotsi/docs/getting-started/installation/)
- [Quickstart](https://digablesolutions.github.io/luotsi/docs/getting-started/quickstart/)
- [AI agent workflows](https://digablesolutions.github.io/luotsi/docs/core-workflows/ai-agent-workflows/)
- [Live view](https://digablesolutions.github.io/luotsi/docs/core-workflows/live-view/)
- [Scenario playbooks](https://digablesolutions.github.io/luotsi/docs/reference/scenario-playbooks/)
- [Replay and artifacts](https://digablesolutions.github.io/luotsi/docs/core-workflows/replay-and-artifacts/)
- [CLI command groups](https://digablesolutions.github.io/luotsi/docs/reference/cli-command-groups/)

## Why Luotsi

- **Real devices, structured contracts.** Commands return JSON envelopes; `inspect` and `view --json` expose JSONL streams for agents and host-side tooling.
- **Replay-first triage.** Runs preserve screenshots, hierarchies, logcat, telemetry, timelines, reports, and replay bundles.
- **CI and lab discipline.** Device readiness, claims, queues, quarantine, health, JUnit, and governance signals share the same CLI surface.
- **Host-driven by design.** Orchestration, policy, and diagnostics stay on the engineer, agent, or runner machine while the Android helper remains thin.

## Install

Windows:

```powershell
iex (irm https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.ps1)
```

macOS / Linux:

```bash
curl -fsSL https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.sh | sh
```

Verify:

```bash
luotsi --version
luotsi version
luotsi devices
```

For installer options, manual archives, source builds, and first-run setup, use the [Installation guide](https://digablesolutions.github.io/luotsi/docs/getting-started/installation/).

## First Run

```bash
luotsi quickstart --human
luotsi doctor
luotsi doctor --device <serial> --fix
```

If you already know the target app and device:

```bash
luotsi quickstart --device <serial> --package <app.id> --artifacts artifacts/first-run --write-json --write-markdown
luotsi doctor --device <serial> --package <app.id> --fix
```

## Main Workflows

| Need | Start with | Docs |
|---|---|---|
| Human/operator device loop | `luotsi view --device <serial>` | [Live view](https://digablesolutions.github.io/luotsi/docs/core-workflows/live-view/) |
| AI agent exploration | `luotsi inspect --device <serial>` | [AI agent workflows](https://digablesolutions.github.io/luotsi/docs/core-workflows/ai-agent-workflows/) |
| Repeatable scenario CI | `luotsi scenario-init`, `luotsi scenario-validate`, `luotsi run` | [Scenario playbooks](https://digablesolutions.github.io/luotsi/docs/reference/scenario-playbooks/) |
| Evidence review after a run | `luotsi replay packet --artifacts <artifact-root>` | [Replay and artifacts](https://digablesolutions.github.io/luotsi/docs/core-workflows/replay-and-artifacts/) |
| Shared lab execution | `luotsi run --claim-device --claim-wait-sec 60 ...` | [Shared lab operations](https://digablesolutions.github.io/luotsi/docs/reference/shared-lab-operations/) |
| Android CLI Journey handoff | `luotsi journey-intake init ...` | [Evidence-backed Android Journeys](https://digablesolutions.github.io/luotsi/docs/core-workflows/evidence-backed-android-journeys/) |

## Command Surface

The maintained command reference lives in the docs site: [CLI command groups](https://digablesolutions.github.io/luotsi/docs/reference/cli-command-groups/).

Common first commands:

```bash
luotsi devices
luotsi screen-state --device <serial>
luotsi inspect --device <serial>
luotsi scenario-init --file scenarios/smoke.json --name smoke
luotsi scenario-validate --path scenarios
luotsi run --path scenarios --device <serial> --report-junit junit.xml
luotsi artifacts verify <artifact.zip> --require-lab-safe --sha256 <digest>
```

## Output Model

Normal commands return one JSON envelope by default. Human output is available with `--human` or `--console-output human`; JSONL sessions are used by `inspect` and `view`. Artifact roots are durable evidence, and `luotsi replay packet --artifacts <artifact-root>` writes the first-minute investigation packet before replay commands reopen them later.

Read the full model in [Output envelopes](https://digablesolutions.github.io/luotsi/docs/reference/output-envelopes/).

## Build From Source

The repo is pinned to .NET SDK `10.0.300` in [`global.json`](global.json).

```bash
dotnet build Luotsi.sln
dotnet test Luotsi.sln
dotnet run --project Luotsi.Cli -- devices
```

AI agents working in this repository should start with [`AGENTS.md`](AGENTS.md). Contributors should use the [Contribution guide](https://digablesolutions.github.io/luotsi/docs/contributing/guide/).
