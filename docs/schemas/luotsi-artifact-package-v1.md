# `luotsi-artifact-package.v1`

`luotsi-artifact-package.json` is the manifest embedded at the root of every `luotsi artifacts pack` zip. It is the durable handoff contract for replayable artifact packages. Use `luotsi artifacts verify <artifact.zip> [--sha256 <digest>]` to validate the manifest, archive entries, SHA-256, and redaction status without extracting the package. Use `luotsi artifacts verify <artifact.zip> --require-lab-safe [--sha256 <digest>]` or `luotsi artifacts unpack <artifact.zip> --require-lab-safe [--sha256 <digest>]` when support or CI must reject unredacted packages before unpacking.

Use `luotsi artifacts info <artifact.zip>` to inspect and validate a package manifest, redaction metadata, SHA-256, and unpack/replay/capsule commands before extracting files. Use `luotsi artifacts intake <artifact.zip> --require-lab-safe --write-json --write-readme --sha256 <digest>` to enforce lab-safe redaction, verify package bytes, restore the artifact root, persist the intake audit summary, and return info/open/replay/capsule commands in one step. The lower-level `luotsi artifacts unpack <artifact.zip> --require-lab-safe --sha256 <digest>` command enforces the same gates before extraction; failures happen before files are written.

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
      "kind": "replay_open",
      "summary": "Open the replay front door for the unpacked artifact root.",
      "command": "luotsi replay open --artifacts <unpacked-artifact-root>"
    },
    {
      "kind": "info_artifacts",
      "summary": "Inspect the unpacked artifact root without opening it.",
      "command": "luotsi artifacts info <unpacked-artifact-root>"
    },
    {
      "kind": "open_artifacts",
      "summary": "Open the unpacked artifact root in the generic artifact browser.",
      "command": "luotsi artifacts open <unpacked-artifact-root>"
    },
    {
      "kind": "replay_capsule",
      "summary": "Write a replay capsule summary for handoff triage.",
      "command": "luotsi replay capsule --artifacts <unpacked-artifact-root> --write-json --write-readme"
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
- The first command should be `replay_open` so humans and agents see the replay front door before the generic artifact browser.
- Use `info_artifacts` for a non-mutating file/category check, and `open_artifacts` only when you specifically need the generic artifact browser.
- Missing `redaction` means the package was created before redaction metadata existed or with the default exact-copy policy.
- `artifacts verify --require-lab-safe` treats missing `redaction` or any non-`lab-safe` mode as a blocked handoff gate and exits non-zero while still reporting manifest/SHA details.
- `artifacts unpack --require-lab-safe` treats missing `redaction` or any non-`lab-safe` mode as a usage error before files are extracted.
- `artifacts intake --require-lab-safe` uses the same extraction-time gate as unpack, then reports whether the package was only `validated` (`--dry-run`) or `restored`; when `--write-json` or `--write-readme` is used on a restore, Luotsi writes `artifact-intake-summary.json` with schema `luotsi-artifact-intake.v1` or `artifact-intake.md` into the restored root and refreshes the artifact index.
- Older `artifact-intake-summary.json` files without a top-level schema remain readable by artifact indexes and replay capsules when the filename matches the persisted intake summary.
- Missing `luotsi-artifact-package.json` is invalid for supported packages and `artifacts info` / `artifacts verify` / `artifacts unpack` / `artifacts intake` fail with a usage error.
- Invalid manifest JSON or missing required fields must fail info/verify/unpack/intake validation early with a clear usage error.
- New manifest revisions should use a new `schema` value rather than changing the meaning of existing required fields in place.
