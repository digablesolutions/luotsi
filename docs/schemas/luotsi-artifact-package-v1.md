# `luotsi-artifact-package.v1`

`luotsi-artifact-package.json` is the manifest embedded at the root of every `luotsi artifacts pack` zip. It is the durable handoff contract for replayable artifact packages.

## Top-level fields

| Field | Type | Required | Meaning |
|---|---|---|---|
| `schema` | string | yes | Exact schema identifier. Current value: `luotsi-artifact-package.v1`. |
| `run_id` | string | yes | Artifact root/run identifier used for the packaged bundle. |
| `created_at` | string | yes | RFC 3339 timestamp for when the package manifest was created. |
| `source_file_count` | integer | yes | Number of source artifact files included in the package, excluding the manifest itself. |
| `category_counts` | object | yes | Aggregate file-category counts for the packaged source files. |
| `recommended_commands` | array | yes | Suggested post-unpack commands. These use `<unpacked-artifact-root>` placeholders because the final local path is chosen at unpack time. |
| `files` | array of strings | yes | Ordered relative file paths included from the source artifact root. |

## `category_counts`

`category_counts` uses these integer fields:

- `screenshots`
- `videos`
- `reports`
- `logs`
- `timelines`
- `other`

## `recommended_commands`

Each `recommended_commands` item is an object with:

| Field | Type | Required | Meaning |
|---|---|---|---|
| `kind` | string | yes | Stable machine-readable command kind. |
| `summary` | string | yes | Short human-readable explanation. |
| `command` | string | yes | Suggested CLI command template. |

## Example

```json
{
  "schema": "luotsi-artifact-package.v1",
  "run_id": "20260526-120000-run",
  "created_at": "2026-05-26T12:00:00Z",
  "source_file_count": 2,
  "category_counts": {
    "screenshots": 0,
    "videos": 0,
    "reports": 0,
    "logs": 0,
    "timelines": 1,
    "other": 1
  },
  "recommended_commands": [
    {
      "kind": "info_artifacts",
      "summary": "Inspect the unpacked artifact root without opening it.",
      "command": "luotsi artifacts info <unpacked-artifact-root>"
    },
    {
      "kind": "replay_open",
      "summary": "Open the replay workbench for the unpacked artifact root.",
      "command": "luotsi replay open --artifacts <unpacked-artifact-root>"
    }
  ],
  "files": [
    "index.html",
    "session-timeline.jsonl"
  ]
}
```

## Compatibility rules

- Unknown fields must be ignored.
- Missing `luotsi-artifact-package.json` is invalid for supported packages and `artifacts unpack` fails with a usage error.
- Invalid manifest JSON or missing required fields must fail unpack validation early with a clear usage error.
- New manifest revisions should use a new `schema` value rather than changing the meaning of existing required fields in place.
