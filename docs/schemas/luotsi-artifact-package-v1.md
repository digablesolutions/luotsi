# `luotsi-artifact-package.v1`

`luotsi-artifact-package.json` is the manifest embedded at the root of every `luotsi artifacts pack` zip. It is the durable handoff contract for replayable artifact packages. Use `luotsi artifacts verify <artifact.zip>` to validate the manifest, archive entries, SHA-256, and redaction status without extracting the package. Use `luotsi artifacts verify <artifact.zip> --require-lab-safe` when support or CI must reject unredacted packages before unpacking.

Use `luotsi artifacts info <artifact.zip>` to inspect and validate a package manifest, redaction metadata, SHA-256, and unpack/replay commands before extracting files. Use `luotsi artifacts unpack <artifact.zip> --sha256 <digest>` to verify package bytes before extraction; mismatches fail before files are written.

## Top-level fields

| Field | Type | Required | Meaning |
|---|---|---|---|
| `schema` | string | yes | Exact schema identifier. Current value: `luotsi-artifact-package.v1`. |
| `run_id` | string | yes | Artifact root/run identifier used for the packaged bundle. |
| `created_at` | string | yes | RFC 3339 timestamp for when the package manifest was created. |
| `source_file_count` | integer | yes | Number of source artifact files included in the package, excluding the manifest itself. |
| `category_counts` | object | yes | Aggregate file-category counts for the packaged source files. |
| `redaction` | object | no | Optional packaging redaction policy and counts. Present when `artifacts pack --redact lab-safe` was used. |
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

## `redaction`

`redaction` is omitted for default exact-copy packages. When present, it uses these fields:

| Field | Type | Required | Meaning |
|---|---|---|---|
| `mode` | string | yes | Redaction mode used while writing zip entries. Current values: `lab-safe` or `off`. |
| `redacted_file_count` | integer | yes | Number of text-like package entries whose contents changed during packaging. |
| `text_file_count` | integer | yes | Number of package entries considered text-like by the packaging policy. |

`lab-safe` is conservative and intended for support, CI, and agent handoff. It redacts obvious secrets from text-like entries and copies binary media byte-for-byte; it does not mutate source artifact files.

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
  "redaction": {
    "mode": "lab-safe",
    "redacted_file_count": 1,
    "text_file_count": 1
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
- Missing `redaction` means the package was created before redaction metadata existed or with the default exact-copy policy.
- `artifacts verify --require-lab-safe` treats missing `redaction` or any non-`lab-safe` mode as a blocked handoff gate and exits non-zero while still reporting manifest/SHA details.
- Missing `luotsi-artifact-package.json` is invalid for supported packages and `artifacts info` / `artifacts verify` / `artifacts unpack` fail with a usage error.
- Invalid manifest JSON or missing required fields must fail info/verify/unpack validation early with a clear usage error.
- New manifest revisions should use a new `schema` value rather than changing the meaning of existing required fields in place.
