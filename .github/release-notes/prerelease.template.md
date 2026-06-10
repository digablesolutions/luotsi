## What changed in this prerelease

- Call out the feature or workflow that is ready for early validation.

## Who should test it now

- Name the users most likely to give useful feedback: agent builders, mobile engineers, CI maintainers, or device-lab operators.

## Open this first

- Docs hub: https://digablesolutions.github.io/luotsi/docs/
- First five minutes: https://digablesolutions.github.io/luotsi/docs/getting-started/first-five-minutes/
- AI agent workflows: https://digablesolutions.github.io/luotsi/docs/core-workflows/ai-agent-workflows/
- Installation: https://digablesolutions.github.io/luotsi/docs/getting-started/installation/
- Replay and artifacts: https://digablesolutions.github.io/luotsi/docs/core-workflows/replay-and-artifacts/

## Output and replay handoff

- For source-tree validation, run `luotsi help output` to see the JSON envelope, JSONL session, artifact root, and replay mental model.
- When a release note mentions failed runs or artifacts, point the first follow-up to `luotsi replay open --artifacts <artifact-root> --dry-run` so evaluators can see the primary failure, recommended next action, and follow-up commands without launching a browser.
