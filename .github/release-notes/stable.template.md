## What changed for agent builders

- Highlight the changes that affect `inspect`, `view`, `run`, `replay`, JSONL, or artifact-driven workflows.

## What changed for engineers and CI

- Highlight the real-device, adb, scenario, lab, or reliability changes that matter outside an agent loop.

## Open this first

- Docs hub: https://digablesolutions.github.io/luotsi/docs/
- First five minutes: https://digablesolutions.github.io/luotsi/docs/getting-started/first-five-minutes/
- AI agent workflows: https://digablesolutions.github.io/luotsi/docs/core-workflows/ai-agent-workflows/
- Installation: https://digablesolutions.github.io/luotsi/docs/getting-started/installation/
- Replay and artifacts: https://digablesolutions.github.io/luotsi/docs/core-workflows/replay-and-artifacts/

## Output and replay handoff

- For source-tree validation, run `luotsi help output` to see the JSON envelope, JSONL session, artifact root, and replay mental model.
- When a release note mentions failed CI runs, agent handoffs, or artifact packets, point the first follow-up to `luotsi replay packet --artifacts <artifact-root>` and the validation gate to `luotsi replay packet --artifacts <artifact-root> --check` so evaluators get `run-summary.json`, `run-summary.md`, the At a Glance summary, primary failure, recommended next action, and the 60-second checklist. Use `luotsi replay open --artifacts <artifact-root> --dry-run` when a human needs the replay front door without launching a browser.
