# Replay And Artifacts

## Replay First

Start from the artifact root. Do not reconnect to a device just to explain a completed run.

```bash
luotsi replay packet --artifacts <artifact-root>
luotsi replay packet --artifacts <artifact-root> --check
luotsi replay open --artifacts <artifact-root> --dry-run
```

`replay packet` writes the durable production handoff: `run-summary.json` and `run-summary.md`. It includes At a Glance, primary failure, recommended next action, and a 60-second checklist. `--check` validates an existing packet as a pass/fail gate.

Use `replay open --dry-run` when a human needs the browser-free replay front door. Use `artifacts open` only when raw file browsing is the explicit goal.

## Triage Commands

```bash
luotsi replay capsule --artifacts <artifact-root> --write-json --write-readme
luotsi replay timeline --artifacts <artifact-root> --failures --context 3 --write-markdown
luotsi replay scrub --artifacts <artifact-root> --failures --context 3 --write-markdown
luotsi replay graph --artifacts <artifact-root> --failed --write-json --write-markdown
luotsi replay graph --artifacts <artifact-root> --node-kind failure --write-markdown
luotsi replay graph --artifacts <artifact-root> --fact action_to_failure --format jsonl
luotsi replay search --artifacts <artifact-root> --contains "not visible"
luotsi replay cluster --artifacts <artifact-root-or-ci-root> --write-json --write-markdown
```

`replay graph` can expose facts, causal chains, and hypotheses. Use these for agent-readable failure paths and likely-cause hints without traversing raw graph nodes manually.

## Artifact Packages

Package for sharing:

```bash
luotsi artifacts pack <artifact-root-or-run-id> --output replay.zip --redact lab-safe
```

Validate before unpacking:

```bash
luotsi artifacts verify replay.zip --require-lab-safe --sha256 <digest>
luotsi artifacts intake replay.zip --output restored-artifacts --require-lab-safe --sha256 <digest> --write-json --write-readme
```

Rules:

- `--redact lab-safe` redacts obvious secrets in text-like zip entries only; source artifacts and binary media remain unchanged.
- `verify` never extracts files.
- `--require-lab-safe` blocks unredacted packages before extraction in verify/unpack/intake flows.
- `intake` is the one-command received-package path for support, CI, and agents.

## Persisted Files To Know

- `run-summary.json` / `run-summary.md`: replay packet handoff.
- `replay-capsule-summary.json` / `replay-capsule.md`: broader triage capsule.
- `scenario-draft-summary.json` / `scenario-draft.md`: generated scenario draft handoff.
- `artifact-intake-summary.json` / `artifact-intake.md`: received-package restore audit.
- `session-timeline.jsonl`: event stream from inspect/view/run sessions.
- `session-replay.json`: replay metadata for view/inspect sessions.
