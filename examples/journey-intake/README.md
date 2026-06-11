# Journey Intake Examples

Use these files to capture Android CLI Journey-style intent before turning it
into a reviewed Luotsi scenario.

The intake file is not an executable Luotsi scenario. It is a handoff document
for an agent, engineer, or CI maintainer to record:

- the app and device context
- the user goal
- assertions that matter
- unsafe actions to avoid
- preferred Luotsi exploration and replay commands
- the review gate before any unattended run

Start from Luotsi's product handoff command:

```bash
luotsi journey-intake init --output journey-intake.json --package com.example.app --device <serial> --write-markdown
luotsi journey-intake validate --file journey-intake.json
luotsi journey-intake draft-scenario --file journey-intake.json --output scenarios/from-journey.json
```

`journey-intake init` writes a review-required `luotsi-journey-intake.v1` file
plus an optional Markdown handoff. The template and schema in this directory are
still useful for editors and agent tooling; keep the `$schema` reference when
copying or generating intake files. The Luotsi CLI validation command checks the
production-critical handoff fields before the file is turned into scenario work,
and returns a non-zero exit code when required guardrails or commands are
missing.

Then follow the public workflow:

```bash
luotsi doctor --device <serial> --fix
luotsi inspect --device <serial> --artifacts artifacts/journey-intake
luotsi scenario-validate --file scenarios/from-journey.json
luotsi replay scenario-draft --artifacts artifacts/journey-intake/<run-id> --output scenarios/from-replay.json --validate --write-markdown
luotsi run --file scenarios/from-journey.json --device <serial> --dry-run
luotsi run --file scenarios/from-journey.json --device <serial> --output-dir artifacts/from-journey-run
luotsi replay packet --artifacts artifacts/from-journey-run
luotsi replay packet --artifacts artifacts/from-journey-run --check
```

Keep generated scenarios review-required. Luotsi scenarios are explicit JSON
playbooks, not arbitrary natural-language execution.
