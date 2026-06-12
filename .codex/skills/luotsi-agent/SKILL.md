---
name: luotsi-agent
description: Luotsi command, agent-loop, scenario, lab, replay, and artifact triage guidance for AI assistants working with Android automation. Use when Codex, Claude Code, or another agent needs to choose, install, compose, parse, or validate Luotsi commands; drive inspect/view JSONL loops; promote exploration into scenarios; run shared-lab/CI-safe commands; or triage Luotsi artifact bundles and failure packets.
---

# Luotsi Agent

Use this skill when the task touches Luotsi as a product or as a tool in another project. Luotsi is a host-driven Android automation CLI: it talks to real Android devices over ADB, returns structured envelopes/JSONL streams, and preserves replayable artifact roots for humans, CI, and agents.

Default loop:

```text
command -> structured output -> artifact root -> replay packet -> next action
```

## First Moves

1. Find Luotsi: run `luotsi version`, `luotsi --version`, or inspect `README.md` / `docs/commands.md` in a source checkout. If `luotsi` is missing, use the install guidance in [references/workflows.md](references/workflows.md).
2. Orient the command model: prefer `luotsi help quickstart` and `luotsi help output` before guessing flags.
3. Preserve evidence: pass `--artifacts <directory>` for runs, inspect sessions, view sessions, and first-run plans when the result needs to be shared or replayed.
4. After any interesting/failing device work, run `luotsi replay packet --artifacts <artifact-root>` before broader exploration. Use `--check` when validating a persisted packet.
5. Do not reconnect to a live device just to understand a completed run. Start from the artifact root unless the next task truly requires new device state.

## Choose The Workflow

- **First evaluation / onboarding**: read [references/workflows.md](references/workflows.md), then use `quickstart`, `quickstart-verify`, `doctor`, and `preflight`.
- **Agent-driven live exploration**: read [references/agent-loop.md](references/agent-loop.md), then use `inspect` for JSONL command/result sessions.
- **Human/operator live control**: use `view`, `view setup`, `view-doctor`, profiles, and screenshots/recordings.
- **Repeatable automation**: read [references/scenarios-and-ci.md](references/scenarios-and-ci.md), then use `scenario-init`, `scenario-validate`, and `run`.
- **Shared lab / CI-safe execution**: prefer `lab plan`, `lab claim`, and `run --claim-device --claim-wait-sec 60`; read [references/scenarios-and-ci.md](references/scenarios-and-ci.md).
- **Post-run triage / handoff package**: read [references/replay-and-artifacts.md](references/replay-and-artifacts.md), then use `replay packet`, `replay open --dry-run`, `replay graph`, `replay capsule`, and `artifacts verify/intake`.
- **Source changes inside Luotsi**: read [references/project-map.md](references/project-map.md) for owning files and validation commands.

## Parse Output

Normal commands return one JSON envelope. Check `ok` and process exit code first. Choose the next command in this order:

1. `data.recommended_next_action_command` or `data.recommendedNextActionCommand`
2. `data.recommended_next_action.command`
3. packet evidence: `data.primary_failure.source_command` or `data.primaryFailure.sourceCommand`
4. packet checklist commands: `data.triage_checklist[].command` or `data.triageChecklist[].command`
5. ordered arrays: `data.recommended_next_steps[]`, `data.next_actions[]`, `data.suggested_commands[]`
6. `artifacts.artifact_root`, then run `luotsi replay packet --artifacts <artifact-root>`
7. `data.commands[]`, `data.artifact_commands[]`, `data.recommended_commands[]` only when no artifact root exists to packetize

For persisted `run-summary.json`, expect schema `luotsi-run-summary.v1` and camelCase fields such as `recommendedNextAction.command`, `primaryFailure.sourceCommand`, and `triageChecklist[].command`.

## Safety Rules

- Use `--dry-run`, `scenario-validate`, `run --validate-only`, `replay packet --check`, and `artifacts verify` before destructive or lab-affecting work.
- Do not retry mutating device actions blindly. Safe reads may retry; taps, typing, installs, pushes, and key events should be deliberate.
- Prefer selectors and exact text over coordinates. If coordinates are necessary, preserve device/layout metadata and validate before CI.
- In shared labs, never suggest direct `run --device <serial>` as the safest production command when a claimable device is known. Prefer `--claim-device --claim-wait-sec 60`.
- For shared artifact zips, prefer `artifacts verify --require-lab-safe` before unpacking and `artifacts intake --require-lab-safe` for one-step restore.

## Bundled References

- [references/workflows.md](references/workflows.md): install, first-run, doctor, view, and command selection.
- [references/agent-loop.md](references/agent-loop.md): JSON envelope parsing, `inspect` JSONL, next-command extraction.
- [references/scenarios-and-ci.md](references/scenarios-and-ci.md): scenario authoring, validation, lab claims, CI reports.
- [references/replay-and-artifacts.md](references/replay-and-artifacts.md): replay packet/capsule/graph/timeline and artifact package handoffs.
- [references/project-map.md](references/project-map.md): Luotsi source ownership and validation commands.
