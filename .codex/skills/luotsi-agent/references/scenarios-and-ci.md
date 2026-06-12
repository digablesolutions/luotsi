# Scenarios And CI

## Scenario Lifecycle

Use scenarios for repeatable, reviewable Android flows.

```bash
luotsi scenario-init --file scenarios/smoke.json --name smoke --package <app.id>
luotsi scenario-validate --path scenarios
luotsi run --path scenarios --dry-run
luotsi run --path scenarios --validate-only
luotsi run --path scenarios --device <serial> --report-junit junit.xml --artifacts artifacts/scenario-run
```

Scenario files are JSON. They can include `setup`, required `steps`, and `teardown`. Use `metadata` to preserve package, activity, device, layout, orientation, and calibration notes. Luotsi warns when real device/app metadata differs.

Prefer `waitElement` / `tapElement` selectors or exact text over coordinates. Use coordinates only with layout metadata and validation.

Common actions include `waitVisible`, `waitElement`, `tapText`, `tapElement`, `tapPoint`, `typeText`, `keyevent`, `takeScreenshot`, `assertScreenshot`, `assertEvent`, `startApp`, `forceStop`, and package/app lifecycle checks. Confirm exact action fields with `docs/scenarios.md` or `luotsi scenario-explain --file <scenario.json>`.

## Promote Exploration To Scenarios

Use this conservative path when an agent explored a real app:

```bash
luotsi inspect --device <serial> --artifacts artifacts/explore
luotsi replay scenario-draft --artifacts <explore-root> --output scenarios/draft.json --validate --write-json --write-markdown
luotsi scenario-validate --file scenarios/draft.json
luotsi run --file scenarios/draft.json --dry-run
```

Generated drafts are review-required. Keep them explicit and small; do not imply natural-language autonomy.

## Shared Lab Safety

Use lab commands when devices are shared:

```bash
luotsi lab status --device-query "state=online,type=physical,availability=available"
luotsi lab plan --device-query "state=online,type=physical,availability=available"
luotsi lab claim --device-query "state=online,type=physical,availability=available" --owner <owner> --ttl-sec 3600 --claim-wait-sec 60
luotsi lab leases
luotsi lab release --serial <serial>
```

Production-safe run shape:

```bash
luotsi run --file <scenario.json> --device <serial> --package <app.id> --claim-device --claim-wait-sec 60 --report-junit junit.xml --artifacts artifacts/luotsi-lab
```

Use `LUOTSI_LAB_STATE_ROOT` when multiple runners/operators share leases, queue entries, quarantines, inventory, and device-health state.

## CI Packet Pattern

Preserve the scenario exit code, but still write packet artifacts when possible:

```bash
luotsi scenario-validate --path "$LUOTSI_SCENARIO_PATH"
luotsi run --path "$LUOTSI_SCENARIO_PATH" --device-query "$LUOTSI_DEVICE_QUERY" --claim-device --owner "$LUOTSI_OWNER" --ttl-sec "$LUOTSI_TTL_SEC" --report-junit "$LUOTSI_JUNIT_PATH" --artifacts "$LUOTSI_ARTIFACTS_DIR"
luotsi replay packet --artifacts "$LUOTSI_ARTIFACTS_DIR"
luotsi replay packet --artifacts "$LUOTSI_ARTIFACTS_DIR" --check
```

Read `docs/portable-physical-lab-ci.md` for reusable shell/PowerShell scripts.
