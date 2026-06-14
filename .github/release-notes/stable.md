## What changed for agent builders

- Luotsi now has a stable first-minute output loop for agents: `command -> structured output -> artifact root -> replay command -> next action`.
- `inspect` and `view --json` keep JSONL session semantics explicit, while one-shot commands keep the `luotsi-command.v1` envelope contract for scripts and CI.
- Failed runs can start with `luotsi replay packet --artifacts <artifact-root>` to write `run-summary.json` and `run-summary.md`, then use `replay packet --check` as the packet validation gate.
- The repo includes reusable agent guidance and parser examples for extracting the next command from envelopes, JSONL logs, and persisted run-summary packets.

## What changed for engineers and CI

- The CLI is packaged as self-contained release archives for Windows, Linux, macOS x64, and macOS arm64, with installer scripts, SHA-256 checksums, and release asset attestations.
- First-run setup is centered on `luotsi quickstart`, `luotsi doctor`, and `luotsi doctor --fix` so adb readiness, device selection, live-view prerequisites, helper APK presence, and FFmpeg view extras are visible before deeper workflows.
- Scenario runs, shared-lab device claims, JUnit/JSON reports, replay artifacts, failure capsules, replay graphs, and artifact packages now share the same evidence-first handoff model.
- Live view sharing is intended for trusted lab or development networks only; `--share-bind` uses raw TCP today and does not provide TLS or authentication.

## Open this first

- Docs hub: https://digablesolutions.github.io/luotsi/docs/
- First five minutes: https://digablesolutions.github.io/luotsi/docs/getting-started/first-five-minutes/
- AI agent workflows: https://digablesolutions.github.io/luotsi/docs/core-workflows/ai-agent-workflows/
- Installation: https://digablesolutions.github.io/luotsi/docs/getting-started/installation/
- Replay and artifacts: https://digablesolutions.github.io/luotsi/docs/core-workflows/replay-and-artifacts/

## Output and replay handoff

- For source-tree validation, run `luotsi help output` to see the JSON envelope, JSONL session, artifact root, and replay mental model.
- When a release note mentions failed CI runs, agent handoffs, or artifact packets, point the first follow-up to `luotsi replay packet --artifacts <artifact-root>` and the validation gate to `luotsi replay packet --artifacts <artifact-root> --check` so evaluators get `run-summary.json`, `run-summary.md`, the At a Glance summary, primary failure, recommended next action, and the 60-second checklist. Use `luotsi replay open --artifacts <artifact-root> --dry-run` when a human needs the replay front door without launching a browser.
