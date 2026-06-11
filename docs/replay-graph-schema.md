# Replay Graph Schema

`luotsi replay graph` emits `luotsi-replay-graph.v1`, a stable node and edge view over replay artifacts. It is intended for agents and humans that need to answer three questions quickly:

- what failed
- what changed or was observed around the failure
- what command can I run next

Graph is not the only replay entry point. `replay packet` is the production handoff for one artifact root, and graph actions intentionally include replay commands so an agent can move from semantic context back to the failure snapshot, recommended next action, and bundle follow-ups before raw artifact browsing.

For first-pass orientation, start with `luotsi replay packet --artifacts <artifact-root>` and validate shared packets with `luotsi replay packet --artifacts <artifact-root> --check` before asking for graph detail. Use `luotsi replay open --artifacts <artifact-root> --dry-run` when a human also needs the replay front door without launching a browser.

## Command

```text
luotsi replay graph --artifacts <artifact-root> [--failed] [--node-kind <kind>] [--edge-kind <kind>] [--action <text>] [--selector <text>] [--contains <text>] [--insight <kind>] [--severity info|warning|error] [--evidence <kind>] [--fact <text>] [--node <id> --depth 1] [--limit 200] [--format json|jsonl] [--write-json] [--write-jsonl] [--write-markdown]
```

Filtering returns a focused subgraph with one-hop context. `total_node_count` and `total_edge_count` describe the full graph before filtering; `node_count` and `edge_count` describe the returned view.

`--node <id> --depth <n>` returns a deterministic neighborhood around a graph node. Use it after a broad query finds a failure, selector, artifact, or generated step ID.

`--insight <kind>` and `--severity <level>` filter the `insights` array without changing the returned node and edge view. Use them when an agent only needs high-signal findings such as warning transitions or error failures.

`--contains <text>` searches node IDs, kinds, labels, properties, edge endpoints, edge kinds, and edge properties. Use it as the broad first query when you only know a failure phrase, selector text, artifact name, or telemetry value.

`--evidence <kind>` filters the promoted `evidence` array without changing the returned node and edge view. Current evidence kinds include `failure`, `artifact`, `selector`, `screen_state`, `telemetry_signal`, and `generated_step`.

`--fact <text>` filters the promoted `facts` array without changing the returned node and edge view. It searches fact category, subject, predicate, object, and source graph IDs.

## Top-Level Fields

| Field | Meaning |
|---|---|
| `schema` | Always `luotsi-replay-graph.v1` for this contract. |
| `artifact_root` | Root directory the graph was built from. |
| `query` | Applied graph query: node kind, edge kind, action text, selector text, failure-only flag, and limit. |
| `node_count`, `edge_count` | Returned graph size after filters. |
| `total_node_count`, `total_edge_count` | Full graph size before filters. |
| `matched_node_count`, `matched_edge_count` | Query match size before `--limit` is applied. |
| `truncated` | `true` when `--limit` capped matched nodes or edges. |
| `node_kinds`, `edge_kinds` | Counts for the returned graph view. |
| `taxonomy` | Machine-readable node, edge, and evidence kind descriptions plus query examples. |
| `agent_summary` | Compact answers for what failed, what changed, what command to run next, and the first promoted evidence node IDs. |
| `insights` | Agent-readable highlights such as failures, selectors, telemetry, and scenario-draft provenance. |
| `actions` | Suggested next commands for capsule, opening artifacts, scrubbing, streaming, or narrowing the graph. |
| `evidence_kinds` | Counts for promoted evidence records in the returned graph view. |
| `evidence` | Compact promoted proof records from returned graph nodes: failures, artifacts, selectors, screen observations, telemetry signals, and generated steps. Each record includes nearby `edge_ids` so agents can trace why the proof is connected. |
| `facts` | Compact subject-predicate-object facts derived from the returned graph view. Facts are the preferred agent input when the caller needs stable semantic statements instead of raw graph traversal. |
| `causal_chains` | Compact causal paths from preceding timeline transitions into failure nodes. Use these before raw graph traversal when asking what led to a failure. |
| `hypotheses` | Ranked likely-cause hints derived from causal chains and evidence. Each hypothesis includes severity, confidence, support IDs, and a follow-up command. |
| `failure_paths` | Compact paths from nearby timeline context into failure nodes. |
| `json_path`, `jsonl_path`, `markdown_path` | Artifact paths when `--write-json`, `--write-jsonl`, or `--write-markdown` are used. |
| `nodes`, `edges` | Stable graph payload. |

## Node Taxonomy

| Kind | Meaning |
|---|---|
| `session` | One `session-replay.json` session. |
| `event` | One normalized timeline event from `session-timeline.jsonl`. |
| `failure` | Failure-relevant timeline event or terminal failure. |
| `failure_capsule` | `failure-capsule.json` summary node. |
| `scenario` | Scenario entry from a failure capsule. |
| `artifact` | Screenshot, logcat, hierarchy, report, or metadata file linked to a scenario. |
| `action` | Promoted action or command such as `waitVisible`, `tap_text`, or `take_screenshot`. |
| `selector` | Promoted text or structured element selector. |
| `screen_state` | Screen or screenshot observation. |
| `telemetry_signal` | Semantic telemetry signal emitted by the app or observed by Luotsi. |
| `scenario_draft` | Generated scenario draft summary. |
| `generated_step` | Step generated by `replay scenario-draft`. |
| `draft_source` | Source family for generated steps and draft normalizations, such as inspect command or telemetry. |
| `draft_normalization` | Stabilization or cleanup normalization applied while generating a draft. |

Scenario-draft `generated_step` and `draft_normalization` nodes preserve audit fields when the draft summary contains them: `source_path`, `sequence`, `timestamp`, and `source_command`. Use those fields to jump from graph context back to the exact `replay timeline` event that produced a step or normalization.

## Edge Taxonomy

| Kind | Meaning |
|---|---|
| `next` | Timeline ordering within one source timeline file. |
| `transitions_to` | Semantic timeline transition with `from_type`, `to_type`, `category`, details, and optional `elapsed_ms`. |
| `indicates` | Event points to a failure node. |
| `has_capsule` | Session has a failure capsule. |
| `contains` | Capsule contains a scenario. |
| `has_artifact` | Scenario links to an artifact. |
| `describes_action` | Event describes an action node. |
| `mentions_selector` | Event mentions a selector node. |
| `observes_screen` | Event observes a screen/screenshot state. |
| `observes_telemetry` | Event observes a semantic telemetry signal. |
| `generates_step` | Scenario draft generates a step. |
| `derived_from` | Generated step or draft normalization came from a source event family. |
| `uses_source` | Scenario draft uses a source family. |
| `applies_normalization` | Scenario draft applied a normalization. |

## Evidence Taxonomy

| Kind | Meaning |
|---|---|
| `failure` | Failure node proof with nearby graph edge IDs and a scrub command. |
| `artifact` | Linked artifact proof such as screenshots, logs, reports, hierarchies, or metadata. |
| `selector` | Promoted selector proof with a graph query command for local context. |
| `screen_state` | Screen or screenshot observation proof. |
| `telemetry_signal` | Semantic telemetry proof observed in timeline events. |
| `generated_step` | Scenario-draft generated step proof with provenance context. |

## Fact Contract

Each `facts[]` item has:

| Field | Meaning |
|---|---|
| `category` | Fact family: `failure`, `transition`, `evidence`, `selector`, or `action`. |
| `subject`, `predicate`, `object` | Stable semantic statement derived from graph nodes and edges. |
| `confidence` | Heuristic confidence from 0.0 to 1.0. Failure paths and direct evidence are highest confidence. |
| `node_ids`, `edge_ids` | Source graph IDs that justify the fact. |
| `command` | Optional follow-up command that reopens or narrows the context for the fact. |

## Causal Chain Contract

Each `causal_chains[]` item has:

| Field | Meaning |
|---|---|
| `failure_node_id` | Failure node reached by the chain. |
| `summary` | Human-readable path summary. |
| `hops` | Ordered edge-derived hops with `from`, `to`, `relation`, optional transition `category`, and optional detail text. |
| `command` | Follow-up graph command that opens the failure neighborhood. |

## Hypothesis Contract

Each `hypotheses[]` item has:

| Field | Meaning |
|---|---|
| `kind` | Hypothesis family such as `action_to_failure` or `failure_evidence`. |
| `severity` | `info`, `warning`, or `error`. |
| `summary` | Short likely-cause hint. |
| `confidence` | Heuristic confidence from 0.0 to 1.0. |
| `evidence_node_ids`, `edge_ids` | Supporting graph IDs. |
| `command` | Follow-up command that opens supporting graph context. |

## Agent Queries

```text
luotsi replay open --artifacts artifacts/run --dry-run
luotsi replay capsule --artifacts artifacts/run --write-readme --write-json
luotsi replay graph --artifacts artifacts/run --failed --write-markdown
luotsi replay graph --artifacts artifacts/run --format jsonl
luotsi replay graph --artifacts artifacts/run --write-jsonl
luotsi replay graph --artifacts artifacts/run --contains "not visible" --write-markdown
luotsi replay graph --artifacts artifacts/run --evidence artifact --format jsonl
luotsi replay graph --artifacts artifacts/run --fact action_to_failure --format jsonl
luotsi replay graph --artifacts artifacts/run --severity warning --write-markdown
luotsi replay graph --artifacts artifacts/run --insight transition --severity warning --format json
luotsi replay graph --artifacts artifacts/run --node-kind selector --write-markdown
luotsi replay graph --artifacts artifacts/run --action waitVisible --limit 50
luotsi replay graph --artifacts artifacts/run --selector "Sign in" --limit 50
luotsi replay graph --artifacts artifacts/run --edge-kind has_artifact --limit 50
luotsi replay graph --artifacts artifacts/run --edge-kind transitions_to --limit 50
luotsi replay graph --artifacts artifacts/run --node failure:session-timeline.jsonl:3 --depth 2
```

`replay-graph.md` starts with "Agent Summary", "What Failed", "What Agents Can Act On", "Evidence", "Facts", "Causal Chains", "Hypotheses", and "Insights" before the raw node and edge tables.

JSONL output includes `summary`, `failure_path`, `evidence`, `causal_chain`, `hypothesis`, `fact`, `insight`, `node`, and `edge` line types. The `summary` line includes `agent_summary`, `node_kinds`, `edge_kinds`, and `evidence_kinds` so agents can decide whether to consume later lines. Use `replay packet` first when an agent needs the durable first-minute packet, then use `agent_summary.commands` for capsule/graph/scrub follow-ups from `actions`. Use `hypothesis` lines when an agent needs ranked likely-cause hints; use `causal_chain` lines when it needs the shortest path into a failure; use `fact` lines when it needs concise semantic statements; use `evidence` lines when it needs proof before deciding whether to open the full graph.
