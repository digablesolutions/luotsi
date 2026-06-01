# Agent Examples

These examples show how an agent or host-side adapter can drive Luotsi without a separate device control plane.

## Inspect Agent Loop

`inspect-agent-loop.mjs` starts `luotsi inspect`, reads JSONL events from stdout, and writes JSON command objects to stdin.

```bash
node examples/agents/inspect-agent-loop.mjs --device <serial> --text "Sign in" --text-match exact --artifacts artifacts/agent-loop
```

The example is intentionally small. Replace its decision function with your own planner, policy checks, or model call. The protocol stays the same: one JSON object per line in both directions.

The `--artifacts` value is a base directory. The scripts create it if needed, and Luotsi writes each session into a timestamped child run directory.

If Luotsi reports a protocol, session, wait, tap, screenshot, post-action state, or inspect-process failure, the scripts exit non-zero and leave the artifact directory behind for replay.

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
