# `luotsi-run-summary.v1`

`run-summary.json` is the durable investigation packet for one Luotsi artifact
root. It is written by `luotsi replay packet --artifacts <artifact-root>` and
by `luotsi replay open --write-json --write-markdown`. Use it as the first file
for CI job summaries, PR comments, and agent loops when a failed run needs to be
understood before opening raw artifacts.

`run-summary.md` is the human-readable companion generated from the same model.
It starts with an At a Glance summary, then a failure snapshot, then a packet
validation gate, then a copy-paste command block followed by a 60-second triage
checklist so a new reviewer can see what failed, prove the packet is current,
run the first command, and avoid broad artifact browsing until the focused
failure window is understood. Agents should prefer `run-summary.json`; humans can start with the
Markdown job summary and then follow the same command fields.

Use `luotsi replay packet --artifacts <artifact-root> --check` when CI, support,
or an agent receives an artifact root and needs to prove that the existing
packet is present, readable, points at the checked artifact root, and has a
Markdown companion before continuing. The check also verifies that the refreshed
artifact index entry points exist and that `run-summary.md` contains the
`## At a Glance` section with the status, recommended next command, and focused
evidence command when a primary failure exists, the `## Failure Snapshot`
section with the primary failure's session, reason, target, scenario, step,
action, and message values when present, the `## Packet Gate` section with the exact
`luotsi replay packet --artifacts <artifact-root> --check` command, the
copy-paste triage command block, the non-null checklist commands inside that
block, the 60-second triage checklist, and the same recommended command as
`run-summary.json`. When `primaryFailure` is present, the check also requires
`primaryFailure.sourceCommand` to be present in both the structured checklist
and Markdown packet. A successful check returns `luotsi-run-summary-check.v1`
with `recommendedNextActionCommand`, `recommendedNextAction`,
`triageChecklist`, and `primaryFailure` copied from the packet so validation can
feed directly into the next agent command.

## Top-level fields

| Field | Type | Required | Meaning |
|---|---|---|---|
| `schema` | string | yes | Exact schema identifier. Current value: `luotsi-run-summary.v1`. |
| `generatedAt` | string | yes | RFC 3339 timestamp for when the packet was generated. |
| `artifactRoot` | string | yes | Artifact root the packet describes. |
| `status` | string | yes | Compact triage state: `needs_triage`, `passed_or_incomplete`, or `no_replay_metadata`. |
| `verdict` | string | yes | One-sentence human-readable interpretation of the status. |
| `sessionCount` | integer | yes | Number of replay sessions found in the artifact index. |
| `failureCount` | integer | yes | Number of replay sessions with failure signals. |
| `triageChecklist` | array | yes | Ordered machine-readable version of the 60-second triage checklist. |
| `failureSnapshot` | object or null | yes | Compact first-screen summary copied from `primaryFailure` for agents and human job summaries. |
| `primaryFailure` | object or null | yes | Best first failure to inspect, or `null` when no primary failure was found. |
| `recommendedNextAction` | object | yes | One best next command for the first minute of triage. |
| `entryPoints` | object | yes | Durable files written for this artifact root. |
| `commands` | array | yes | Additional exact replay commands for deeper inspection. |

## Check Result

`luotsi replay packet --artifacts <artifact-root> --check` returns a normal
Luotsi command envelope whose `data.schema` is `luotsi-run-summary-check.v1`.
The check result is intentionally shaped like a continuation packet: agents can
validate the existing files and keep following `recommended_next_action.command`,
`primary_failure.source_command`, or `triage_checklist[].command` without reopening
`run-summary.json`.

Because the check result is returned inside the normal Luotsi command envelope,
its `data` fields use snake_case. The persisted `run-summary.json` packet uses
the camelCase fields documented in the rest of this page.

| Field | Type | Meaning |
|---|---|---|
| `schema` | string | Exact schema identifier. Current value: `luotsi-run-summary-check.v1`. |
| `checked_at` | string | RFC 3339 timestamp for when validation ran. |
| `artifact_root` | string | Artifact root that was checked. |
| `packet_path` | string | Validated `run-summary.json` path. |
| `status` | string | Check status. Current success value: `valid`. |
| `packet_status` | string | Original packet `status`, such as `needs_triage`. |
| `session_count` | integer | Session count copied from the packet. |
| `failure_count` | integer | Failure count copied from the packet. |
| `recommended_next_action_command` | string | Convenience copy of `recommended_next_action.command`. |
| `failure_snapshot` | object or null | Compact failure snapshot copied from the packet. |
| `recommended_next_action` | object | Next action copied from the packet. |
| `triage_checklist` | array | Checklist copied from the packet. |
| `primary_failure` | object or null | Primary failure copied from the packet. |
| `run_summary_markdown_path` | string | Validated `run-summary.md` path. |

## `status`

`status` has these current values:

- `needs_triage`: failure signals were found; start with `recommendedNextAction.command`.
- `passed_or_incomplete`: replay metadata exists, but no failure signal was found. Inspect the timeline or write a capsule if the run still looks suspicious.
- `no_replay_metadata`: no replay metadata was found. Inspect the artifact index and verify the original command wrote session replay files.

Treat unknown future values as "read `verdict`, then fall back to `commands[]`
or `entryPoints.indexHtmlPath`."

## `triageChecklist`

`triageChecklist` is the structured form of the first Markdown section in
`run-summary.md`. It exists so agents do not need to parse Markdown to follow
the same 60-second path as humans.

The Markdown packet starts with `## At a Glance`, then `## Failure Snapshot`,
then `## Packet Gate`, a fenced `bash` block containing the exact
`luotsi replay packet --artifacts <artifact-root> --check` command for this
artifact root. It is followed by `## Copy-Paste Triage Commands`, a fenced
`bash` block containing the non-null checklist commands in order. This is the
fastest human handoff surface. `replay packet --check` verifies that the
at-a-glance summary, snapshot, and gate command exist and that every non-null
checklist command appears in the copy-paste block.

Each checklist item uses:

| Field | Type | Required | Meaning |
|---|---|---|---|
| `step` | integer | yes | One-based checklist order. |
| `action` | string | yes | Human-readable action for this step. |
| `command` | string or null | yes | Exact command for this step when one exists. |
| `rationale` | string | yes | Why this step belongs in the first minute. |

The first item must use `recommendedNextAction.command` as its `command`.

## `failureSnapshot`

`failureSnapshot` is the compact machine-readable form of the top Markdown
section. It is `null` when `primaryFailure` is `null`; otherwise it repeats the
fields a reviewer needs before choosing a deeper replay surface:

| Field | Meaning |
|---|---|
| `sessionId` | Replay session identifier from `primaryFailure.sessionId`. |
| `reason` | Session completion reason from `primaryFailure.reason`. |
| `target` | Device, package, or other target when known. |
| `scenario` | Scenario name or identifier when known. |
| `step` | Scenario step name or index when known. |
| `action` | Action that was running near the failure. |
| `message` | Failure message or condensed reason. |

`replay packet --check` requires `failureSnapshot` when `primaryFailure` is
present, verifies that it matches `primaryFailure`, and repeats it in
`luotsi-run-summary-check.v1` so an agent can answer "what failed?" directly
from the check envelope.

## `primaryFailure`

When present, `primaryFailure` identifies the replay session and the focused
failure evidence:

| Field | Meaning |
|---|---|
| `sessionKind` | Replay session kind, such as `view`, `inspect`, or `scenario`. |
| `sessionId` | Replay session identifier from `session-replay.json`. |
| `startedAt` | RFC 3339 session start timestamp. |
| `endedAt` | RFC 3339 session end timestamp. |
| `reason` | Session completion reason. |
| `exitCode` | Session exit code. |
| `target` | Device, package, or other target when replay metadata knows it. |
| `scenario` | Scenario name or identifier when replay metadata knows it. |
| `step` | Scenario step name or index when available. |
| `action` | Action that was running near the failure. |
| `message` | Failure message or condensed reason. |
| `timelinePath` | Timeline file containing the failure evidence. |
| `failureCapsulePath` | Failure capsule JSON path when the run captured one. |
| `sourceCommand` | Best available command to reopen the focused evidence. Prefer an exact timeline event command when available; otherwise Luotsi falls back to a capsule or timeline command so the primary failure remains actionable. |

When `primaryFailure` is present, `run-summary.md` also starts with
`## At a Glance` and `## Failure Snapshot` so a human reviewer sees the status,
next command, focused evidence command, session, reason, target, scenario, step,
action, and message before broader command lists. `replay packet --check`
verifies that every non-empty value in that snapshot appears in the Markdown
companion.

## `recommendedNextAction`

`recommendedNextAction` is the highest-signal next command. It uses:

| Field | Type | Required | Meaning |
|---|---|---|---|
| `kind` | string | yes | Stable machine-readable action kind, such as `scrub_failure` or `write_capsule`. |
| `title` | string | yes | Short human-readable label. |
| `reason` | string | yes | Why this command is the best next move. |
| `command` | string | yes | Exact command to run next. |

Agents should try `recommendedNextAction.command` before scanning the rest of the
packet.

## `entryPoints`

`entryPoints` uses these path fields:

| Field | Type | Required | Meaning |
|---|---|---|---|
| `indexHtmlPath` | string | yes | Refreshed artifact browser HTML entry point. |
| `indexMarkdownPath` | string | yes | Refreshed artifact index Markdown entry point. |
| `replayOpenJsonPath` | string or null | yes | Persisted `replay-open-summary.json` path when `replay open --write-json` wrote one. |
| `replayOpenMarkdownPath` | string or null | yes | Persisted `replay-open.md` path when `replay open --write-markdown` wrote one. |
| `runSummaryJsonPath` | string or null | yes | Persisted `run-summary.json` path when JSON was written. |
| `runSummaryMarkdownPath` | string or null | yes | Persisted `run-summary.md` path when Markdown was written. |

`replay packet` writes both run-summary paths. `replay open` only fills them
when `--write-json` and `--write-markdown` are supplied.

## `commands`

Each command item uses:

| Field | Type | Required | Meaning |
|---|---|---|---|
| `kind` | string | yes | Stable command kind. |
| `description` | string | yes | Short explanation of the command. |
| `command` | string | yes | Exact CLI command to run. |

The recommended action is intentionally duplicated as a command when it also
belongs in the broader command list. Consumers should still prefer
`recommendedNextAction.command`.

## Example

```json
{
  "schema": "luotsi-run-summary.v1",
  "generatedAt": "2026-06-10T12:00:00Z",
  "artifactRoot": "artifacts/luotsi-lab",
  "status": "needs_triage",
  "verdict": "Failure signals found. Start with the recommended next action before broad artifact browsing.",
  "sessionCount": 1,
  "failureCount": 1,
  "triageChecklist": [
    {
      "step": 1,
      "action": "Run the recommended packet command",
      "command": "luotsi replay scrub --artifacts artifacts/luotsi-lab --failures --context 3 --write-markdown",
      "rationale": "This is the highest-signal next command computed from replay metadata."
    },
    {
      "step": 2,
      "action": "Read the primary failure fields before opening broad artifacts",
      "command": "luotsi replay scrub --source-path session-timeline.jsonl --sequence 42 --context 3",
      "rationale": "Session identity and focused timeline evidence should be understood before broad artifact browsing."
    },
    {
      "step": 3,
      "action": "Use the commands section only after the focused failure window is understood",
      "command": null,
      "rationale": "Follow-up commands are useful after the first failure window is clear."
    }
  ],
  "failureSnapshot": {
    "sessionId": "checkout-20260610",
    "reason": "failed",
    "target": "emulator-5554",
    "scenario": "checkout",
    "step": "submit payment",
    "action": "tap",
    "message": "Expected confirmation text was not visible."
  },
  "primaryFailure": {
    "sessionKind": "scenario",
    "sessionId": "checkout-20260610",
    "startedAt": "2026-06-10T11:59:30Z",
    "endedAt": "2026-06-10T12:00:00Z",
    "reason": "failed",
    "exitCode": 1,
    "target": "emulator-5554",
    "scenario": "checkout",
    "step": "submit payment",
    "action": "tap",
    "message": "Expected confirmation text was not visible.",
    "timelinePath": "session-timeline.jsonl",
    "failureCapsulePath": "failure-capsule.json",
    "sourceCommand": "luotsi replay scrub --source-path session-timeline.jsonl --sequence 42 --context 3"
  },
  "recommendedNextAction": {
    "kind": "scrub_failure",
    "title": "Scrub the primary failure",
    "reason": "Failure metadata points at one timeline event.",
    "command": "luotsi replay scrub --artifacts artifacts/luotsi-lab --failures --context 3 --write-markdown"
  },
  "entryPoints": {
    "indexHtmlPath": "artifacts/luotsi-lab/index.html",
    "indexMarkdownPath": "artifacts/luotsi-lab/index.md",
    "replayOpenJsonPath": null,
    "replayOpenMarkdownPath": null,
    "runSummaryJsonPath": "artifacts/luotsi-lab/run-summary.json",
    "runSummaryMarkdownPath": "artifacts/luotsi-lab/run-summary.md"
  },
  "commands": [
    {
      "kind": "scrub",
      "description": "Open a focused previous/current/next event view around the failure.",
      "command": "luotsi replay scrub --artifacts artifacts/luotsi-lab --failures --context 3 --write-markdown"
    },
    {
      "kind": "capsule",
      "description": "Write a shareable failure capsule summary.",
      "command": "luotsi replay capsule --artifacts artifacts/luotsi-lab --write-readme --write-json"
    }
  ]
}
```

## Compatibility rules

- Unknown fields must be ignored.
- New required semantics should use a new `schema` value rather than changing the meaning of existing required fields in place.
- Consumers should accept both camelCase artifact JSON fields and snake_case command-envelope fields when they parse either persisted `run-summary.json` or the `data` object returned by `luotsi replay packet`.
- Consumers should check `schema` first, then use `recommendedNextAction.command` / `recommended_next_action.command` as the first next command.
- If `primaryFailure` is `null`, do not assume the run passed. Check `status`, `verdict`, and `sessionCount`.
- If `primaryFailure.sourceCommand` is present, use it as the focused evidence command after `recommendedNextAction.command`. It should not be treated as broad artifact browsing; it is the packet's best path back to the failure evidence.
- If `runSummaryJsonPath` or `runSummaryMarkdownPath` is `null`, the packet was returned in-memory by a command path that did not persist both files. Run `luotsi replay packet --artifacts <artifact-root>` to write them.
- A successful `luotsi-run-summary-check.v1` result repeats `recommendedNextAction`, `recommendedNextActionCommand`, `failureSnapshot`, `triageChecklist`, and `primaryFailure` from the packet so consumers can continue from the check envelope without reopening `run-summary.json`.
- `replay packet --check` is the contract gate for an existing packet. It must exit non-zero for missing JSON, invalid JSON, unsupported schema, stale `artifactRoot`, missing index entry points, missing `triageChecklist`, a first checklist item that does not point at `recommendedNextAction.command`, missing `recommendedNextAction.command`, a primary failure without `failureSnapshot`, a `failureSnapshot` that does not match `primaryFailure`, a primary failure without `primaryFailure.sourceCommand`, a checklist that omits `primaryFailure.sourceCommand`, missing `entryPoints.runSummaryJsonPath`, missing `entryPoints.runSummaryMarkdownPath`, a missing Markdown companion, Markdown without the at-a-glance summary, Markdown whose at-a-glance summary omits the recommended command or primary failure evidence command, Markdown without the failure snapshot, Markdown without the packet validation gate command, Markdown without the copy-paste triage command block, a copy-paste block that omits any non-null checklist command, Markdown without the 60-second triage checklist, Markdown that omits the JSON packet's recommended command, or Markdown that omits `primaryFailure.sourceCommand`.
