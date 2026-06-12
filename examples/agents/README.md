# Agent Examples

These examples show how an agent or host-side adapter can drive Luotsi without a separate device control plane.

Start with the same output loop Luotsi uses everywhere:

```text
command -> structured output -> artifact root -> replay command -> next action
```

For the CLI-native primer, run `luotsi help output`. For the published docs version, read [First Five Minutes](../../website/src/content/docs/docs/getting-started/first-five-minutes.mdx).

## Inspect Agent Loop

`inspect-agent-loop.mjs` starts `luotsi inspect`, reads JSONL events from stdout, and writes JSON command objects to stdin.

```bash
node examples/agents/inspect-agent-loop.mjs --device <serial> --text "Sign in" --text-match exact --artifacts artifacts/agent-loop
```

The example is intentionally small. Replace its decision function with your own planner, policy checks, or model call. The protocol stays the same: one JSON object per line in both directions.

When adapting it, keep the loop explicit:

1. Wait for `screen_snapshot`.
2. Send one command with a stable `id`.
3. Wait for the matching `command_result`.
4. If the command changes state, wait for the matching `screen_delta`.
5. Capture artifacts before exiting or when a command fails.

The `--artifacts` value is a base directory. The scripts create it if needed, and Luotsi writes each session into a timestamped child run directory.

For one-shot commands and replay follow-ups, parse the standard envelope before making another device decision. Check `ok` and the process exit code first, then choose the next command in the same order as `luotsi help output`:

1. `data.recommended_next_action_command` in command envelopes; the parsers also tolerate `recommendedNextActionCommand` in mixed or persisted inputs
2. `data.recommended_next_action.command`
3. Focused packet evidence: `data.primary_failure.source_command` / `primaryFailure.sourceCommand`
4. Packet checklist commands: `data.triage_checklist[].command` / `triageChecklist[].command`
5. Ordered handoff arrays: `data.recommended_next_steps`, `data.next_actions`, `data.suggested_commands`
6. Fallback evidence pointer: `artifacts.artifact_root`, then run `luotsi replay packet --artifacts <artifact-root>`
7. Command arrays: `data.commands`, `data.artifact_commands`, `data.recommended_commands` only when there is no artifact root to packetize

Command arrays are not always ordered by the production-friendly first move, so the parser examples prefer the artifact-root packet fallback before `data.commands`, `data.artifact_commands`, or `data.recommended_commands`. When no artifact root is available, the examples still prefer a `replay_open` command over `open_artifacts`; use `open_artifacts` only when the next task is specifically browsing raw files.

Use the tiny parser examples when you want that rule as executable glue:

```bash
luotsi run --file scenarios/smoke.json --device <serial> --artifacts artifacts/smoke-run \
  | python3 examples/agents/extract-next-command.py

luotsi run --file scenarios/smoke.json --device <serial> --artifacts artifacts/smoke-run \
  | node examples/agents/extract-next-command.mjs
```

The parsers accept either one normal Luotsi JSON envelope or a saved JSONL-style log with multiple one-line JSON objects; in JSONL mode they use the last Luotsi command envelope they find. Bad input exits non-zero with an `extract-next-command:` message and no language runtime stack trace.

They also accept the persisted `run-summary.json` packet written by `luotsi replay packet`. That packet uses the artifact JSON schema `luotsi-run-summary.v1` and camelCase fields such as `recommendedNextAction.command`, `primaryFailure.sourceCommand`, `evidenceFiles[]`, and `triageChecklist[].command`, so an agent can download a CI artifact, feed `run-summary.json` into the same parser, and continue with the recommended command, focused evidence command, or structured checklist command without scraping Markdown. Check envelopes expose `recommended_next_action_command` as the direct continuation command because normal Luotsi command-envelope `data` fields use snake_case; they also repeat `evidence_files[]` so the agent can attach or inspect the proof files after selecting the command. The parser examples still tolerate camelCase for persisted packet JSON and mixed tooling. The fallback `artifact_root` command writes that same durable packet before the loop tries broader replay exploration; use `luotsi replay open --artifacts <artifact-root> --dry-run` when a human needs the replay front door response. Run `luotsi replay packet --artifacts <artifact-root> --check` as the pass/fail gate when you receive an existing packet. The source-tree contract lives at `docs/schemas/luotsi-run-summary-v1.md`.

If Luotsi reports a protocol, session, wait, tap, screenshot, post-action state, or inspect-process failure, the scripts exit non-zero and leave the artifact directory behind for replay.

After a failed or interesting run, start from the artifacts instead of reconnecting blindly:

```bash
luotsi artifacts list --artifacts artifacts/agent-loop
luotsi replay packet --last --artifacts artifacts/agent-loop
luotsi replay timeline --artifacts artifacts/agent-loop/<run-id> --type command_result
```

Use selector fields when text is broad:

```bash
node examples/agents/inspect-agent-loop.mjs --device <serial> --text "Files" --text-match exact --resource-id "com.elotouch.home:id/tvAppName" --class-name "android.widget.TextView" --artifacts artifacts/agent-loop
```

For a non-mutating smoke test, add `--no-tap`. The loop still waits for text, writes a screenshot command, and exits with artifacts.

If Bun is your local JavaScript runtime, the same script works without changes:

```bash
bun examples/agents/inspect-agent-loop.mjs --device <serial> --text "Sign in" --text-match exact --artifacts artifacts/agent-loop
```

`inspect-agent-loop.py` is the same idea in portable standard-library Python 3.8+. Keep it as a smoke-test script or CI adapter when Node is not the host runtime.

## Language choice

The protocol is language-agnostic. Node is the first public example because many agent adapters and MCP experiments start in JavaScript or TypeScript, and the same `.mjs` script can also be run by Bun if that is your local runtime.

Python is kept for portable scripting and CI glue. A maintained Go or Rust client should be a separate, tested package rather than a copy-paste variant of this minimal process loop.
