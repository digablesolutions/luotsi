# Agent Loop Reference

## Output Contracts

Normal Luotsi commands return a single envelope:

```json
{
  "schema": "luotsi-command.v1",
  "ok": true,
  "command": "screen-state",
  "data": {},
  "artifacts": { "artifact_root": "artifacts/run" },
  "provenance": {},
  "error": null
}
```

Agents should check `ok` and the process exit code first, then follow the next-command precedence from `SKILL.md`.

## Inspect JSONL Session

Use `inspect` for an agent-controlled session:

```bash
luotsi inspect --device <serial> --artifacts artifacts/agent-loop
```

Protocol shape:

```text
agent writes one JSON command per line -> Luotsi emits JSONL events -> agent waits for matching command_result
```

Safe inspect loop:

1. Wait for `session_started` and a `screen_snapshot`.
2. Send one command with a stable `id`.
3. Wait for the matching `command_result`.
4. If the command changes state, wait for the matching `screen_delta`.
5. Capture screenshot or state before exit/failure.

Example commands:

```json
{"id":"1","command":"wait_visible","text":"Sign in","text_match":"exact","timeout_sec":15}
{"id":"2","command":"tap_text","text":"Sign in","text_match":"exact","timeout_sec":5}
{"id":"3","command":"screenshot","label":"after-sign-in"}
{"id":"4","command":"exit"}
```

Prefer exact text and structured selectors:

```json
{
  "id": "tap-files",
  "command": "tap_text",
  "text": "Files",
  "text_match": "exact",
  "resource_id": "com.example.app:id/itemTitle",
  "class_name": "android.widget.TextView"
}
```

## Parser Examples

Repository examples:

```bash
node examples/agents/inspect-agent-loop.mjs --device <serial> --text "Sign in" --text-match exact --artifacts artifacts/agent-loop
python3 examples/agents/inspect-agent-loop.py --device <serial> --text "Sign in" --text-match exact --artifacts artifacts/agent-loop
```

Next-command extraction:

```bash
luotsi run --file scenarios/smoke.json --device <serial> --artifacts artifacts/smoke-run \
  | python3 examples/agents/extract-next-command.py

luotsi run --file scenarios/smoke.json --device <serial> --artifacts artifacts/smoke-run \
  | node examples/agents/extract-next-command.mjs
```

The parser examples accept normal envelopes, JSONL logs, and persisted `run-summary.json` packets.
