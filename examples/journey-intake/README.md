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

Start from the template:

```bash
cp examples/journey-intake/evidence-backed-journey-intake.template.json journey-intake.json
luotsi journey-intake validate --file journey-intake.json
```

The template points at `luotsi-journey-intake.schema.json`, which documents the
stable `luotsi-journey-intake.v1` handoff shape. Keep that schema reference when
copying the file so editors and agent tooling can check the required fields.
The Luotsi CLI validation command checks the production-critical handoff fields
before the file is turned into scenario work, and returns a non-zero exit code
when required guardrails or commands are missing.

Then follow the public workflow:

```bash
luotsi doctor --device <serial> --fix
luotsi inspect --device <serial> --artifacts artifacts/journey-intake
luotsi replay scenario-draft --artifacts artifacts/journey-intake/<run-id> --output scenarios/from-journey.json --validate --write-markdown
luotsi run --file scenarios/from-journey.json --device <serial> --dry-run
luotsi run --file scenarios/from-journey.json --device <serial> --output-dir artifacts/from-journey-run
luotsi replay open --artifacts artifacts/from-journey-run --dry-run
```

Keep generated scenarios review-required. Luotsi scenarios are explicit JSON
playbooks, not arbitrary natural-language execution.
