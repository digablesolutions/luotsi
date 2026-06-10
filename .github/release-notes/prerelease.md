## Highlights

- Replay, live view, scenario authoring, and device-lab workflows are moving toward a single developer-first Android automation surface.
- This prerelease is intended for early validation on real devices, CI runners, and agent-driven debugging loops.
- Open the first-five-minute, replay, and installation docs first when validating the release.

## Validation Focus

- Exercise `view`, `view setup --fix`, and `doctor --fix` from a clean install.
- Run a small scenario with JSON/JSONL reports and inspect the replay artifacts.
- Run `luotsi help output` for the JSON envelope, JSONL session, artifact root, and replay handoff model.
- Start failed-run triage with `luotsi replay open --artifacts <artifact-root> --dry-run` to see the primary failure, recommended next action, and follow-up commands without launching a browser.
- Check that release packages include the expected helper APK and host-native runtime assets.

## Start Here

- Docs hub: https://digablesolutions.github.io/luotsi/docs/
- First five minutes: https://digablesolutions.github.io/luotsi/docs/getting-started/first-five-minutes/
- Replay and artifacts: https://digablesolutions.github.io/luotsi/docs/core-workflows/replay-and-artifacts/
- Installation: https://digablesolutions.github.io/luotsi/docs/getting-started/installation/
