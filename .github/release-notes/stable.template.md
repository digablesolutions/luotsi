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
- When a release note mentions failed runs or artifacts, point the first follow-up to `luotsi replay open --artifacts <artifact-root> --dry-run` so evaluators can see the primary failure, recommended next action, and follow-up commands without launching a browser.
