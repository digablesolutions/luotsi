# Contributing

Luotsi is intended to stay boring to operate and boring to automate. Keep
changes small, explicit, and easy to validate.

## Workflow

- Branch from `main` for each change.
- Open a pull request instead of pushing straight to `main`.
- Keep commits and PRs scoped to one change or one tightly related slice.
- Update docs when command behavior, flags, artifacts, or operator UX change.

## Validation

Use the pinned .NET SDK from `global.json` (`10.0.300`) for source builds and
tests. Release archives are self-contained and expose the installed binary as
`luotsi`, but the commands below are the contributor/source-tree workflow.

Run the normal .NET safety net from the repository root:

```powershell
dotnet build .\Luotsi.sln
dotnet test .\Luotsi.sln
```

If you only touched a narrow slice, run the focused test or command that proves
that slice first, then fall back to the full solution checks before merge.

If you changed the Android helper, also validate the helper build from
`Luotsi.ViewServer.Android`:

```powershell
.\gradlew.bat assembleDebug
```

## Design expectations

- Preserve the single JSON envelope contract for normal command mode.
- Preserve JSONL streaming semantics for `inspect` and `view` sessions.
- Prefer host-side orchestration over device-side complexity.
- Keep Android support ADB-first unless there is a concrete reason not to.
- Maintain artifact capture on device-facing failures.
- Prefer injected fakes in tests over real devices or real `adb`.

## Pull requests

PRs should explain:

- what changed
- how it was validated
- any contract, artifact, or UX impact

If the change affects command output, attach representative JSON or artifact
examples in the PR description.